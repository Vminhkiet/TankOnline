#!/usr/bin/env python3
"""
bench_32match_agent.py
─────────────────────────────────────────────────────────────────────────────
Python sidecar cho benchmark 32-match concurrent.
Parse log từ bench_32match.exe và expose Prometheus metrics trên port 9102.

Pipeline:
  bench_32match.exe  (Windows, stdout)
       │  (redirect vào log file)
       ▼
  bench_32match_agent.py --log /mnt/.../bench_32match.log
       │  GET /metrics/prometheus  (port 9102)
       ▼
  Prometheus :9090  (scrape mỗi 5s)
       ▼
  Grafana :3000  (Dashboard "32-Match Concurrent Benchmark")

Log lines được parse:
  [M32Perf]  ticks=N matches=32 workers=8 mask=0x3FC | overruns=N
  [M32Phase] bullet avg=Xus p95=Xus p99=Xus max=Xus
             | physics avg=Xus p95=Xus p99=Xus max=Xus
             | snap avg=Xus p95=Xus p99=Xus max=Xus
  [M32Match] id=M bullet=Xus physics=Xus snap=Xus total=Xus

Chạy (WSL2):
  python3 Tank/bench_32match_agent.py \\
    --log /mnt/d/Unity/TankOnline/Tank/out/build/x64-Release/bench_32_match/bench_32match.log
"""

import re
import time
import threading
import argparse
import os
from http.server import HTTPServer, BaseHTTPRequestHandler

# ── Cấu hình ──────────────────────────────────────────────────────────────────
DEFAULT_LOG = "/mnt/d/Unity/TankOnline/Tank/out/build/x64-Release/bench_32_match/bench_32match.log"
AGENT_PORT  = 9102
BUDGET_US   = 16667.0

# ── Regex patterns ────────────────────────────────────────────────────────────
# [M32Perf] ticks=1200 matches=32 workers=8 mask=0x3FC | overruns=45
PERF_RE = re.compile(
    r'\[M32Perf\]\s+ticks=(\d+)\s+matches=(\d+)\s+workers=(\d+).*?'
    r'\|\s+overruns=(\d+)'
)

# [M32Phase] bullet avg=1309us p95=2100us p99=2800us max=5200us
#            | physics avg=28100us p95=45000us p99=52000us max=89000us
#            | snap avg=669us p95=900us p99=1200us max=2100us
PHASE_RE = re.compile(
    r'\[M32Phase\]\s+'
    r'bullet avg=(\d+)us p95=(\d+)us p99=(\d+)us max=(\d+)us\s*'
    r'\|\s*physics avg=(\d+)us p95=(\d+)us p99=(\d+)us max=(\d+)us\s*'
    r'\|\s*snap avg=(\d+)us p95=(\d+)us p99=(\d+)us max=(\d+)us'
)

# [M32Match] id=10000 bullet=1400us physics=27500us snap=650us total=29550us
MATCH_RE = re.compile(
    r'\[M32Match\]\s+id=(\d+)\s+'
    r'bullet=(\d+)us\s+physics=(\d+)us\s+snap=(\d+)us\s+total=(\d+)us'
)

# [M32Concurrent] wall_avg=40665us sum_match_avg=283031us parallelism=6.9x workers=8 (sequential_would_be=1301280us)
CONCURRENT_RE = re.compile(
    r'\[M32Concurrent\]\s+'
    r'wall_avg=(\d+)us\s+sum_match_avg=(\d+)us\s+'
    r'parallelism=([\d.]+)x\s+workers=(\d+)\s+'
    r'\(sequential_would_be=(\d+)us\)'
)

# ── Shared state ──────────────────────────────────────────────────────────────
_lock  = threading.Lock()
_state = {
    # From [M32Concurrent] — bằng chứng song song
    "wall_avg_us":          None,   # thời gian thực chạy 32 match
    "sum_match_avg_us":     None,   # tổng nếu đếm từng match
    "parallelism_factor":   None,   # ≈ N_WORKERS nếu song song
    "sequential_would_be":  None,   # thời gian nếu chạy tuần tự
    # From [M32Perf]
    "ticks":          0,
    "matches":        32,
    "workers":        8,
    "overruns":       0,
    # From [M32Phase] — 3-phase avg
    "bullet_avg":     None,
    "bullet_p95":     None,
    "bullet_p99":     None,
    "bullet_max":     None,
    "physics_avg":    None,
    "physics_p95":    None,
    "physics_p99":    None,
    "physics_max":    None,
    "snap_avg":       None,
    "snap_p95":       None,
    "snap_p99":       None,
    "snap_max":       None,
    # Derived
    "total_avg":      None,
    "physics_share":  None,  # physics_avg / total_avg * 100
    # Per-match breakdown (last window)
    "per_match": {},          # {match_id: {bullet, physics, snap, total}}
    # Status
    "active": 0,              # 0=waiting 1=warmup 2=running
    "last_update": 0.0,
}

# ── Log tailer ────────────────────────────────────────────────────────────────
def tail_log(log_path: str):
    print(f"[agent] Waiting for log: {log_path}")
    while not os.path.exists(log_path):
        time.sleep(1)

    with _lock:
        _state["active"] = 1

    print(f"[agent] Tailing {log_path} ...")
    with open(log_path, "r", encoding="utf-8", errors="replace") as f:
        f.seek(0)
        while True:
            try:
                cur_size = os.path.getsize(log_path)
                if cur_size < f.tell():
                    f.seek(0)
            except OSError:
                pass

            line = f.readline()
            if not line:
                time.sleep(0.05)
                continue
            line = line.rstrip()

            # Warmup done → measuring
            if "Warmup done" in line or "Streaming" in line:
                with _lock:
                    _state["active"] = 2
                print("[agent] Phase: MEASURING")
                continue

            # [M32Concurrent]
            m = CONCURRENT_RE.search(line)
            if m:
                wall, summ, par, workers, seq = \
                    int(m.group(1)), int(m.group(2)), \
                    float(m.group(3)), int(m.group(4)), int(m.group(5))
                with _lock:
                    _state["wall_avg_us"]         = wall
                    _state["sum_match_avg_us"]     = summ
                    _state["parallelism_factor"]   = par
                    _state["sequential_would_be"]  = seq
                print(f"[agent] [Concurrent] wall={wall}us sum={summ}us "
                      f"parallelism={par}x (sequential_would_be={seq}us)")
                continue

            # [M32Perf]
            m = PERF_RE.search(line)
            if m:
                ticks, matches, workers, overruns = [int(x) for x in m.groups()]
                with _lock:
                    _state["ticks"]    = ticks
                    _state["matches"]  = matches
                    _state["workers"]  = workers
                    _state["overruns"] = overruns
                continue

            # [M32Phase]
            m = PHASE_RE.search(line)
            if m:
                (ba, b95, b99, bmax,
                 pa, p95, p99, pmax,
                 sa, s95, s99, smax) = [int(x) for x in m.groups()]
                total = ba + pa + sa
                share = round(pa / total * 100, 1) if total > 0 else 0
                with _lock:
                    _state.update({
                        "bullet_avg": ba, "bullet_p95": b95,
                        "bullet_p99": b99, "bullet_max": bmax,
                        "physics_avg": pa, "physics_p95": p95,
                        "physics_p99": p99, "physics_max": pmax,
                        "snap_avg": sa,    "snap_p95": s95,
                        "snap_p99": s99,   "snap_max": smax,
                        "total_avg": total,
                        "physics_share": share,
                        "last_update": time.time(),
                    })
                print(f"[agent] Phase bullet={ba}µs physics={pa}µs snap={sa}µs "
                      f"total={total}µs physics_share={share}%")
                continue

            # [M32Match]
            m = MATCH_RE.search(line)
            if m:
                mid, bu, pu, su, tot = [int(x) for x in m.groups()]
                with _lock:
                    _state["per_match"][mid] = {
                        "bullet": bu, "physics": pu,
                        "snap": su, "total": tot
                    }
                continue


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

    # Concurrency proof
    g("m32_wall_avg_ms",          (s["wall_avg_us"] or 0) / 1000,         "Wall-clock avg to run ALL 32 matches (ms)")
    g("m32_sum_match_avg_ms",     (s["sum_match_avg_us"] or 0) / 1000,    "Sum of individual match times (ms) — sequential equivalent")
    g("m32_parallelism_factor",   s["parallelism_factor"],                 "Parallelism factor = sum/wall (approx N_WORKERS if truly parallel)")
    g("m32_sequential_would_be_ms", (s["sequential_would_be"] or 0) / 1000, "Time if 32 matches ran sequentially (ms)")

    # Status
    g("m32_active",         s["active"],        "0=waiting 1=warmup 2=running")
    g("m32_ticks",          s["ticks"],          "Total ticks measured")
    g("m32_overruns_total",  s["overruns"],      "Per-match ticks exceeding 16667µs budget")

    # 3-phase avg
    g("m32_bullet_avg_us",   s["bullet_avg"],    "Bullet phase avg µs per match per tick")
    g("m32_bullet_p95_us",   s["bullet_p95"],    "Bullet phase P95 µs")
    g("m32_bullet_p99_us",   s["bullet_p99"],    "Bullet phase P99 µs")
    g("m32_bullet_max_us",   s["bullet_max"],    "Bullet phase max µs")

    g("m32_physics_avg_us",  s["physics_avg"],   "Physics phase avg µs per match per tick")
    g("m32_physics_p95_us",  s["physics_p95"],   "Physics phase P95 µs")
    g("m32_physics_p99_us",  s["physics_p99"],   "Physics phase P99 µs")
    g("m32_physics_max_us",  s["physics_max"],   "Physics phase max µs")

    g("m32_snap_avg_us",     s["snap_avg"],      "Snapshot phase avg µs per match per tick")
    g("m32_snap_p95_us",     s["snap_p95"],      "Snapshot phase P95 µs")
    g("m32_snap_p99_us",     s["snap_p99"],      "Snapshot phase P99 µs")
    g("m32_snap_max_us",     s["snap_max"],      "Snapshot phase max µs")

    g("m32_total_avg_us",    s["total_avg"],     "Total avg µs per match per tick (bullet+physics+snap)")
    g("m32_physics_share_pct", s["physics_share"], "Physics share of total avg (pct)")
    g("m32_budget_us",       BUDGET_US,           "Tick budget 16667µs = 60Hz")

    # Per-match breakdown (last window)
    pm = s.get("per_match", {})
    for mid, vals in pm.items():
        lbl = f'match_id="{mid}"'
        g("m32_match_bullet_avg_us",  vals["bullet"],  f"Per-match bullet avg µs", lbl)
        g("m32_match_physics_avg_us", vals["physics"], f"Per-match physics avg µs", lbl)
        g("m32_match_snap_avg_us",    vals["snap"],    f"Per-match snap avg µs", lbl)
        g("m32_match_total_avg_us",   vals["total"],   f"Per-match total avg µs", lbl)

    return ("\n".join(lines) + "\n").encode("utf-8")


# ── HTTP handler ──────────────────────────────────────────────────────────────
class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        path = self.path.rstrip("/")
        if path in ("/metrics/prometheus", "/metrics"):
            with _lock:
                body = _prom(_state)
            self.send_response(200)
            self.send_header("Content-Type",
                             "text/plain; version=0.0.4; charset=utf-8")
            self.end_headers()
            self.wfile.write(body)
        elif path == "/status":
            import json
            with _lock:
                body = json.dumps(
                    {k: v for k, v in _state.items() if k != "per_match"},
                    indent=2).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(body)
        else:
            self.send_response(404)
            self.end_headers()

    def log_message(self, fmt, *args):
        pass


# ── Entry point ───────────────────────────────────────────────────────────────
if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Prometheus agent for bench_32match.exe")
    parser.add_argument("--log",  default=DEFAULT_LOG,
                        help="Path to bench_32match.log")
    parser.add_argument("--port", type=int, default=AGENT_PORT,
                        help="HTTP port for Prometheus scrape")
    args = parser.parse_args()

    print("=" * 60)
    print("32-Match Benchmark Agent — SE315.Q21")
    print(f"  Log  : {args.log}")
    print(f"  Port : http://localhost:{args.port}/metrics/prometheus")
    print(f"  Status: http://localhost:{args.port}/status")
    print("=" * 60)

    t = threading.Thread(target=tail_log, args=(args.log,),
                         daemon=True, name="log-tailer")
    t.start()

    server = HTTPServer(("0.0.0.0", args.port), Handler)
    print(f"[agent] Serving on port {args.port} ...")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[agent] Stopped.")
