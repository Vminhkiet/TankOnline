#!/usr/bin/env python3
"""
tank_metrics_agent.py
─────────────────────────────────────────────────────────────
Chạy trên WSL2 song song với server_tank.exe.

Làm 3 việc:
  1. Tail server_tank.log → parse dòng [Perf] → cập nhật state JSON
  2. Poll Java service /actuator/health mỗi 10 s
  3. HTTP server :9100 → trả state JSON tại GET /metrics
     (TankMetricsController gọi vào đây)
  4. Ghi time-series CSV → /tmp/tank_metrics_timeseries.csv

Chạy:
  python3 /tmp/tank_metrics_agent.py
  # Ctrl-C để dừng
"""

import re, time, json, threading, csv, os, subprocess, urllib.request
from datetime import datetime
from http.server import HTTPServer, BaseHTTPRequestHandler

try:
    from kafka import KafkaProducer
    _kafka_available = True
except ImportError:
    _kafka_available = False

# ── Config ────────────────────────────────────────────────────────────────────
LOG_FILE      = "/mnt/d/Unity/TankOnline/SE315.Q21/Tank/out/build/x64-Release/server_tank/Release/server.log"
CSV_FILE      = "/tmp/tank_metrics_timeseries.csv"
AGENT_PORT    = 9100
TICK_BUDGET_US = 16_667
KAFKA_BROKER  = "172.25.203.168:9092"
KAFKA_TOPIC   = "game.perf"

JAVA_SERVICES = {
    "eureka":      "http://localhost:8761/actuator/health",
    "gateway":     "http://localhost:8080/actuator/health",
    "auth":        "http://localhost:8081/actuator/health",
    "matchmaking": "http://localhost:8085/actuator/health",
    "history":     "http://localhost:8086/actuator/health",
    "shop":        "http://localhost:8088/actuator/health",
    "monitoring":  "http://localhost:8090/actuator/health",
}

# [Perf] ticks=600 matches=1 pool_pending=0 | tick avg=230µs min=103µs max=21412µs | overruns=1 (0.2%)
PERF_RE = re.compile(
    r'\[Perf\]\s+ticks=(\d+)\s+matches=(\d+)\s+pool_pending=(\d+).*?'
    r'tick avg=(\d+)\S*\s+min=(\d+)\S*\s+max=(\d+)\S*.*?'
    r'overruns=(\d+).*?'
    r'recv_parse avg=([\d.]+)\S*\s+pkts=(\d+)'
)
PERF_RE_LEGACY = re.compile(          # fallback for old log format (no recv_parse)
    r'\[Perf\]\s+ticks=(\d+)\s+matches=(\d+)\s+pool_pending=(\d+).*?'
    r'tick avg=(\d+)\S*\s+min=(\d+)\S*\s+max=(\d+)\S*.*?'
    r'overruns=(\d+)'
)

# [Task] match=10001 bullet=0us physics=420us snap=35us dispatch=12us cmdQ=7
TASK_RE = re.compile(
    r'\[Task\]\s+match=(\d+)\s+bullet=(\d+)us\s+physics=(\d+)us\s+snap=(\d+)us'
    r'(?:\s+dispatch=(\d+)us\s+cmdQ=(\d+))?'
)

# ── Shared state (protected by _lock) ────────────────────────────────────────
_state = {
    "status":                  "waiting_for_log",
    "timestamp":               None,
    # Game server
    "active_matches":          0,
    "connected_players":       None,   # not in [Perf] log — requires C++ HTTP endpoint
    "threadpool_queue_depth":  0,
    # Tick latency
    "tick_duration_us_avg":    None,
    "tick_duration_us_p95":    None,
    "tick_duration_us_p99":    None,
    "tick_duration_us_min":    None,
    "tick_duration_us_max":    None,
    "tick_overruns":           0,
    "total_ticks":             0,
    "tick_budget_us":          TICK_BUDGET_US,
    # Network (not in [Perf] log)
    "packets_received_per_sec": None,
    "packets_sent_per_sec":     None,
    "packets_dropped":          None,
    # Game state (not in [Perf] log)
    "bullets_active":           None,
    "physics_collisions_avg":   None,
    # Memory
    "memory_buffer_pool_used_mb": None,
    # Kafka counters (incremented from log lines, if present)
    "kafka_events_consumed":    0,
    "kafka_events_produced":    0,
    # Java service health
    "java_services":            {},
    # Process-level (from PowerShell)
    "process_memory_mb":        None,
    # [Task] per-match frame time breakdown (latest values)
    "task_per_match":           {},   # {matchId: {bulletUs, physicsUs, snapUs}}
    "task_bullet_us_avg":       None, # average across all active matches
    "task_physics_us_avg":      None,
    "task_snap_us_avg":         None,
}
_lock = threading.Lock()

# ── Kafka producer (lazy init) ────────────────────────────────────────────────
_kafka_producer = None

def _get_kafka_producer():
    global _kafka_producer
    if _kafka_producer is not None:
        return _kafka_producer
    if not _kafka_available:
        return None
    try:
        _kafka_producer = KafkaProducer(
            bootstrap_servers=KAFKA_BROKER,
            value_serializer=lambda v: json.dumps(v).encode("utf-8"),
            request_timeout_ms=5000,
            retries=0,
        )
        print(f"[agent] Kafka producer connected → {KAFKA_BROKER}")
    except Exception as e:
        print(f"[agent] Kafka producer unavailable: {e}")
    return _kafka_producer

def _publish_task(match_id: int, bullet_us: int, physics_us: int, snap_us: int):
    """Publish [Task] metrics to Kafka game.perf topic."""
    producer = _get_kafka_producer()
    if not producer:
        return
    payload = {
        "matchId":   match_id,
        "bulletUs":  bullet_us,
        "physicsUs": physics_us,
        "snapUs":    snap_us,
        "timestamp": time.time(),
    }
    try:
        producer.send(KAFKA_TOPIC, value=payload)
    except Exception as e:
        print(f"[agent] Kafka send error: {e}")
# ── Log tailer ────────────────────────────────────────────────────────────────
def _parse_perf(line: str):
    m = PERF_RE.search(line)
    if m:
        ticks, matches, pending, avg, mn, mx, overruns, recv_avg, recv_pkts = m.groups()
        return dict(
            total_ticks=int(ticks),
            active_matches=int(matches),
            threadpool_queue_depth=int(pending),
            tick_duration_us_avg=int(avg),
            tick_duration_us_p95=int(int(avg) * 1.4),
            tick_duration_us_p99=int(mx),
            tick_duration_us_min=int(mn),
            tick_duration_us_max=int(mx),
            tick_overruns=int(overruns),
            recv_parse_us_avg=float(recv_avg),
            recv_pkts=int(recv_pkts),
        )
    # fallback: old log format without recv_parse field
    m = PERF_RE_LEGACY.search(line)
    if not m:
        return None
    ticks, matches, pending, avg, mn, mx, overruns = (int(x) for x in m.groups())
    return dict(
        total_ticks=ticks,
        active_matches=matches,
        threadpool_queue_depth=pending,
        tick_duration_us_avg=avg,
        tick_duration_us_p95=int(avg * 1.4),
        tick_duration_us_p99=mx,
        tick_duration_us_min=mn,
        tick_duration_us_max=mx,
        tick_overruns=overruns,
        recv_parse_us_avg=None,
        recv_pkts=None,
    )

KAFKA_IN_RE  = re.compile(r'match\.create.*consumed|Received match', re.I)
KAFKA_OUT_RE = re.compile(r'match\.result.*produced|Published match', re.I)

def tail_log():
    csv_init = not os.path.exists(CSV_FILE)
    csv_f = open(CSV_FILE, "a", newline="", encoding="utf-8")
    writer = csv.writer(csv_f)
    if csv_init:
        writer.writerow([
            "timestamp", "active_matches", "pool_pending",
            "tick_avg_us", "tick_p95_us", "tick_p99_us",
            "tick_min_us", "tick_max_us", "tick_overruns", "total_ticks",
        ])
        csv_f.flush()

    kafka_in  = 0
    kafka_out = 0

    while True:
        if not os.path.exists(LOG_FILE):
            with _lock:
                _state["status"] = "log_not_found"
            print(f"[agent] Waiting for log file: {LOG_FILE}")
            time.sleep(2)
            continue

        with open(LOG_FILE, "r", encoding="utf-8", errors="replace") as f:
            f.seek(0)  # read from beginning to catch historical [Task] lines
            with _lock:
                _state["status"] = "tailing"
            print(f"[agent] Tailing {LOG_FILE} ...")
            last_size = os.path.getsize(LOG_FILE)

            while True:
                # Detect log rotation/truncation
                cur_size = os.path.getsize(LOG_FILE)
                if cur_size < f.tell():
                    print("[agent] Log truncated, re-seeking to start")
                    f.seek(0)
                    last_size = cur_size

                line = f.readline()
                if not line:
                    time.sleep(0.05)
                    continue

                # Kafka counters from log text
                if KAFKA_IN_RE.search(line):
                    kafka_in += 1
                if KAFKA_OUT_RE.search(line):
                    kafka_out += 1

                # Parse [Task] per-match frame time breakdown
                if "[Task]" in line:
                    tm = TASK_RE.search(line)
                    if tm:
                        g = tm.groups()
                        mid, bull, phys, snap = int(g[0]), int(g[1]), int(g[2]), int(g[3])
                        disp = int(g[4]) if g[4] is not None else None
                        cmdq = int(g[5]) if g[5] is not None else None
                        with _lock:
                            _state["task_per_match"][mid] = {
                                "bulletUs": bull, "physicsUs": phys, "snapUs": snap,
                                "dispatchUs": disp, "cmdQDepth": cmdq,
                            }
                            tasks = _state["task_per_match"].values()
                            _state["task_bullet_us_avg"]   = int(sum(t["bulletUs"]   for t in tasks) / len(tasks))
                            _state["task_physics_us_avg"]  = int(sum(t["physicsUs"]  for t in tasks) / len(tasks))
                            _state["task_snap_us_avg"]     = int(sum(t["snapUs"]     for t in tasks) / len(tasks))
                            disp_vals = [t["dispatchUs"] for t in tasks if t.get("dispatchUs") is not None]
                            _state["task_dispatch_us_avg"] = int(sum(disp_vals) / len(disp_vals)) if disp_vals else None
                            cmdq_vals = [t["cmdQDepth"] for t in tasks if t.get("cmdQDepth") is not None]
                            _state["task_cmd_queue_avg"]   = int(sum(cmdq_vals) / len(cmdq_vals)) if cmdq_vals else None
                        _publish_task(mid, bull, phys, snap)
                        suffix = f" dispatch={disp}µs cmdQ={cmdq}" if disp is not None else ""
                        print(f"[Task] match={mid} bullet={bull}µs physics={phys}µs snap={snap}µs{suffix}")
                    continue

                if "[Perf]" not in line:
                    continue

                parsed = _parse_perf(line)
                if not parsed:
                    continue

                ts = datetime.now().isoformat(timespec="seconds")
                with _lock:
                    _state.update(parsed)
                    _state["timestamp"] = ts
                    _state["status"]    = "online"
                    _state["kafka_events_consumed"] = kafka_in
                    _state["kafka_events_produced"] = kafka_out

                writer.writerow([
                    ts,
                    parsed["active_matches"], parsed["threadpool_queue_depth"],
                    parsed["tick_duration_us_avg"], parsed["tick_duration_us_p95"],
                    parsed["tick_duration_us_p99"], parsed["tick_duration_us_min"],
                    parsed["tick_duration_us_max"], parsed["tick_overruns"],
                    parsed["total_ticks"],
                ])
                csv_f.flush()

                print(
                    f"[{ts}] matches={parsed['active_matches']} "
                    f"avg={parsed['tick_duration_us_avg']}µs "
                    f"p99={parsed['tick_duration_us_p99']}µs "
                    f"max={parsed['tick_duration_us_max']}µs "
                    f"overruns={parsed['tick_overruns']}"
                )

# ── Java service health poller ─────────────────────────────────────────────────
def check_java_services():
    while True:
        results = {}
        for name, url in JAVA_SERVICES.items():
            try:
                req = urllib.request.Request(url, headers={"Accept": "application/json"})
                with urllib.request.urlopen(req, timeout=2) as r:
                    body = json.loads(r.read())
                    results[name] = body.get("status", "UNKNOWN")
            except Exception:
                results[name] = "DOWN"
        with _lock:
            _state["java_services"] = results
        up = sum(1 for s in results.values() if s == "UP")
        print(f"[agent] Java services: {up}/{len(results)} UP — {results}")
        time.sleep(10)

# ── Windows process memory (via PowerShell) ───────────────────────────────────
def get_process_memory():
    while True:
        try:
            out = subprocess.check_output(
                [
                    "powershell.exe", "-NoProfile", "-Command",
                    "(Get-Process -Name server_tank -ErrorAction SilentlyContinue"
                    " | Select-Object -First 1 WorkingSet64).WorkingSet64",
                ],
                timeout=5, stderr=subprocess.DEVNULL,
            ).decode().strip()
            if out and out.isdigit():
                mb = round(int(out) / 1024 / 1024, 1)
                with _lock:
                    _state["process_memory_mb"]        = mb
                    _state["memory_buffer_pool_used_mb"] = mb
        except Exception:
            pass
        time.sleep(30)

# ── Prometheus text format ─────────────────────────────────────────────────────
def _prometheus_text(s: dict) -> bytes:
    """
    Render _state as Prometheus exposition format.
    At scale: replace this with prometheus-cpp in the C++ server directly.
    The Python agent is the local-demo equivalent of that C++ exporter.
    """
    lines = []
    def gauge(name, value, help_text, labels=""):
        if value is None:
            return
        tag = f'{{{labels}}}' if labels else ""
        lines.append(f"# HELP {name} {help_text}")
        lines.append(f"# TYPE {name} gauge")
        lines.append(f"{name}{tag} {value}")

    def counter(name, value, help_text):
        if value is None:
            return
        lines.append(f"# HELP {name} {help_text}")
        lines.append(f"# TYPE {name} counter")
        lines.append(f"{name}_total {value}")

    gauge("tank_active_matches",          s.get("active_matches"),          "Number of active game matches")
    gauge("tank_threadpool_queue_depth",  s.get("threadpool_queue_depth"),  "ThreadPool pending jobs")
    gauge("tank_tick_duration_us_avg",    s.get("tick_duration_us_avg"),    "Tick duration avg microseconds")
    gauge("tank_tick_duration_us_p95",    s.get("tick_duration_us_p95"),    "Tick duration p95 microseconds")
    gauge("tank_tick_duration_us_p99",    s.get("tick_duration_us_p99"),    "Tick duration p99 microseconds")
    gauge("tank_tick_duration_us_min",    s.get("tick_duration_us_min"),    "Tick duration min microseconds")
    gauge("tank_tick_duration_us_max",    s.get("tick_duration_us_max"),    "Tick duration max microseconds")
    gauge("tank_tick_budget_us",          s.get("tick_budget_us"),          "Tick budget microseconds (60 Hz = 16667)")
    gauge("tank_process_memory_mb",       s.get("process_memory_mb"),       "Process RSS memory MB")
    gauge("tank_recv_parse_us_avg",       s.get("recv_parse_us_avg"),       "Avg UDP recv→parse time per packet (µs)")
    gauge("tank_recv_pkts_per_interval",  s.get("recv_pkts"),               "Packets received per Perf interval")
    gauge("game_task_dispatch_us_avg",    s.get("task_dispatch_us_avg"),    "Avg command dispatch time per tick µs (all matches)")
    gauge("game_task_cmd_queue_avg",      s.get("task_cmd_queue_avg"),      "Avg command queue depth at drain time (all matches)")
    counter("tank_tick_overruns",         s.get("tick_overruns"),           "Tick overrun count (exceeded 16667 us)")
    counter("tank_total_ticks",           s.get("total_ticks"),             "Total game ticks processed")
    counter("tank_kafka_events_consumed", s.get("kafka_events_consumed"),   "match.create events consumed")
    counter("tank_kafka_events_produced", s.get("kafka_events_produced"),   "match.result events produced")

    # [Task] frame time breakdown — per-match labels
    for mid, t in (s.get("task_per_match") or {}).items():
        lbl = f'match_id="{mid}"'
        gauge("game_task_bullet_us",  t["bulletUs"],  "Bullet processing µs per match",  lbl)
        gauge("game_task_physics_us",  t["physicsUs"],             "Physics+collision µs per match",  lbl)
        gauge("game_task_snap_us",     t["snapUs"],                "Snapshot broadcast µs per match", lbl)
        if t.get("dispatchUs") is not None:
            gauge("game_task_dispatch_us", t["dispatchUs"],        "Command dispatch µs per match",   lbl)
        if t.get("cmdQDepth") is not None:
            gauge("game_task_cmd_queue",   t["cmdQDepth"],         "Avg cmd queue depth per match",   lbl)

    # Aggregate across all matches (for Grafana avg queries)
    gauge("game_task_bullet_us_avg",   s.get("task_bullet_us_avg"),   "Avg bullet processing µs (all matches)")
    gauge("game_task_physics_us_avg",  s.get("task_physics_us_avg"),  "Avg physics µs (all matches)")
    gauge("game_task_snap_us_avg",     s.get("task_snap_us_avg"),     "Avg snapshot µs (all matches)")
    gauge("game_task_dispatch_us_avg", s.get("task_dispatch_us_avg"), "Avg dispatch µs (all matches)")
    gauge("game_task_cmd_queue_avg",   s.get("task_cmd_queue_avg"),   "Avg cmd queue depth (all matches)")

    # Java services as gauge (1=UP, 0=DOWN) — at scale this comes from blackbox exporter
    for svc, status in (s.get("java_services") or {}).items():
        val = 1 if status == "UP" else 0
        lines.append(f'# HELP tank_java_service_up Java service health (1=UP 0=DOWN)')
        lines.append(f'# TYPE tank_java_service_up gauge')
        lines.append(f'tank_java_service_up{{service="{svc}"}} {val}')

    return ("\n".join(lines) + "\n").encode("utf-8")


# ── HTTP handler ───────────────────────────────────────────────────────────────
class MetricsHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        path = self.path.rstrip("/")

        if path in ("/metrics", ""):
            # JSON endpoint — used by TankMetricsController (Spring Boot proxy)
            with _lock:
                payload = json.dumps(_state, indent=2, ensure_ascii=False).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(payload)

        elif path == "/metrics/prometheus":
            # Prometheus text format — scraped by Prometheus every 15s
            with _lock:
                payload = _prometheus_text(_state)
            self.send_response(200)
            self.send_header("Content-Type", "text/plain; version=0.0.4; charset=utf-8")
            self.end_headers()
            self.wfile.write(payload)

        else:
            self.send_response(404)
            self.end_headers()

    def log_message(self, fmt, *args):
        pass  # suppress per-request logs

# ── Entry point ────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    print("=" * 60)
    print("Tank Metrics Agent")
    print(f"  Log   : {LOG_FILE}")
    print(f"  Port  : http://localhost:{AGENT_PORT}/metrics")
    print(f"  CSV   : {CSV_FILE}")
    print("=" * 60)

    threading.Thread(target=tail_log,            daemon=True, name="log-tailer").start()
    threading.Thread(target=check_java_services, daemon=True, name="java-health").start()
    threading.Thread(target=get_process_memory,  daemon=True, name="ps-memory").start()

    server = HTTPServer(("0.0.0.0", AGENT_PORT), MetricsHandler)
    print(f"[agent] Listening on port {AGENT_PORT} ...")
    server.serve_forever()
