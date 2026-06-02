#!/usr/bin/env python3
"""
bench_wc_live_agent.py
──────────────────────────────────────────────────────────────────────────────
Live Worst-Case Benchmark Monitor & Controller  —  port 9103

Chức năng:
  • Khởi động bench_wc_live.exe với số player hiện tại
  • Khi user đổi player count → kill + restart exe với player count mới
  • Tail log → parse [WC_Final] / [WC_Phase] → expose Prometheus metrics
  • HTTP GET  /                    → HTML control panel (ô nhập players)
  • HTTP GET  /metrics/prometheus  → Prometheus format
  • HTTP GET  /status              → JSON trạng thái hiện tại
  • HTTP POST /config              → {"players": N}  đổi player count

Chạy:
  python3 Tank/bench_wc_live_agent.py
  python3 Tank/bench_wc_live_agent.py --players=10 --port=9103

Grafana thêm job:
  - job_name: bench-wc-live
    metrics_path: /metrics/prometheus
    static_configs:
      - targets: ["host.docker.internal:9103"]
"""

import os, re, json, time, threading, subprocess, argparse, signal
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs

# ── Config ────────────────────────────────────────────────────────────────────
AGENT_PORT   = 9103
BUDGET_US    = 16_667.0
CONFIG_FILE  = "bench_wc_config.json"

# Tìm exe theo thứ tự ưu tiên
EXE_CANDIDATES = [
    # WSL2 mount paths (Linux-accessible Windows drives)
    "/mnt/d/Unity/TankOnline/game/SE315.Q21/Tank/out/build/x64-Release/bench_worst_case/bench_wc_live.exe",
    "/mnt/d/Unity/TankOnline/game/SE315.Q21/Tank/out/build/x64-Release/bench_worst_case/Release/bench_wc_live.exe",
    # Windows-style relative paths (fallback nếu chạy từ Windows)
    r"Tank\out\build\x64-Release\bench_worst_case\bench_wc_live.exe",
    r"Tank\out\build\x64-Release\bench_worst_case\Release\bench_worst_case.exe",
    r"Tank\bench_worst_case\Release\bench_wc_live.exe",
    r"bench_wc_live.exe",
    # WSL path nếu chạy từ WSL2
    "/mnt/d/Unity/TankOnline/Tank/out/build/x64-Release/bench_worst_case/bench_wc_live.exe",
]

LOG_FILE = r"Tank\out\build\x64-Release\bench_worst_case\bench_wc_live.log"

# ── Shared state ──────────────────────────────────────────────────────────────
_config_lock = threading.Lock()
_config = {"players": 10}

_metrics_lock = threading.Lock()
_metrics = {
    "players":       10,
    "window":        0,
    # Total tick
    "total_avg_us":  None,
    "total_p99_us":  None,
    "total_max_us":  None,
    # Phases
    "bullet_avg_us":  None,  "bullet_p99_us":  None,  "bullet_max_us":  None,
    "physics_avg_us": None,  "physics_p99_us": None,  "physics_max_us": None,
    "snap_avg_us":    None,  "snap_p99_us":    None,  "snap_max_us":    None,
    # Derived
    "gpc":           None,
    "budget_pct":    None,
    "overruns":      0,
    "steady_bullets": 0,
    # Overhead (task-switch)
    "matches":                    1,
    "overhead_avg_us":            None,
    "overhead_p99_us":            None,
    "overhead_per_switch_avg_us": None,
    "overhead_per_switch_p99_us": None,
    # Status
    "active":        0,
    "last_update":   0.0,
    # Streaming phase avg (mới nhất từ [WC_Phase])
    "phase_bullet_avg":  None,
    "phase_physics_avg": None,
    "phase_snap_avg":    None,
    "phase_total_avg":   None,
}

_process     = None
_proc_lock   = threading.Lock()
_running     = True

# ── Config helpers ────────────────────────────────────────────────────────────
def _load_config():
    global _config
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE) as f:
                _config.update(json.load(f))
        except Exception:
            pass

def _save_config():
    with open(CONFIG_FILE, "w") as f:
        json.dump(_config, f, indent=2)

# ── Find exe ──────────────────────────────────────────────────────────────────
def _find_exe():
    # Check relative to this script's directory first
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root       = os.path.dirname(script_dir)          # SE315.Q21/
    candidates = EXE_CANDIDATES + [
        os.path.join(root, "Tank", "out", "build", "x64-Release",
                     "bench_worst_case", "bench_wc_live.exe"),
    ]
    for c in candidates:
        path = c if os.path.isabs(c) else os.path.join(root, c)
        if os.path.exists(path):
            return os.path.abspath(path)
    return None

# ── Log file path ─────────────────────────────────────────────────────────────
def _get_log_path():
    exe = _find_exe()
    if exe:
        return os.path.join(os.path.dirname(exe), "bench_wc_live.log")
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root       = os.path.dirname(script_dir)
    return os.path.join(root, LOG_FILE)

# ── Process lifecycle ─────────────────────────────────────────────────────────
def _kill_current():
    global _process
    with _proc_lock:
        if _process and _process.poll() is None:
            try:
                _process.terminate()
                _process.wait(timeout=5)
            except Exception:
                try: _process.kill()
                except Exception: pass
        _process = None

def _start_benchmark(players: int):
    global _process
    _kill_current()

    exe = _find_exe()
    if not exe:
        print("[agent] bench_wc_live.exe not found — monitoring log only")
        print("[agent] Searched:", EXE_CANDIDATES)
        with _metrics_lock:
            _metrics["active"] = 0
        return

    log_path = _get_log_path()
    # Xóa log cũ để tránh đọc kết quả cũ
    try:
        os.remove(log_path)
    except Exception:
        pass

    # Chạy từ thư mục chứa exe để tìm đúng assests/bench_spread.json
    exe_dir = os.path.dirname(exe)
    # Nếu exe được copy ra ngoài Release/, thử dùng Release/ subdir làm cwd
    release_dir = os.path.join(exe_dir, "Release")
    cwd = release_dir if os.path.isdir(release_dir) else exe_dir

    # Log file đặt cạnh exe (cwd)
    log_path = os.path.join(cwd, "bench_wc_live.log")

    matches_k = _config.get("matches", 1)
    cmd = [exe, f"--players={players}", f"--matches={matches_k}", f"--log={log_path}"]
    print(f"[agent] Starting from cwd: {cwd}")
    print(f"[agent] CMD: {' '.join(cmd)}")
    print(f"[agent] Log: {log_path}")

    with _proc_lock:
        _process = subprocess.Popen(
            cmd,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1
        )

    # Pin process vào core cụ thể nếu --core N được chỉ định
    affinity_core = _config.get("core", -1)
    if affinity_core >= 0 and _process is not None:
        affinity_mask = 1 << affinity_core
        try:
            subprocess.run(
                ["powershell.exe", "-NoProfile", "-Command",
                 f"(Get-Process -Id {_process.pid}).ProcessorAffinity = {affinity_mask}"],
                capture_output=True, timeout=5
            )
            print(f"[agent] Pinned PID {_process.pid} to core {affinity_core} (mask=0x{affinity_mask:X})")
        except Exception as e:
            print(f"[agent] Warning: could not set affinity: {e}")

    with _metrics_lock:
        _metrics["active"] = 1
        _metrics["players"] = players
        # Reset metrics khi restart
        for k in ("total_avg_us","total_p99_us","total_max_us",
                  "bullet_avg_us","bullet_p99_us","bullet_max_us",
                  "physics_avg_us","physics_p99_us","physics_max_us",
                  "snap_avg_us","snap_p99_us","snap_max_us",
                  "gpc","budget_pct","window"):
            _metrics[k] = None
        _metrics["overruns"] = 0

    print(f"[agent] PID={_process.pid}  players={players}")

    # Thread đọc stdout của process
    def _read_stdout():
        try:
            for line in _process.stdout:
                line = line.strip()
                if line:
                    print(f"[bench] {line}")
                    _parse_line(line)
        except Exception:
            pass

    t = threading.Thread(target=_read_stdout, daemon=True)
    t.start()

# ── Log parser ────────────────────────────────────────────────────────────────
# [WC_Final] players=10 window=1 bullet_avg=188us bullet_p99=295us ...
WC_FINAL_RE = re.compile(
    r'\[WC_Final\]\s+'
    r'players=(\d+)\s+(?:matches=(\d+)\s+)?window=(\d+)\s+'
    r'bullet_avg=(\d+)us\s+bullet_p99=(\d+)us\s+bullet_max=(\d+)us\s+'
    r'physics_avg=(\d+)us\s+physics_p99=(\d+)us\s+physics_max=(\d+)us\s+'
    r'snap_avg=(\d+)us\s+snap_p99=(\d+)us\s+snap_max=(\d+)us\s+'
    r'total_avg=(\d+)us\s+total_p99=(\d+)us\s+total_max=(\d+)us\s+'
    r'gpc=(\d+)\s+budget_pct=([\d.]+)\s+overruns=(\d+)/(\d+)\s+'
    r'steady_bullets=(\d+)'
    r'(?:\s+overhead_avg=(\d+)us\s+overhead_p99=(\d+)us\s+'
    r'overhead_per_switch_avg=(\d+)us\s+overhead_per_switch_p99=(\d+)us)?'
)

# [WC_Phase] players=10 window=1 bullet_avg=Xus physics_avg=Xus snap_avg=Xus total_avg=Xus overruns=K/T
WC_PHASE_RE = re.compile(
    r'\[WC_Phase\]\s+'
    r'players=(\d+)\s+window=(\d+)\s+'
    r'bullet_avg=(\d+)us\s+physics_avg=(\d+)us\s+snap_avg=(\d+)us\s+'
    r'total_avg=(\d+)us\s+overruns=(\d+)/(\d+)'
)

def _parse_line(line: str):
    m = WC_FINAL_RE.search(line)
    if m:
        players   = int(m.group(1))
        matches_k = int(m.group(2)) if m.group(2) else 1
        window    = int(m.group(3))
        bul_avg   = int(m.group(4));  bul_p99  = int(m.group(5));  bul_max  = int(m.group(6))
        phy_avg   = int(m.group(7));  phy_p99  = int(m.group(8));  phy_max  = int(m.group(9))
        snp_avg   = int(m.group(10)); snp_p99  = int(m.group(11)); snp_max  = int(m.group(12))
        tot_avg   = int(m.group(13)); tot_p99  = int(m.group(14)); tot_max  = int(m.group(15))
        gpc       = int(m.group(16))
        budget    = float(m.group(17))
        overruns  = int(m.group(18))
        bullets   = int(m.group(20))
        # overhead fields (optional, present when matches>1)
        ovhd_avg        = int(m.group(21)) if m.group(21) else None
        ovhd_p99        = int(m.group(22)) if m.group(22) else None
        ovhd_sw_avg     = int(m.group(23)) if m.group(23) else None
        ovhd_sw_p99     = int(m.group(24)) if m.group(24) else None

        with _metrics_lock:
            _metrics.update({
                "players":        players,
                "matches":        matches_k,
                "window":         window,
                "bullet_avg_us":  bul_avg,   "bullet_p99_us":  bul_p99,   "bullet_max_us":  bul_max,
                "physics_avg_us": phy_avg,   "physics_p99_us": phy_p99,   "physics_max_us": phy_max,
                "snap_avg_us":    snp_avg,   "snap_p99_us":    snp_p99,   "snap_max_us":    snp_max,
                "total_avg_us":   tot_avg,   "total_p99_us":   tot_p99,   "total_max_us":   tot_max,
                "gpc":            gpc,
                "budget_pct":     budget,
                "overruns":       overruns,
                "steady_bullets": bullets,
                "overhead_avg_us":            ovhd_avg,
                "overhead_p99_us":            ovhd_p99,
                "overhead_per_switch_avg_us": ovhd_sw_avg,
                "overhead_per_switch_p99_us": ovhd_sw_p99,
                "active":         1,
                "last_update":    time.time(),
            })
        ovhd_str = f" overhead/switch={ovhd_sw_avg}µs" if ovhd_sw_avg else ""
        print(f"[agent] ✓ window={window} players={players} matches={matches_k} total_p99={tot_p99}µs GPC={gpc}{ovhd_str}")
        return

    m = WC_PHASE_RE.search(line)
    if m:
        with _metrics_lock:
            _metrics.update({
                "phase_bullet_avg":  int(m.group(3)),
                "phase_physics_avg": int(m.group(4)),
                "phase_snap_avg":    int(m.group(5)),
                "phase_total_avg":   int(m.group(6)),
                "last_update":       time.time(),
            })

# ── Also tail log file (fallback khi đọc stdout không đủ) ────────────────────
def _tail_log_thread():
    """Tail log file độc lập với stdout reader — dùng khi exe output sang file."""
    log_path = _get_log_path()
    while _running:
        if not os.path.exists(log_path):
            time.sleep(1)
            continue
        try:
            with open(log_path, "r", encoding="utf-8", errors="replace") as f:
                f.seek(0, 2)  # seek to end
                while _running:
                    line = f.readline()
                    if not line:
                        time.sleep(0.1)
                        continue
                    line = line.strip()
                    if line and ("[WC_Final]" in line or "[WC_Phase]" in line):
                        _parse_line(line)
        except Exception:
            time.sleep(1)

# ── Prometheus format ─────────────────────────────────────────────────────────
def _prometheus_output() -> str:
    with _metrics_lock:
        m = dict(_metrics)

    players = m["players"]
    lbl = f'players="{players}"'
    lines = []

    def g(name, val, help_text, unit="us"):
        if val is None:
            return
        lines.append(f"# HELP {name} {help_text}")
        lines.append(f"# TYPE {name} gauge")
        lines.append(f"{name}{{{lbl}}} {val}")

    # Total tick (most important for GPC)
    g("wc_total_avg_us",   m["total_avg_us"],  "Worst-case total tick avg µs")
    g("wc_total_p99_us",   m["total_p99_us"],  "Worst-case total tick P99 µs ← dùng tính GPC")
    g("wc_total_max_us",   m["total_max_us"],  "Worst-case total tick max µs")

    # Phases
    g("wc_bullet_avg_us",  m["bullet_avg_us"],  "Bullet phase avg µs")
    g("wc_bullet_p99_us",  m["bullet_p99_us"],  "Bullet phase P99 µs")
    g("wc_bullet_max_us",  m["bullet_max_us"],  "Bullet phase max µs")
    g("wc_physics_avg_us", m["physics_avg_us"], "Physics phase avg µs (BOTTLENECK)")
    g("wc_physics_p99_us", m["physics_p99_us"], "Physics phase P99 µs")
    g("wc_physics_max_us", m["physics_max_us"], "Physics phase max µs")
    g("wc_snap_avg_us",    m["snap_avg_us"],    "Snapshot phase avg µs")
    g("wc_snap_p99_us",    m["snap_p99_us"],    "Snapshot phase P99 µs")
    g("wc_snap_max_us",    m["snap_max_us"],    "Snapshot phase max µs")

    # Derived — mỗi khi có kết quả mới
    g("wc_gpc",          m["gpc"],          "GPC = floor(16667 / total_P99) match/tick/core")
    g("wc_budget_pct",   m["budget_pct"],   "Tick budget used at P99 (%)")
    g("wc_overruns",     m["overruns"],     "Ticks exceeding 16667µs budget in last window")
    g("wc_steady_bullets", m["steady_bullets"], "Steady-state bullet count in match")
    g("wc_window",       m["window"],       "Current measurement window index")

    # Streaming phase avg (cập nhật mỗi 600 ticks = ~10s)
    g("wc_phase_bullet_avg",  m.get("phase_bullet_avg"),  "Bullet phase streaming avg µs")
    g("wc_phase_physics_avg", m.get("phase_physics_avg"), "Physics phase streaming avg µs")
    g("wc_phase_snap_avg",    m.get("phase_snap_avg"),    "Snap phase streaming avg µs")
    g("wc_phase_total_avg",   m.get("phase_total_avg"),   "Total phase streaming avg µs")

    # Overhead (task-switch cost)
    g("wc_overhead_avg_us",              m.get("overhead_avg_us"),              "Total overhead per tick avg µs (wall - sum_individual)")
    g("wc_overhead_p99_us",              m.get("overhead_p99_us"),              "Total overhead per tick P99 µs")
    g("wc_overhead_per_switch_avg_us",   m.get("overhead_per_switch_avg_us"),   "Overhead per task-switch avg µs = overhead/(K-1)")
    g("wc_overhead_per_switch_p99_us",   m.get("overhead_per_switch_p99_us"),   "Overhead per task-switch P99 µs")
    g("wc_matches",                      m.get("matches", 1),                   "Number of matches run sequentially per tick")

    # Meta
    g("wc_players", players, "Current player count being benchmarked")
    g("wc_active",  m["active"], "1 = benchmark running, 0 = stopped")
    g("wc_budget_us", int(BUDGET_US), "Tick budget = 16667µs at 60Hz")

    return "\n".join(lines) + "\n"

# ── HTML control page ─────────────────────────────────────────────────────────
def _control_html() -> str:
    with _metrics_lock:
        m = dict(_metrics)

    def fmt(v, suffix="µs"):
        return f"{v:,}{suffix}" if v is not None else "—"

    def gpc_color(v):
        if v is None: return "#888"
        if v >= 3: return "#66bb6a"
        if v >= 2: return "#ffa726"
        return "#ef5350"

    def budget_color(v):
        if v is None: return "#888"
        if v < 50: return "#66bb6a"
        if v < 75: return "#ffa726"
        return "#ef5350"

    gpc   = m.get("gpc")
    bpct  = m.get("budget_pct")
    n_pl  = m["players"]
    win   = m.get("window") or 0
    stale = (time.time() - m["last_update"]) > 30 if m["last_update"] else True

    status_color = "#66bb6a" if m["active"] and not stale else "#ef5350"
    status_text  = "🟢 Running" if m["active"] and not stale else "🔴 Waiting / Stale"

    return f"""<!DOCTYPE html>
<html lang="vi">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Worst-Case Benchmark Monitor</title>
<style>
* {{ box-sizing: border-box; margin: 0; padding: 0; }}
body {{ font-family: 'Segoe UI', monospace; background: #0d1117; color: #c9d1d9; padding: 24px; }}
h1 {{ color: #58a6ff; font-size: 1.4rem; margin-bottom: 4px; }}
.sub {{ color: #8b949e; font-size: 0.85rem; margin-bottom: 24px; }}
.row {{ display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 16px; }}
.card {{ background: #161b22; border: 1px solid #30363d; border-radius: 8px; padding: 16px; flex: 1; min-width: 200px; }}
.card h3 {{ color: #8b949e; font-size: 0.75rem; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 12px; }}
.big {{ font-size: 2rem; font-weight: 700; }}
.label {{ font-size: 0.8rem; color: #8b949e; margin-top: 4px; }}
table {{ width: 100%; border-collapse: collapse; font-size: 0.85rem; }}
td, th {{ padding: 6px 10px; text-align: right; }}
th {{ text-align: left; color: #8b949e; font-weight: normal; }}
tr:hover td {{ background: #21262d; }}
.sep {{ border-top: 1px solid #30363d; }}
.ctrl {{ background: #161b22; border: 1px solid #30363d; border-radius: 8px; padding: 20px; margin-bottom: 16px; }}
.ctrl h3 {{ color: #58a6ff; margin-bottom: 12px; }}
input[type=number] {{
    padding: 8px 12px; font-size: 1rem; width: 120px;
    background: #0d1117; color: #c9d1d9;
    border: 1px solid #30363d; border-radius: 6px;
}}
input[type=number]:focus {{ outline: none; border-color: #58a6ff; }}
button {{
    padding: 8px 20px; font-size: 0.95rem; cursor: pointer;
    background: #238636; color: #fff; border: none; border-radius: 6px;
    margin-left: 8px;
}}
button:hover {{ background: #2ea043; }}
.status {{ display: inline-block; padding: 4px 10px; border-radius: 4px;
           font-size: 0.8rem; background: #21262d; color: {status_color}; }}
.hint {{ color: #8b949e; font-size: 0.8rem; margin-top: 8px; }}
a {{ color: #58a6ff; text-decoration: none; }}
a:hover {{ text-decoration: underline; }}
.phase-bar {{ height: 8px; border-radius: 4px; background: #21262d; margin: 4px 0; overflow: hidden; }}
.phase-fill {{ height: 100%; border-radius: 4px; }}
</style>
</head>
<body>

<h1>⚡ Worst-Case Match Monitor</h1>
<p class="sub">Giám sát hiệu năng match nặng nhất theo thời gian thực &nbsp;|&nbsp;
  <a href="/metrics/prometheus">Prometheus</a> &nbsp;|&nbsp;
  <a href="/status">JSON</a></p>

<div class="row">
  <div class="card">
    <h3>Trạng thái</h3>
    <div class="big" style="color:{status_color}; font-size:1.2rem">{status_text}</div>
    <div class="label">Players: <b style="color:#e6edf3">{n_pl}</b>  |  Window: <b style="color:#e6edf3">{win}</b></div>
    <div class="label">Steady bullets: <b style="color:#e6edf3">{fmt(m.get("steady_bullets"),"")}</b></div>
  </div>

  <div class="card">
    <h3>GPC (Games Per Core)</h3>
    <div class="big" style="color:{gpc_color(gpc)}">{gpc if gpc is not None else "—"}</div>
    <div class="label">match / tick / core</div>
    <div class="label" style="font-size:0.75rem">= floor(16667 / P99) với P99 = {fmt(m.get("total_p99_us"))}</div>
  </div>

  <div class="card">
    <h3>Budget Used (P99)</h3>
    <div class="big" style="color:{budget_color(bpct)}">{f"{bpct:.1f}%" if bpct is not None else "—"}</div>
    <div class="label">của budget 16,667µs (60Hz)</div>
    <div class="label">Overruns: <b style="color:#ef5350">{m.get("overruns",0)}</b> / {win * 600 if win else "—"} ticks</div>
  </div>
</div>

<div class="row">
  <div class="card">
    <h3>Tick Time</h3>
    <table>
      <tr><th></th><th>Avg</th><th>P99</th><th>Max</th></tr>
      <tr><td style="text-align:left; color:#f0883e"><b>Total</b></td>
          <td>{fmt(m.get("total_avg_us"))}</td>
          <td><b>{fmt(m.get("total_p99_us"))}</b></td>
          <td>{fmt(m.get("total_max_us"))}</td></tr>
      <tr class="sep">
          <td style="text-align:left; color:#79c0ff">Bullet</td>
          <td>{fmt(m.get("bullet_avg_us"))}</td>
          <td>{fmt(m.get("bullet_p99_us"))}</td>
          <td>{fmt(m.get("bullet_max_us"))}</td></tr>
      <tr><td style="text-align:left; color:#ff7b72">Physics ⚠</td>
          <td>{fmt(m.get("physics_avg_us"))}</td>
          <td>{fmt(m.get("physics_p99_us"))}</td>
          <td>{fmt(m.get("physics_max_us"))}</td></tr>
      <tr><td style="text-align:left; color:#56d364">Snap</td>
          <td>{fmt(m.get("snap_avg_us"))}</td>
          <td>{fmt(m.get("snap_p99_us"))}</td>
          <td>{fmt(m.get("snap_max_us"))}</td></tr>
    </table>
  </div>

  <div class="card" style="min-width:220px">
    <h3>Tỷ lệ pha (P99)</h3>
    {"".join(f'''
    <div style="font-size:0.8rem; margin-bottom:6px">
      <span style="color:{c}">{ph}</span>
      <span style="float:right; color:#e6edf3">{fmt(val)}</span>
      <div class="phase-bar"><div class="phase-fill" style="width:{pct}%; background:{c}"></div></div>
    </div>
    ''' for ph, val, c, pct in [
        ("Bullet",  m.get("bullet_p99_us"),
         "#79c0ff", round(100 * (m.get("bullet_p99_us") or 0) / max(m.get("total_p99_us") or 1, 1))),
        ("Physics", m.get("physics_p99_us"),
         "#ff7b72", round(100 * (m.get("physics_p99_us") or 0) / max(m.get("total_p99_us") or 1, 1))),
        ("Snap",    m.get("snap_p99_us"),
         "#56d364", round(100 * (m.get("snap_p99_us") or 0) / max(m.get("total_p99_us") or 1, 1))),
    ])}
  </div>
</div>

<div class="ctrl">
  <h3>🎮 Đổi số player trong match</h3>
  <form id="cfg" onsubmit="return applyConfig()">
    <label for="p">Số player (1–100):</label>
    <input type="number" id="p" min="1" max="100" value="{n_pl}">
    <button type="submit">▶ Áp dụng &amp; Chạy lại</button>
  </form>
  <p class="hint">⏳ Sau khi nhấn, benchmark sẽ khởi động lại (~15s warmup) rồi hiển thị kết quả mới.
    Trang tự reload sau 15s.</p>
</div>

<p class="hint">
  📊 <a href="http://localhost:3000" target="_blank">Mở Grafana</a> &nbsp;|&nbsp;
  Dashboard: <b>Worst-Case Live Benchmark</b> &nbsp;|&nbsp;
  Metrics cập nhật mỗi ~10s (600 ticks)
</p>

<script>
async function applyConfig() {{
  const p = parseInt(document.getElementById('p').value);
  if (p < 1 || p > 100) {{ alert('Player count phải từ 1 đến 100'); return false; }}
  document.querySelector('button').textContent = '⏳ Đang khởi động...';
  document.querySelector('button').disabled = true;
  try {{
    const r = await fetch('/config', {{
      method: 'POST',
      headers: {{'Content-Type': 'application/json'}},
      body: JSON.stringify({{players: p}})
    }});
    const j = await r.json();
    if (j.ok) {{
      setTimeout(() => location.reload(), 15000);
    }} else {{
      alert('Lỗi: ' + (j.error || 'unknown'));
      location.reload();
    }}
  }} catch(e) {{
    alert('Lỗi kết nối: ' + e);
    location.reload();
  }}
  return false;
}}
// Auto-refresh mỗi 15s để cập nhật metrics
setTimeout(() => location.reload(), 15000);
</script>

</body>
</html>"""

# ── HTTP Handler ──────────────────────────────────────────────────────────────
class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        path = urlparse(self.path).path.rstrip("/")

        if path in ("", "/", "/control"):
            body = _control_html().encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        elif path in ("/metrics/prometheus", "/metrics"):
            body = _prometheus_output().encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/plain; version=0.0.4; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        elif path == "/status":
            with _metrics_lock:
                data = dict(_metrics)
            data["exe_found"] = _find_exe() is not None
            data["log_path"]  = _get_log_path()
            body = json.dumps(data, indent=2).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(body)

        else:
            self.send_response(404)
            self.end_headers()

    def do_POST(self):
        path = urlparse(self.path).path

        if path == "/config":
            try:
                length = int(self.headers.get("Content-Length", 0))
                raw    = self.rfile.read(length)
                data    = json.loads(raw)
                players = int(data.get("players", _config["players"]))
                matches = int(data.get("matches", _config.get("matches", 1)))
                players = max(1, min(100, players))
                matches = max(1, min(50,  matches))

                with _config_lock:
                    _config["players"] = players
                    _config["matches"] = matches
                    _save_config()

                # Restart benchmark in background
                t = threading.Thread(target=_start_benchmark, args=(players,), daemon=True)
                t.start()

                resp = json.dumps({"ok": True, "players": players}).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(resp)))
                self.end_headers()
                self.wfile.write(resp)

            except Exception as e:
                resp = json.dumps({"ok": False, "error": str(e)}).encode()
                self.send_response(400)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(resp)
        else:
            self.send_response(404)
            self.end_headers()

    def log_message(self, fmt, *args):
        pass  # suppress request logs

# ── Watchdog: restart nếu process chết ───────────────────────────────────────
def _watchdog_thread():
    while _running:
        time.sleep(10)
        with _proc_lock:
            dead = (_process is not None and _process.poll() is not None)
        if dead:
            print("[agent] Process died — restarting...")
            with _config_lock:
                p = _config["players"]
            _start_benchmark(p)

# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    global CONFIG_FILE   # khai bao truoc khi dung
    parser = argparse.ArgumentParser(description="Worst-Case Benchmark Live Agent")
    parser.add_argument("--players", type=int, default=None, help="Initial player count")
    parser.add_argument("--matches", type=int, default=None, help="Number of matches per tick (for overhead measurement)")
    parser.add_argument("--port",    type=int, default=AGENT_PORT)
    parser.add_argument("--no-start", action="store_true", help="Dung tu dong start exe")
    parser.add_argument("--config",  default=CONFIG_FILE)
    parser.add_argument("--core",    type=int, default=-1,  help="Pin benchmark to CPU core N (-1 = no pinning)")
    args = parser.parse_args()

    CONFIG_FILE = args.config

    _load_config()
    if args.players is not None:
        _config["players"] = args.players
    if args.matches is not None:
        _config["matches"] = args.matches
    if args.core >= 0:
        _config["core"] = args.core
    if args.players is not None or args.matches is not None:
        _save_config()

    players = _config["players"]

    print("=" * 60)
    print("Worst-Case Benchmark Live Monitor")
    print(f"  Control : http://localhost:{args.port}/")
    print(f"  Metrics : http://localhost:{args.port}/metrics/prometheus")
    print(f"  Config  : {CONFIG_FILE}")
    print(f"  Players : {players}")
    print(f"  Exe     : {_find_exe() or '(not found — monitoring log only)'}")
    print("=" * 60)

    # Start benchmark exe
    if not args.no_start:
        _start_benchmark(players)

    # Tail log as fallback
    tail_t = threading.Thread(target=_tail_log_thread, daemon=True, name="log-tailer")
    tail_t.start()

    # Watchdog
    wd_t = threading.Thread(target=_watchdog_thread, daemon=True, name="watchdog")
    wd_t.start()

    # HTTP server
    server = HTTPServer(("0.0.0.0", args.port), Handler)
    print(f"[agent] HTTP server listening on :{args.port}")

    def _shutdown(sig, frame):
        global _running
        _running = False
        _kill_current()
        server.shutdown()

    signal.signal(signal.SIGINT,  _shutdown)
    signal.signal(signal.SIGTERM, _shutdown)

    try:
        server.serve_forever()
    except Exception:
        pass
    print("[agent] Stopped.")

if __name__ == "__main__":
    main()
