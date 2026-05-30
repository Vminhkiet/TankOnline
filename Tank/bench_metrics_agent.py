#!/usr/bin/env python3
"""
bench_metrics_agent.py
─────────────────────────────────────────────────────────────────────────────
Python sidecar cho benchmark worst-case GPC.
Chạy trên WSL2 song song với bench_worst_case.exe trên Windows.

Pipeline:
  bench_worst_case.exe → bench_worst_case.log
       │ (mount qua WSL2 /mnt/d/...)
       ▼
  bench_metrics_agent.py (port 9101)
       │ GET /metrics/prometheus  (Prometheus text format)
       ▼
  Prometheus :9090  (scrape mỗi 5s)
       ▼
  Grafana :3000  (Dashboard "GPC Worst-Case Benchmark")

Chạy:
  python3 bench_metrics_agent.py [--log /path/to/bench_worst_case.log]

Stop: Ctrl-C

Metrics được expose:
  bench_bullet_us_avg / p99 / max
  bench_physics_us_avg / p99 / max
  bench_snapshot_us_avg / p99 / max
  bench_total_us_avg / p99 / max
  bench_overruns_total
  bench_gpc
  bench_budget_used_pct
  bench_ticks_measured
  bench_active (0=waiting, 1=warming_up, 2=measuring, 3=done)
"""

import re
import time
import threading
import argparse
import os
from http.server import HTTPServer, BaseHTTPRequestHandler

# ── Cấu hình mặc định ─────────────────────────────────────────────────────────
DEFAULT_LOG = "/mnt/d/Unity/TankOnline/Tank/out/build/x64-Release/bench_worst_case/bench_worst_case.log"
AGENT_PORT  = 9101
BUDGET_US   = 16667.0

# ── Regex patterns ────────────────────────────────────────────────────────────
# [BenchPhase] bullet avg=188us | physics avg=3285us | snap avg=59us | total avg=3536us | overruns=0/600
PHASE_RE = re.compile(
    r'\[BenchPhase\]\s+'
    r'bullet avg=(\d+)us.*?'
    r'physics avg=(\d+)us.*?'
    r'snap avg=(\d+)us.*?'
    r'total avg=(\d+)us.*?'
    r'overruns=(\d+)/(\d+)'
)

# [BenchFinal] phase=bullet    avg=188us p50=185us p95=260us p99=295us max=527us
FINAL_RE = re.compile(
    r'\[BenchFinal\]\s+phase=(\w+)\s+'
    r'avg=(\d+)us\s+p50=(\d+)us\s+p95=(\d+)us\s+p99=(\d+)us\s+max=(\d+)us'
)

# [BenchFinal] GPC=floor(16667/4728)=3 match/core
GPC_RE = re.compile(
    r'\[BenchFinal\]\s+GPC=floor\(\d+/(\d+)\)=(\d+)'
)

# [BenchFinal] overruns=0/6000 (0.000%)
OVERRUN_RE = re.compile(
    r'\[BenchFinal\]\s+overruns=(\d+)/(\d+)'
)

# [BenchFinal] budget_us=16667 total_p99=4728us budget_used=28.4%
BUDGET_RE = re.compile(
    r'\[BenchFinal\]\s+budget_us=\d+\s+total_p99=(\d+)us\s+budget_used=([\d.]+)%'
)

# ── Shared state ──────────────────────────────────────────────────────────────
_lock  = threading.Lock()
_state = {
    # Rolling window (updated every LOG_INTERVAL ticks = 10s)
    "bullet_avg_us":   None,
    "physics_avg_us":  None,
    "snap_avg_us":     None,
    "total_avg_us":    None,
    "overruns":        0,
    "ticks_so_far":    0,
    # Final percentiles (after benchmark completes)
    "final": {},        # {phase: {avg, p50, p95, p99, max}}
    "gpc":              None,
    "total_p99_us":     None,
    "budget_used_pct":  None,
    # Status: 0=waiting 1=warmup 2=measuring 3=done
    "bench_active":     0,
}

# ── Log tailer ────────────────────────────────────────────────────────────────
def tail_log(log_path: str):
    print(f"[agent] Waiting for log file: {log_path}")
    while not os.path.exists(log_path):
        time.sleep(1)

    with _lock:
        _state["bench_active"] = 1   # warming up

    print(f"[agent] Tailing {log_path} ...")
    with open(log_path, "r", encoding="utf-8", errors="replace") as f:
        f.seek(0)
        while True:
            # Detect log rotation
            try:
                cur_size = os.path.getsize(log_path)
                if cur_size < f.tell():
                    f.seek(0)
            except OSError:
                pass

            line = f.readline()
            if not line:
                time.sleep(0.1)
                continue

            line = line.rstrip()

            # Warmup → measuring transition
            if "Starting" in line and "tick measurement" in line:
                with _lock:
                    _state["bench_active"] = 2   # measuring
                print(f"[agent] Phase: MEASURING")
                continue

            # Rolling window flush
            m = PHASE_RE.search(line)
            if m:
                bu, pu, su, tot, ov, tks = (int(x) for x in m.groups())
                with _lock:
                    _state["bullet_avg_us"]  = bu
                    _state["physics_avg_us"] = pu
                    _state["snap_avg_us"]    = su
                    _state["total_avg_us"]   = tot
                    _state["overruns"]       = ov
                    _state["ticks_so_far"]   = tks
                print(f"[agent] [Phase] bullet={bu}µs physics={pu}µs snap={su}µs "
                      f"total={tot}µs overruns={ov}/{tks}")
                continue

            # Final percentiles per phase
            m = FINAL_RE.search(line)
            if m:
                phase, avg, p50, p95, p99, mx = m.group(1), int(m.group(2)), \
                    int(m.group(3)), int(m.group(4)), int(m.group(5)), int(m.group(6))
                with _lock:
                    _state["final"][phase] = {
                        "avg": avg, "p50": p50, "p95": p95, "p99": p99, "max": mx
                    }
                print(f"[agent] [Final] {phase}: avg={avg} p99={p99} max={mx}")
                continue

            # GPC
            m = GPC_RE.search(line)
            if m:
                p99_us, gpc_val = int(m.group(1)), int(m.group(2))
                with _lock:
                    _state["gpc"]         = gpc_val
                    _state["total_p99_us"] = p99_us
                print(f"[agent] [Final] GPC={gpc_val}  total_P99={p99_us}µs")
                continue

            # Budget used
            m = BUDGET_RE.search(line)
            if m:
                p99_us = int(m.group(1))
                bpct   = float(m.group(2))
                with _lock:
                    _state["total_p99_us"]  = p99_us
                    _state["budget_used_pct"] = bpct
                continue

            # Overrun final line
            m = OVERRUN_RE.search(line)
            if m and "BenchFinal" in line:
                ov, tks = int(m.group(1)), int(m.group(2))
                with _lock:
                    _state["overruns"]     = ov
                    _state["ticks_so_far"] = tks
                continue

            # Done
            if "Done. Log:" in line:
                with _lock:
                    _state["bench_active"] = 3
                print("[agent] Benchmark DONE.")
                break

    print("[agent] Log tailer finished.")


# ── Prometheus text format ────────────────────────────────────────────────────
def _prom(s: dict) -> bytes:
    lines = []

    def g(name, value, help_text, labels=""):
        if value is None:
            return
        tag = f'{{{labels}}}' if labels else ""
        lines.append(f"# HELP {name} {help_text}")
        lines.append(f"# TYPE {name} gauge")
        lines.append(f"{name}{tag} {value}")

    def c(name, value, help_text):
        if value is None:
            return
        lines.append(f"# HELP {name} {help_text}")
        lines.append(f"# TYPE {name} counter")
        lines.append(f"{name}_total {value}")

    g("bench_active",           s.get("bench_active"),         "0=waiting 1=warmup 2=measuring 3=done")
    g("bench_ticks_measured",   s.get("ticks_so_far"),         "Ticks measured so far")
    g("bench_bullet_us_avg",    s.get("bullet_avg_us"),        "Rolling avg: updateBullets µs per tick")
    g("bench_physics_us_avg",   s.get("physics_avg_us"),       "Rolling avg: runPhysics µs per tick")
    g("bench_snapshot_us_avg",  s.get("snap_avg_us"),          "Rolling avg: getSnapshot µs per tick")
    g("bench_total_us_avg",     s.get("total_avg_us"),         "Rolling avg: total game logic µs per tick")
    c("bench_overruns",         s.get("overruns"),             "Ticks exceeding 16667µs budget")
    g("bench_gpc",              s.get("gpc"),                  "GPC = floor(16667 / total_P99_us)")
    g("bench_total_p99_us",     s.get("total_p99_us"),         "Total tick P99 latency µs (sum of phases)")
    g("bench_budget_used_pct",  s.get("budget_used_pct"),      "Budget used at P99 (pct of 16667µs)")
    g("bench_budget_us",        BUDGET_US,                     "Tick budget (16667µs = 60Hz)")

    # Per-phase final percentiles
    for phase, vals in (s.get("final") or {}).items():
        lbl = f'phase="{phase}"'
        g("bench_phase_avg_us", vals["avg"], f"Final avg latency µs — {phase} phase", lbl)
        g("bench_phase_p50_us", vals["p50"], f"Final P50 latency µs — {phase} phase", lbl)
        g("bench_phase_p95_us", vals["p95"], f"Final P95 latency µs — {phase} phase", lbl)
        g("bench_phase_p99_us", vals["p99"], f"Final P99 latency µs — {phase} phase", lbl)
        g("bench_phase_max_us", vals["max"], f"Final max latency µs — {phase} phase", lbl)

    return ("\n".join(lines) + "\n").encode("utf-8")


# ── HTTP handler ──────────────────────────────────────────────────────────────
class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        path = self.path.rstrip("/")
        if path in ("/metrics/prometheus", "/metrics"):
            with _lock:
                body = _prom(_state)
            self.send_response(200)
            self.send_header("Content-Type", "text/plain; version=0.0.4; charset=utf-8")
            self.end_headers()
            self.wfile.write(body)
        elif path == "/status":
            import json
            with _lock:
                body = json.dumps(_state, indent=2).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(body)
        else:
            self.send_response(404)
            self.end_headers()

    def log_message(self, fmt, *args):
        pass   # suppress per-request logs


# ── Entry point ───────────────────────────────────────────────────────────────
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Bench metrics agent for Grafana")
    parser.add_argument("--log",  default=DEFAULT_LOG,
                        help="Path to bench_worst_case.log")
    parser.add_argument("--port", type=int, default=AGENT_PORT,
                        help="HTTP port for Prometheus scrape")
    args = parser.parse_args()

    print("=" * 60)
    print("Bench Metrics Agent — SE315.Q21 GPC Benchmark")
    print(f"  Log file : {args.log}")
    print(f"  Port     : http://localhost:{args.port}/metrics/prometheus")
    print(f"  Status   : http://localhost:{args.port}/status")
    print("=" * 60)

    t = threading.Thread(target=tail_log, args=(args.log,), daemon=True, name="log-tailer")
    t.start()

    server = HTTPServer(("0.0.0.0", args.port), Handler)
    print(f"[agent] Serving on port {args.port} ...")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[agent] Stopped.")
