#!/usr/bin/env python3
"""
latency_measure.py — Client-side experience measurement
========================================================
Đo 3 chỉ số client experience (tương đương Valorant 128-tick analysis):

  1. shot_reg_latency  : C2S_SHOOT gửi đi → S2C_SNAPSHOT có bullet xuất hiện
  2. snapshot_interval : khoảng cách giữa 2 snapshot liên tiếp (= 1/effective_Hz)
  3. snapshot_jitter   : stddev của snapshot_interval (OS scheduling noise)

Chạy lần lượt qua các mức tải nền: 0 / 5 / 10 / 20 / 32 match đồng thời.
So sánh output:

  Load       | shot_reg p50 | shot_reg p99 | eff.Hz | jitter
  0  matches |   ~17ms      |   ~25ms      | ~60Hz  | ~1ms
  32 matches |   ???        |   ???        | ???    | ???
"""

import json, math, socket, struct, time, subprocess, re, statistics
from datetime import datetime

# ── Config ────────────────────────────────────────────────────────────────────
SERVER_HOST    = "172.25.192.1"
SERVER_PORT    = 8080
SERVER_LOG     = "/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release/server.log"
BUDGET_US      = 16_667
MEASURE_MID    = 19999   # match dùng để đo latency
BG_BASE_MID    = 20000   # match IDs cho background load
N_SHOTS        = 60      # số lần bắn đo mỗi level
BG_PPM         = 4       # players per background match (keepalive only)

LOAD_LEVELS = [0, 5, 10, 20, 32]

# ── Bit-packing ───────────────────────────────────────────────────────────────
def _bits(lo, hi): return max(1, math.ceil(math.log2(hi - lo + 1))) if hi > lo else 1

class _BW:
    def __init__(self): self.b, self.c, self.n = bytearray(), 0, 0
    def w(self, v, lo, hi):
        nb = _bits(lo, hi); self.c |= ((v - lo) << self.n); self.n += nb
        while self.n >= 8: self.b.append(self.c & 0xFF); self.c >>= 8; self.n -= 8
    def end(self):
        if self.n: self.b.append(self.c & 0xFF)
        return bytes(self.b)

_DIR = [(1,2),(2,1),(1,0),(0,1)]

def move_pkt(mid, pid, seq, dx=1, dz=1):
    bw = _BW()
    bw.w(12, 8, 1400); bw.w(1001, 0, 65535)
    bw.w(mid, 0, 1000000); bw.w(pid, 0, 255)
    bw.w(seq % 256, 0, 255); bw.w(0, 0, 65535)
    bw.w(dx, 0, 2); bw.w(dz, 0, 2); bw.w(0, 0, 255)
    return bw.end()

def shoot_pkt(mid, pid, seq, force=22):
    bw = _BW()
    bw.w(11, 8, 1400); bw.w(1002, 0, 65535)
    bw.w(mid, 0, 1000000); bw.w(pid, 0, 255)
    bw.w(seq % 256, 0, 255); bw.w(0, 0, 65535)
    bw.w(force, 15, 30)
    return bw.end()

# ── S2C_SNAPSHOT parser ────────────────────────────────────────────────────────
# Offset 0: matchId(uint32), 4: opcode(uint16)=2000, 6: serverTick(uint16),
#           8: tankCount(uint16), 10: TankState[N×23], 10+N*23: bulletCount(uint16),
#           then BulletState[M×16]
S2C_SNAPSHOT = 2000
TANK_SIZE    = 23
BULLET_SIZE  = 16

def parse_snapshot(data):
    """Returns (serverTick, tankCount, bulletCount) or None."""
    if len(data) < 10:
        return None
    matchId, opcode, serverTick, tankCount = struct.unpack_from('<IHHH', data, 0)
    if opcode != S2C_SNAPSHOT:
        return None
    bc_offset = 10 + tankCount * TANK_SIZE
    if len(data) < bc_offset + 2:
        return None
    bulletCount = struct.unpack_from('<H', data, bc_offset)[0]
    return serverTick, tankCount, bulletCount

# ── Kafka inject ──────────────────────────────────────────────────────────────
def kafka_inject(match_list):
    lines = "\n".join(json.dumps({
        "matchId": mid, "mapName": "world", "maxDuration": 300, "players": pids
    }) for mid, pids in match_list)
    r = subprocess.run(
        ["docker", "exec", "-i", "kafka", "kafka-console-producer",
         "--bootstrap-server", "localhost:9092", "--topic", "match.create"],
        input=lines.encode(), capture_output=True, timeout=20
    )
    return r.returncode == 0

# ── Background keepalive ──────────────────────────────────────────────────────
import threading

class BgLoad:
    """Giữ N match sống bằng C2S_MOVE keepalive @ 15 Hz."""
    def __init__(self):
        self._socks = {}; self._seq = {}
        self._lock = threading.Lock(); self._running = False

    def add(self, mid, pids):
        with self._lock:
            for i, pid in enumerate(pids):
                key = (mid, i)
                s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
                s.bind(('', 0)); s.setblocking(False)
                self._socks[key] = (s, mid, pid); self._seq[key] = 0

    def start(self):
        self._running = True
        threading.Thread(target=self._loop, daemon=True).start()

    def stop(self): self._running = False

    def clear(self):
        with self._lock:
            for s, _, _ in self._socks.values():
                try: s.close()
                except: pass
            self._socks.clear(); self._seq.clear()

    def _loop(self):
        interval = 1/15; cycle = 0
        while self._running:
            t0 = time.monotonic()
            dx, dz = _DIR[cycle % len(_DIR)]; cycle += 1
            with self._lock:
                snap = list(self._socks.items())
            for key, (s, mid, pid) in snap:
                seq = self._seq.get(key, 0)
                try: s.sendto(move_pkt(mid, pid, seq, dx, dz), (SERVER_HOST, SERVER_PORT))
                except: pass
                self._seq[key] = (seq + 1) % 256
            dt = time.monotonic() - t0
            if dt < interval: time.sleep(interval - dt)

# ── Probe (measurement socket) ────────────────────────────────────────────────
class LatencyProbe:
    """
    1 socket duy nhất (player 1, MEASURE_MID).
    Gửi keepalive để server biết địa chỉ → server gửi S2C_SNAPSHOT về đây.
    """
    def __init__(self, mid=MEASURE_MID, pid=1):
        self.mid = mid; self.pid = pid
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(('', 0))
        self.sock.settimeout(0.8)
        self.addr = (SERVER_HOST, SERVER_PORT)
        self.seq = 0

    def keepalive(self, n=3):
        for _ in range(n):
            self.sock.sendto(move_pkt(self.mid, self.pid, self.seq), self.addr)
            self.seq = (self.seq + 1) % 256
            time.sleep(0.07)

    def flush_recv(self, ms=200):
        """Drain buffer trước khi đo để tránh stale snapshots."""
        deadline = time.monotonic() + ms/1000
        while time.monotonic() < deadline:
            try: self.sock.recvfrom(4096)
            except: break

    def measure_shot_reg(self):
        """
        Gửi C2S_SHOOT tại t0.
        Đợi S2C_SNAPSHOT đầu tiên có bulletCount > 0 tại t1.
        Trả về latency ms hoặc None nếu timeout.
        """
        self.keepalive(2)
        self.flush_recv()

        t0 = time.monotonic()
        self.sock.sendto(shoot_pkt(self.mid, self.pid, self.seq), self.addr)
        self.seq = (self.seq + 1) % 256

        deadline = t0 + 1.0  # 1s timeout
        while time.monotonic() < deadline:
            try:
                data, _ = self.sock.recvfrom(4096)
            except socket.timeout:
                continue
            result = parse_snapshot(data)
            if result and result[2] > 0:   # bulletCount > 0
                return (time.monotonic() - t0) * 1000
        return None  # bullet không xuất hiện trong 1s

    def measure_snapshot_rate(self, duration_s=5.0):
        """
        Passive: nhận snapshots trong duration_s giây.
        Trả về (intervals_ms list, tick_skips).
        """
        self.keepalive(1)
        intervals = []
        last_t = None
        last_tick = None
        tick_skips = 0
        deadline = time.monotonic() + duration_s

        while time.monotonic() < deadline:
            try:
                data, _ = self.sock.recvfrom(4096)
            except socket.timeout:
                self.keepalive(1)
                continue
            result = parse_snapshot(data)
            if not result:
                continue
            serverTick, _, _ = result
            now = time.monotonic()

            if last_t is not None:
                intervals.append((now - last_t) * 1000)
            last_t = now

            if last_tick is not None:
                skip = (serverTick - last_tick) & 0xFFFF
                if skip > 1:
                    tick_skips += skip - 1
            last_tick = serverTick

        return intervals, tick_skips

    def close(self): self.sock.close()

# ── Server perf log reader ────────────────────────────────────────────────────
PERF_RE = re.compile(
    r'\[Perf\]\s+ticks=\d+\s+matches=(\d+).*?tick avg=(\d+).*?max=(\d+).*?overruns=(\d+)')

def last_perf():
    try:
        with open(SERVER_LOG, "r", encoding="utf-8", errors="replace") as f:
            f.seek(0, 2); sz = f.tell()
            f.seek(max(0, sz - 32768)); chunk = f.read()
        ms = PERF_RE.findall(chunk)
        if ms:
            m = ms[-1]
            return int(m[0]), int(m[1]), int(m[2]), int(m[3])
    except: pass
    return None, None, None, None

# ── Main ──────────────────────────────────────────────────────────────────────
def pct(lst, p):
    if not lst: return 0
    s = sorted(lst)
    return s[min(int(len(s)*p/100), len(s)-1)]

def run_level(load_matches, match_ctr, bg):
    """Inject thêm match nền nếu cần, đo latency, trả về result dict."""
    cur = match_ctr

    if load_matches > 0:
        batch = [(cur + i, list(range(1, BG_PPM+1))) for i in range(load_matches)]
        match_ctr += load_matches
        print(f"   Kafka inject {load_matches} bg matches...", end="", flush=True)
        kafka_inject(batch)
        print(" OK")
        for mid, pids in batch:
            bg.add(mid, pids)
        time.sleep(8)  # chờ matches khởi động

    probe = LatencyProbe()

    # Đảm bảo measurement match đã tạo và probe đăng ký địa chỉ
    probe.keepalive(5)
    time.sleep(1)

    # 1. Đo shot registration latency
    print(f"   Đo shot registration ({N_SHOTS} shots)...", end="", flush=True)
    shot_samples = []
    timeouts = 0
    for i in range(N_SHOTS):
        lat = probe.measure_shot_reg()
        if lat is not None:
            shot_samples.append(lat)
        else:
            timeouts += 1
        if (i+1) % 10 == 0:
            print(".", end="", flush=True)
        time.sleep(2.5)  # chờ bullet hết hạn trước khi bắn tiếp
    print()

    # 2. Đo snapshot rate và jitter
    print(f"   Đo snapshot jitter (5s)...", end="", flush=True)
    intervals, tick_skips = probe.measure_snapshot_rate(5.0)
    print(" OK")

    # 3. Đọc server Perf log
    srv_matches, srv_avg, srv_max, srv_ov = last_perf()

    probe.close()

    # Tính stats
    eff_hz = (1000 / statistics.mean(intervals)) if intervals else 0
    jitter = statistics.stdev(intervals) if len(intervals) > 1 else 0

    return {
        "load_matches"  : load_matches,
        "shot_n"        : len(shot_samples),
        "shot_timeouts" : timeouts,
        "shot_p50"      : round(pct(shot_samples, 50), 1),
        "shot_p95"      : round(pct(shot_samples, 95), 1),
        "shot_p99"      : round(pct(shot_samples, 99), 1),
        "shot_max"      : round(max(shot_samples), 1) if shot_samples else 0,
        "eff_hz"        : round(eff_hz, 1),
        "jitter_ms"     : round(jitter, 2),
        "tick_skips"    : tick_skips,
        "srv_avg_us"    : srv_avg or 0,
        "srv_matches"   : srv_matches or 0,
    }, match_ctr


def main():
    print()
    print("═"*68)
    print("  Tank Server — Client Experience Measurement (Valorant-style)")
    print(f"  {datetime.now():%Y-%m-%d %H:%M:%S}")
    print(f"  Đo: shot_registration_latency / snapshot_hz / jitter")
    print(f"  Load levels: {LOAD_LEVELS} background matches")
    print("═"*68)

    # Tạo measurement match trước
    print(f"\n  Tạo measurement match (id={MEASURE_MID})...", end="", flush=True)
    kafka_inject([(MEASURE_MID, [1, 2])])
    print(" OK")
    time.sleep(6)

    bg = BgLoad(); bg.start()
    match_ctr = BG_BASE_MID
    results = []

    try:
        for lvl in LOAD_LEVELS:
            print(f"\n  {'─'*64}")
            print(f"  Load: {lvl} background matches")
            print(f"  {'─'*64}")
            result, match_ctr = run_level(lvl, match_ctr, bg)
            results.append(result)

            print(f"  → shot p50={result['shot_p50']}ms  p99={result['shot_p99']}ms  "
                  f"max={result['shot_max']}ms  timeout={result['shot_timeouts']}")
            print(f"  → snapshot: {result['eff_hz']}Hz  jitter={result['jitter_ms']}ms  "
                  f"tick_skips={result['tick_skips']}")
            print(f"  → server: tick_avg={result['srv_avg_us']}µs  "
                  f"active_matches={result['srv_matches']}")

    finally:
        bg.stop(); bg.clear()

    # ── Final Report ──────────────────────────────────────────────────────────
    print()
    print("╔" + "═"*78 + "╗")
    print("║  Client Experience Report — Valorant-style Tick Analysis" + " "*21 + "║")
    print("╠" + "═"*78 + "╣")
    print(f"║  {'BgLoad':>7} {'shotP50':>8} {'shotP95':>8} {'shotP99':>8} {'shotMax':>8}"
          f" {'effHz':>7} {'jitter':>8} {'tkSkip':>7} {'srvAvg':>8}  ║")
    print("╠" + "═"*78 + "╣")
    for r in results:
        print(f"║  {r['load_matches']:>7} {r['shot_p50']:>7}ms {r['shot_p95']:>7}ms "
              f"{r['shot_p99']:>7}ms {r['shot_max']:>7}ms"
              f" {r['eff_hz']:>6}Hz {r['jitter_ms']:>7}ms {r['tick_skips']:>7} "
              f"{r['srv_avg_us']:>6}µs  ║")
    print("╠" + "═"*78 + "╣")

    # So sánh Valorant
    print(f"║  Valorant 64-tick  : shot_reg max = 15.6ms  (1 tick @ 64Hz)" + " "*17 + "║")
    print(f"║  Valorant 128-tick : shot_reg max =  7.8ms  (1 tick @ 128Hz)" + " "*16 + "║")
    print(f"║  Our server 60-tick: shot_reg max = 16.7ms  (theoretical)" + " "*19 + "║")
    print("╠" + "═"*78 + "╣")

    # Tự động nhận xét
    base = results[0] if results else None
    peak = results[-1] if results else None
    if base and peak and base['shot_p50'] > 0:
        delta = peak['shot_p99'] - base['shot_p99']
        hz_drop = base['eff_hz'] - peak['eff_hz']
        print(f"║  Kết luận: p99 tăng {delta:+.1f}ms  |  "
              f"Hz drop {hz_drop:+.1f}  |  "
              f"tick_skips={peak['tick_skips']}"
              + " "*(78 - len(f"  Kết luận: p99 tăng {delta:+.1f}ms  |  Hz drop {hz_drop:+.1f}  |  tick_skips={peak['tick_skips']}") - 2)
              + "║")
    print("╚" + "═"*78 + "╝")
    print()

    print("  Giải thích các cột:")
    print("  shot_p50/p99/max : latency từ lúc bắn đến khi snapshot có bullet (ms)")
    print("  eff_hz           : tần suất snapshot thực tế nhận được ở client")
    print("  jitter           : stddev khoảng cách snapshot (noise)")
    print("  tick_skips       : số tick server bị skip (overrun → client không nhận đủ)")
    print("  srvAvg           : tick avg phía server (µs)")


if __name__ == "__main__":
    main()
