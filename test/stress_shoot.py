#!/usr/bin/env python3
"""
stress_shoot.py — Tank Server Capacity Test WITH SHOOTING
==========================================================
Giống tank_stress_match.py nhưng mỗi player bắn đạn liên tục.
Mỗi đạn → server tính swept-sphere collision vs tất cả tanks + walls mỗi tick.
→ Đây là workload nặng nhất: O(bullets × tanks) per match per tick.

Ramp: 1 → 5 → 10 → 20 → 32 matches
      Mỗi match: 10 players, mỗi player bắn 1 phát/2 giây
"""

import json, math, socket, threading, time, subprocess, re
from datetime import datetime

# ── Config ────────────────────────────────────────────────────────────────────
KAFKA_BROKER   = "172.25.203.168:9092"
SERVER_HOST    = "172.25.192.1"
SERVER_PORT    = 8080
BASE_MATCH_ID  = 10000
KEEPALIVE_HZ   = 15          # move packets per second per player
SHOOT_HZ       = 0.5         # shots per second per player (1 shot / 2s)
BUDGET_US      = 16_667
SERVER_LOG     = "/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release/server.log"

RAMP = [
    {"matches":  1, "ppm":  2, "secs": 20, "label": "Warm-up"},
    {"matches":  5, "ppm":  4, "secs": 25, "label": "Light  "},
    {"matches": 10, "ppm":  6, "secs": 30, "label": "Medium "},
    {"matches": 20, "ppm":  8, "secs": 35, "label": "Heavy  "},
    {"matches": 32, "ppm": 10, "secs": 40, "label": "Peak   "},
]

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

_DIR = [(1,2),(2,1),(1,0),(0,1),(2,2),(0,0),(1,1),(2,0)]

def move_pkt(match_id, player_id, seq, dx, dz):
    bw = _BW()
    bw.w(12, 8, 1400); bw.w(1001, 0, 65535)
    bw.w(match_id, 0, 1000000); bw.w(player_id, 0, 255)
    bw.w(seq % 256, 0, 255); bw.w(0, 0, 65535)
    bw.w(dx, 0, 2); bw.w(dz, 0, 2); bw.w(0, 0, 255)
    return bw.end()

def shoot_pkt(match_id, player_id, seq, force=22):
    """C2S_SHOOT = 1002, force in [15..30]"""
    bw = _BW()
    bw.w(11, 8, 1400); bw.w(1002, 0, 65535)
    bw.w(match_id, 0, 1000000); bw.w(player_id, 0, 255)
    bw.w(seq % 256, 0, 255); bw.w(0, 0, 65535)
    bw.w(force, 15, 30)
    return bw.end()

# ── Kafka inject ──────────────────────────────────────────────────────────────
def kafka_inject(match_list):
    lines = "\n".join(json.dumps({
        "matchId": mid, "mapName": "world",
        "maxDuration": 300, "players": pids
    }) for mid, pids in match_list)
    r = subprocess.run(
        ["docker", "exec", "-i", "kafka", "kafka-console-producer",
         "--bootstrap-server", "localhost:9092", "--topic", "match.create"],
        input=lines.encode(), capture_output=True, timeout=20
    )
    return r.returncode == 0

# ── KeepAlive + Shooting engine ───────────────────────────────────────────────
class KeepAlive:
    """
    Mỗi player có 1 socket riêng (unique source port).
    Gửi move @ KEEPALIVE_HZ + shoot @ SHOOT_HZ.
    """
    def __init__(self):
        self._socks   = {}
        self._players = {}
        self._seq     = {}
        self._shoot_t = {}   # last shoot time per player
        self._lock    = threading.Lock()
        self._running = False
        self._sent_move  = 0
        self._sent_shoot = 0

    def add(self, match_id, player_ids):
        with self._lock:
            for idx, pid in enumerate(player_ids):
                key = (match_id, idx)
                if key not in self._socks:
                    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
                    s.bind(('', 0)); s.setblocking(False)
                    self._socks[key]   = s
                    self._players[key] = (match_id, pid)
                    self._seq[key]     = 0
                    self._shoot_t[key] = 0.0

    def remove_all(self):
        with self._lock:
            for s in self._socks.values():
                try: s.close()
                except: pass
            self._socks.clear(); self._players.clear()
            self._seq.clear();   self._shoot_t.clear()

    def start(self):
        self._running = True
        threading.Thread(target=self._loop, daemon=True).start()

    def stop(self): self._running = False

    def _loop(self):
        interval = 1.0 / KEEPALIVE_HZ
        shoot_interval = 1.0 / SHOOT_HZ
        cycle = 0
        while self._running:
            t0  = time.monotonic()
            now = time.time()
            dx, dz = _DIR[cycle % len(_DIR)]; cycle += 1

            with self._lock:
                snap = list(self._socks.items())

            for key, sock in snap:
                mid, pid = self._players.get(key, (0, 0))
                seq      = self._seq.get(key, 0)
                addr     = (SERVER_HOST, SERVER_PORT)

                # Move packet (always)
                try:
                    sock.sendto(move_pkt(mid, pid, seq, dx, dz), addr)
                    self._sent_move += 1
                except: pass

                # Shoot packet (every 1/SHOOT_HZ seconds)
                last = self._shoot_t.get(key, 0.0)
                if now - last >= shoot_interval:
                    try:
                        sock.sendto(shoot_pkt(mid, pid, seq), addr)
                        self._sent_shoot += 1
                    except: pass
                    self._shoot_t[key] = now

                self._seq[key] = (seq + 1) % 256

            dt = time.monotonic() - t0
            if dt < interval: time.sleep(interval - dt)

    def stats(self):
        with self._lock:
            nm = len(set(k[0] for k in self._socks))
            np = len(self._socks)
        return nm, np, self._sent_move, self._sent_shoot

# ── Log reader ────────────────────────────────────────────────────────────────
PERF_RE = re.compile(
    r'\[Perf\]\s+ticks=\d+\s+matches=(\d+).*?tick avg=(\d+).*?max=(\d+).*?overruns=(\d+)')

def last_perf():
    try:
        with open(SERVER_LOG, "r", encoding="utf-8", errors="replace") as f:
            f.seek(0, 2); size = f.tell()
            f.seek(max(0, size - 65536)); chunk = f.read()
        ms = PERF_RE.findall(chunk)
        if ms:
            m = ms[-1]
            return int(m[0]), int(m[1]), int(m[2]), int(m[3])
    except: pass
    return None, None, None, None

# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    print()
    print("═" * 70)
    print("  Tank C++ Server — Stress Test WITH SHOOTING (collision heavy)")
    print(f"  {datetime.now():%Y-%m-%d %H:%M:%S}")
    print(f"  Move: {KEEPALIVE_HZ} pkt/s/player  |  Shoot: {SHOOT_HZ} shot/s/player")
    print(f"  Bullet collision: swept-sphere vs all tanks + walls per tick")
    print("═" * 70)

    print("\n  PRE-CHECK isolation:")
    print("  1. Đã chạy isolate_cores.ps1?  (Ctrl+C nếu chưa)")
    print("  2. Close Chrome/Edge/Teams?")
    print("  3. Power plan = High Performance?")
    time.sleep(3)

    ka = KeepAlive(); ka.start()
    match_ctr = BASE_MATCH_ID
    active    = []
    results   = []

    try:
        for lvl in RAMP:
            target = lvl["matches"]
            ppm    = lvl["ppm"]
            secs   = lvl["secs"]
            label  = lvl["label"]

            total_players  = target * ppm
            total_bullets  = total_players * SHOOT_HZ  # bullets/s total
            bullets_per_tick = total_bullets / 60       # bullets per tick (avg in flight)

            print(f"\n  {'─'*66}")
            print(f"  {label} → {target} matches × {ppm} players = {total_players} players")
            print(f"  Shoot rate: {total_players * SHOOT_HZ:.0f} shots/s  "
                  f"≈ {bullets_per_tick:.1f} bullets active/tick")
            print(f"  Collision checks/tick ≈ {bullets_per_tick:.0f} bullets × {total_players} tanks "
                  f"= {bullets_per_tick * total_players:.0f} checks")
            print(f"  {'─'*66}")

            cur = len(active)
            if cur < target:
                batch = []
                for _ in range(target - cur):
                    mid  = match_ctr; match_ctr += 1
                    pids = list(range(1, ppm + 1))
                    batch.append((mid, pids)); active.append((mid, pids))

                print(f"  Kafka inject {len(batch)} matches...", end="", flush=True)
                ok = kafka_inject(batch)
                print(" OK" if ok else " FAILED")

                for mid_b, pids_b in batch:
                    ka.add(mid_b, pids_b)
                time.sleep(6)

            _, np, s_move, s_shoot = ka.stats()
            print(f"  Sockets: {np}  Move sent: {s_move}  Shoot sent: {s_shoot}")

            # ── Sampling ──────────────────────────────────────────────────────
            print(f"  Sampling {secs}s ", end="", flush=True)
            samples = []
            t_end = time.time() + secs
            while time.time() < t_end:
                am, avg, p99, ov = last_perf()
                if avg is not None:
                    samples.append((am, avg, p99, ov))
                    bp = p99 / BUDGET_US * 100
                    print(f"\r  [{label.strip()}] matches={am:>3} "
                          f"avg={avg:>6}µs p99={p99:>6}µs budget={bp:>5.1f}%  "
                          f"({max(0, t_end-time.time()):.0f}s)", end="", flush=True)
                time.sleep(3)
            print()

            if samples:
                avg_avg = sum(s[1] for s in samples) / len(samples)
                avg_p99 = sum(s[2] for s in samples) / len(samples)
                avg_ov  = sum(s[3] for s in samples) / len(samples)
                bp      = avg_p99 / BUDGET_US * 100
                # CPU per match per tick
                workers = 8
                waves   = math.ceil(target / workers)
                cpu_per_match = avg_avg / waves if waves > 0 else 0
                status = ("EXCELLENT" if bp < 30 else "GOOD" if bp < 60
                          else "OK" if bp < 85 else "WARNING" if bp < 100 else "OVERLOAD")
            else:
                avg_avg = avg_p99 = avg_ov = bp = cpu_per_match = 0
                status = "NO DATA"

            results.append(dict(
                label=label.strip(), target=target, ppm=ppm,
                total_players=total_players,
                bullets_ptick=round(bullets_per_tick, 1),
                tick_avg=round(avg_avg), tick_p99=round(avg_p99),
                cpu_per_match=round(cpu_per_match),
                overruns=round(avg_ov, 1),
                budget_pct=round(bp, 1), status=status
            ))
            print(f"  → avg={avg_avg:.0f}µs  p99={avg_p99:.0f}µs  "
                  f"cpu/match={cpu_per_match:.0f}µs  budget={bp:.1f}%  {status}")

    finally:
        print("\n  Stopping...")
        ka.stop(); ka.remove_all()

    # ── Report ────────────────────────────────────────────────────────────────
    print()
    print("╔" + "═"*80 + "╗")
    print("║  Stress Test WITH SHOOTING — Final Report" + " "*38 + "║")
    print("╠" + "═"*80 + "╣")
    hdr = (f"║  {'Phase':<10}{'Match':>6}{'Player':>7}{'Bullet/tk':>10}"
           f"{'tick_avg':>10}{'tick_p99':>10}{'CPU/match':>10}{'Budget%':>8}  {'Status':<10}║")
    print(hdr)
    print("╠" + "═"*80 + "╣")
    for r in results:
        print(f"║  {r['label']:<10}{r['target']:>6}{r['total_players']:>7}"
              f"{r['bullets_ptick']:>10.1f}"
              f"{r['tick_avg']:>9}µs{r['tick_p99']:>9}µs"
              f"{r['cpu_per_match']:>9}µs{r['budget_pct']:>7.1f}%  {r['status']:<10}║")
    print("╠" + "═"*80 + "╣")
    print(f"║  Shoot rate: {SHOOT_HZ} shot/s/player  |  "
          f"Budget: {BUDGET_US:,}µs  |  Workers: 8" + " "*24 + "║")
    print("╚" + "═"*80 + "╝")
    print()


if __name__ == "__main__":
    main()
