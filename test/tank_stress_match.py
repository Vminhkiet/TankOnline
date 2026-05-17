#!/usr/bin/env python3
"""
Tank C++ Server — Active Match Stress Test
==========================================
Inject match.create → Kafka → C++ server creates matches
Per-player UDP socket → server registers each player session separately
Ramp: 1 → 5 → 10 → 20 → 32 matches (mỗi match 10 players)

Key fix: mỗi player có 1 socket riêng (unique src port)
→ resolvePlayer() assigns slot theo source addr
"""

import json, math, socket, threading, time, subprocess, sys
from datetime import datetime

# ── Config ────────────────────────────────────────────────────────────────────
KAFKA_BROKER     = "172.25.203.168:9092"
SERVER_HOST      = "172.25.192.1"   # Windows host IP
SERVER_PORT      = 8080
BASE_MATCH_ID    = 8000
KEEPALIVE_HZ     = 15              # UDP/s per player
BUDGET_US        = 16_667

RAMP = [
    {"matches":  1, "ppm": 2,  "secs": 25, "label": "Warm-up"},
    {"matches":  5, "ppm": 4,  "secs": 30, "label": "Light  "},
    {"matches": 10, "ppm": 6,  "secs": 35, "label": "Medium "},
    {"matches": 20, "ppm": 8,  "secs": 40, "label": "Heavy  "},
    {"matches": 32, "ppm": 10, "secs": 40, "label": "Peak   "},
    {"matches": 10, "ppm":  4, "secs": 20, "label": "Cooldown"},
]

# ── Bit-packed UDP packet ──────────────────────────────────────────────────────
def _bits(lo, hi):
    return max(1, math.ceil(math.log2(hi - lo + 1))) if hi > lo else 1

class _BW:
    def __init__(self): self.b, self.c, self.n = bytearray(), 0, 0
    def w(self, v, lo, hi):
        nb = _bits(lo, hi); self.c |= ((v - lo) << self.n); self.n += nb
        while self.n >= 8: self.b.append(self.c & 0xFF); self.c >>= 8; self.n -= 8
    def end(self):
        if self.n: self.b.append(self.c & 0xFF)
        return bytes(self.b)

_MOVE_SIZE = 12   # ceil(91 bits / 8) = 12 bytes
_DIR = [(1,2),(2,1),(1,0),(0,1),(2,2),(0,0),(1,1),(2,0)]

def move_pkt(match_id, player_id, seq, dx, dz):
    bw = _BW()
    bw.w(_MOVE_SIZE, 8,  1400)
    bw.w(1001,       0, 65535)
    bw.w(match_id,   0, 1000000)
    bw.w(player_id,  0, 255)
    bw.w(seq % 256,  0, 255)
    bw.w(0,          0, 65535)
    bw.w(dx,         0, 2)
    bw.w(dz,         0, 2)
    bw.w(0,          0, 255)
    return bw.end()

# ── Kafka inject ──────────────────────────────────────────────────────────────
def kafka_inject(match_list):
    """[(match_id, [player_ids])] → produce to Kafka in one exec call."""
    lines = "\n".join(json.dumps({
        "matchId": mid, "mapName": "world",
        "maxDuration": 300, "players": pids
    }) for mid, pids in match_list)

    proc = subprocess.run(
        ["docker", "exec", "-i", "kafka",
         "kafka-console-producer",
         "--bootstrap-server", "localhost:9092",
         "--topic", "match.create"],
        input=lines.encode(), capture_output=True, timeout=20
    )
    return proc.returncode == 0

# ── Per-player keepalive engine ───────────────────────────────────────────────
class KeepAlive:
    """
    Each (match_id, player_idx) owns its own UDP socket bound to a unique
    ephemeral port → server resolvePlayer() assigns a distinct session slot.
    """
    def __init__(self):
        self._socks   = {}   # (mid, pidx) → socket
        self._players = {}   # (mid, pidx) → (match_id, player_id_value)
        self._seq     = {}   # (mid, pidx) → seq
        self._lock    = threading.Lock()
        self._running = False
        self._thread  = None
        self._sent    = 0
        self._err     = 0

    def add(self, match_id, player_ids):
        with self._lock:
            for idx, pid in enumerate(player_ids):
                key = (match_id, idx)
                if key not in self._socks:
                    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
                    s.bind(('', 0))       # OS assigns unique ephemeral port
                    s.setblocking(False)
                    self._socks[key]   = s
                    self._players[key] = (match_id, pid)
                    self._seq[key]     = 0

    def remove(self, match_ids):
        with self._lock:
            to_del = [k for k in self._socks if k[0] in match_ids]
            for k in to_del:
                try: self._socks[k].close()
                except: pass
                del self._socks[k]
                self._players.pop(k, None)
                self._seq.pop(k, None)

    def start(self):
        self._running = True
        self._thread  = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

    def stop(self):
        self._running = False
        if self._thread: self._thread.join(timeout=5)
        with self._lock:
            for s in self._socks.values():
                try: s.close()
                except: pass
            self._socks.clear()

    def _loop(self):
        interval = 1.0 / KEEPALIVE_HZ
        cycle = 0
        while self._running:
            t0 = time.monotonic()
            dx, dz = _DIR[cycle % len(_DIR)]; cycle += 1
            with self._lock:
                snap = list(self._socks.items())
            for key, sock in snap:
                mid, pid = self._players.get(key, (0, 0))
                seq      = self._seq.get(key, 0)
                try:
                    pkt = move_pkt(mid, pid, seq, dx, dz)
                    sock.sendto(pkt, (SERVER_HOST, SERVER_PORT))
                    self._sent += 1
                except: self._err += 1
                self._seq[key] = (seq + 1) % 256
            dt = time.monotonic() - t0
            if dt < interval: time.sleep(interval - dt)

    def stats(self):
        with self._lock:
            nm = len(set(k[0] for k in self._socks))
            np = len(self._socks)
        return nm, np, self._sent, self._err

# ── Server log polling ────────────────────────────────────────────────────────
SERVER_LOG = "/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release/server.log"
PERF_RE    = __import__('re').compile(
    r'\[Perf\]\s+ticks=\d+\s+matches=(\d+).*?tick avg=(\d+).*?max=(\d+).*?overruns=(\d+)')

def last_perf():
    try:
        with open(SERVER_LOG, "r", encoding="utf-8", errors="replace") as f:
            f.seek(0, 2); size = f.tell()
            f.seek(max(0, size - 65536))
            chunk = f.read()
        matches = PERF_RE.findall(chunk)
        if matches:
            m = matches[-1]
            return int(m[0]), int(m[1]), int(m[2]), int(m[3])
    except: pass
    return None, None, None, None

def wait_log_updated(prev_matches, timeout=8):
    """Wait until server log shows new [Perf] line."""
    t0 = time.time()
    while time.time() - t0 < timeout:
        am, *_ = last_perf()
        if am != prev_matches or am is not None:
            return True
        time.sleep(1)
    return False

# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    print()
    print("═" * 66)
    print("  Tank C++ Server — Match Capacity Stress Test")
    print(f"  {datetime.now():%Y-%m-%d %H:%M:%S}  |  server={SERVER_HOST}:{SERVER_PORT}")
    print("═" * 66)

    ka = KeepAlive()
    ka.start()

    match_ctr   = BASE_MATCH_ID
    active      = []   # [(mid, [pids])]
    results     = []

    try:
        for lvl in RAMP:
            target = lvl["matches"]
            ppm    = lvl["ppm"]
            secs   = lvl["secs"]
            label  = lvl["label"]

            print(f"\n  {'─'*62}")
            print(f"  {label}  →  {target} matches × {ppm} players  "
                  f"({target*ppm} total players)")
            print(f"  {'─'*62}")

            cur = len(active)

            if cur < target:
                batch = []
                for _ in range(target - cur):
                    mid  = match_ctr; match_ctr += 1
                    pids = list(range(1, ppm + 1))
                    batch.append((mid, pids))
                    active.append((mid, pids))

                print(f"  Kafka inject {len(batch)} matches...", end="", flush=True)
                ok = kafka_inject(batch)
                print(" OK" if ok else " FAILED")

                # Pre-register UDP sockets BEFORE server processes Kafka
                for mid_b, pids_b in batch:
                    ka.add(mid_b, pids_b)
                time.sleep(6)          # let server consume Kafka events

            elif cur > target:
                remove_ids = [m for m,_ in active[target:]]
                active = active[:target]
                ka.remove(remove_ids)

            nm, np, sent, err = ka.stats()
            print(f"  KeepAlive: {nm} matches, {np} sockets, "
                  f"~{np * KEEPALIVE_HZ} pkt/s")

            # ── Sampling ──────────────────────────────────────────────────────
            print(f"  Sampling {secs}s", end="", flush=True)
            samples = []
            t_end   = time.time() + secs
            while time.time() < t_end:
                am, avg, p99, ov = last_perf()
                if am is not None:
                    samples.append((am, avg, p99, ov))
                    bp = p99 / BUDGET_US * 100
                    print(f"\r  [{label}] matches={am:>3} avg={avg:>6}µs "
                          f"p99={p99:>6}µs budget={bp:>5.1f}%  "
                          f"({max(0, t_end-time.time()):.0f}s)", end="", flush=True)
                time.sleep(4)
            print()

            # Aggregate
            if samples:
                avg_am  = sum(s[0] for s in samples) / len(samples)
                avg_avg = sum(s[1] for s in samples) / len(samples)
                avg_p99 = sum(s[2] for s in samples) / len(samples)  # using tick_max as worst-case
                avg_ov  = sum(s[3] for s in samples) / len(samples)
                bp      = avg_p99 / BUDGET_US * 100
                status  = ("EXCELLENT" if bp<30 else "GOOD" if bp<60
                           else "OK" if bp<85 else "WARNING" if bp<100 else "OVERLOAD")
            else:
                avg_am=avg_avg=avg_p99=avg_ov=bp=0; status="NO DATA"

            results.append(dict(
                label=label.strip(), target=target, ppm=ppm,
                total_players=target*ppm,
                active_matches=round(avg_am,1),
                tick_avg=round(avg_avg,1), tick_p99=round(avg_p99,1),
                overruns=round(avg_ov,1), budget_pct=round(bp,1),
                status=status
            ))
            print(f"  → avg={avg_avg:.0f}µs  p99={avg_p99:.0f}µs  "
                  f"budget={bp:.1f}%  {status}")

    finally:
        print("\n  Stopping keepalive...")
        ka.stop()

    # ── Report ────────────────────────────────────────────────────────────────
    print()
    print("╔" + "═"*74 + "╗")
    print("║  C++ Server Capacity — Final Report" + " "*38 + "║")
    print("╠" + "═"*74 + "╣")
    hdr = f"║  {'Phase':<10}{'Matches':>8}{'Players':>8}{'Active':>8}{'tick_avg':>10}{'tick_p99':>10}{'Budget%':>9}  {'Status':<10}║"
    print(hdr)
    print("╠" + "═"*74 + "╣")
    for r in results:
        print(f"║  {r['label']:<10}{r['target']:>8}{r['total_players']:>8}"
              f"{r['active_matches']:>8.0f}{r['tick_avg']:>9.0f}µs"
              f"{r['tick_p99']:>9.0f}µs{r['budget_pct']:>8.1f}%  {r['status']:<10}║")
    print("╠" + "═"*74 + "╣")
    print(f"║  Budget = {BUDGET_US:,}µs (60 Hz) · Server MAX_CONCURRENT_MATCHES = 64" + " "*13 + "║")
    print("╚" + "═"*74 + "╝")
    print()

if __name__ == "__main__":
    main()
