#!/usr/bin/env python3
"""
Tank C++ Server — 10-Player Staircase Stress Test
==================================================
Inject match.create → Kafka → C++ server creates matches
Per-player UDP socket → server registers each player session separately
All matches run with PPM=10 (max capacity, 10 spawn points).

Staircase pattern: add --step matches every --duration seconds.
Stops automatically when tick_max > 16,667µs (budget exceeded).

Usage:
  python3 tank_stress_match.py [--step 5] [--duration 120] [--max 50] [--base-id 30000]
"""

import argparse, json, math, socket, threading, time, subprocess, sys
from datetime import datetime

# ── Config ────────────────────────────────────────────────────────────────────
KAFKA_BROKER     = "172.25.203.168:9092"
SERVER_HOST      = "172.25.192.1"   # Windows host IP
SERVER_PORT      = 8080
KEEPALIVE_HZ     = 15              # move packets/s per player
SHOOT_EVERY      = 10              # shoot every Nth move cycle → 15/10 = 1.5 shots/s
BUDGET_US        = 16_667
PPM              = 10              # players per match (fixed — 10 spawn points in world.json)

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

def shoot_pkt(match_id, player_id, seq, force=22):
    """C2S_SHOOT = 1002, ~11 bytes bit-packed. force in [15..30]."""
    bw = _BW()
    bw.w(11,        8,  1400)    # packetSize
    bw.w(1002,      0, 65535)    # opcode C2S_SHOOT
    bw.w(match_id,  0, 1000000)
    bw.w(player_id, 0, 255)
    bw.w(seq % 256, 0, 255)
    bw.w(0,         0, 65535)    # unk
    bw.w(force,    15, 30)       # bullet force
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
            dx, dz = _DIR[cycle % len(_DIR)]
            # Fire every SHOOT_EVERY cycles → 15/10 = 1.5 shots/s per bot
            do_shoot = (cycle % SHOOT_EVERY == 0)
            cycle += 1
            with self._lock:
                snap = list(self._socks.items())
            for key, sock in snap:
                mid, pid = self._players.get(key, (0, 0))
                seq      = self._seq.get(key, 0)
                try:
                    sock.sendto(move_pkt(mid, pid, seq, dx, dz),
                                (SERVER_HOST, SERVER_PORT))
                    self._sent += 1
                    if do_shoot:
                        sock.sendto(shoot_pkt(mid, pid, seq + 1),
                                    (SERVER_HOST, SERVER_PORT))
                        self._sent += 1
                except:
                    self._err += 1
                self._seq[key] = (seq + 2 if do_shoot else seq + 1) % 256
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
    """Search backward from EOF in 256KB chunks until a [Perf] line is found."""
    try:
        with open(SERVER_LOG, "rb") as f:
            f.seek(0, 2)
            size = f.tell()
            chunk_size = 262144   # 256KB per pass
            offset = size
            while offset > 0:
                offset = max(0, offset - chunk_size)
                f.seek(offset)
                chunk = f.read(chunk_size + 512).decode("utf-8", errors="replace")
                matches = PERF_RE.findall(chunk)
                if matches:
                    m = matches[-1]
                    return int(m[0]), int(m[1]), int(m[2]), int(m[3])
                if offset == 0:
                    break
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
    parser = argparse.ArgumentParser(
        description="Tank C++ Server — 10-player staircase GPC benchmark")
    parser.add_argument("--step",     type=int, default=5,     metavar="N",
                        help="Matches to add per step (default 5 → 50 bots/step)")
    parser.add_argument("--duration", type=int, default=120,   metavar="SECS",
                        help="Observation window per step in seconds (default 120)")
    parser.add_argument("--max",      type=int, default=50,    metavar="N",
                        help="Hard limit on total matches (default 50)")
    parser.add_argument("--base-id",  type=int, default=30000, metavar="ID",
                        help="Starting match ID (default 30000)")
    args = parser.parse_args()

    STEP_SIZE = args.step
    STEP_SECS = args.duration
    MAX_MATCHES = args.max
    match_ctr = args.base_id

    print()
    print("═" * 68)
    print("  Tank C++ Server — 10-Player Staircase GPC Benchmark")
    print(f"  {datetime.now():%Y-%m-%d %H:%M:%S}  |  server={SERVER_HOST}:{SERVER_PORT}")
    print(f"  step={STEP_SIZE} matches ({STEP_SIZE*PPM} bots)  "
          f"duration={STEP_SECS}s  max={MAX_MATCHES} matches")
    print(f"  Stop condition: tick_max > {BUDGET_US:,}µs")
    print("═" * 68)

    ka = KeepAlive()
    ka.start()

    active  = []   # [(mid, [pids])]
    results = []

    try:
        step_num = 0
        while len(active) < MAX_MATCHES:
            step_num += 1
            to_add = min(STEP_SIZE, MAX_MATCHES - len(active))
            total  = len(active) + to_add

            print(f"\n  {'─'*64}")
            print(f"  Step {step_num}  +{to_add} matches → {total} total  "
                  f"({total * PPM} bots,  {total * PPM * KEEPALIVE_HZ} move/s  "
                  f"+ {total * PPM * KEEPALIVE_HZ // SHOOT_EVERY} shoot/s)")
            print(f"  {'─'*64}")

            # Inject new matches to Kafka
            batch = []
            for _ in range(to_add):
                mid  = match_ctr; match_ctr += 1
                pids = list(range(1, PPM + 1))
                batch.append((mid, pids))
                active.append((mid, pids))

            print(f"  Kafka inject {to_add} matches...", end="", flush=True)
            ok = kafka_inject(batch)
            print(" OK" if ok else " FAILED")

            for mid_b, pids_b in batch:
                ka.add(mid_b, pids_b)
            time.sleep(6)   # let server consume Kafka events and spawn sessions

            # ── Observation window ────────────────────────────────────────────
            # Skip first 20s to let connection storm settle before measuring
            time.sleep(20)

            samples       = []
            overload      = False
            overload_runs = 0   # consecutive overload samples
            t_end         = time.time() + STEP_SECS
            while time.time() < t_end:
                am, avg, tick_max, ov = last_perf()
                if am is not None:
                    samples.append((am, avg, tick_max, ov))
                    bp = tick_max / BUDGET_US * 100
                    remain = max(0, t_end - time.time())
                    flag = " !!OVER!!" if tick_max > BUDGET_US else ""
                    print(f"\r  matches={am:>3}  avg={avg:>6}µs  "
                          f"max={tick_max:>6}µs  budget={bp:>5.1f}%  "
                          f"{remain:.0f}s{flag}     ", end="", flush=True)
                    overload_runs = (overload_runs + 1) if tick_max > BUDGET_US else 0
                    if overload_runs >= 3:
                        overload = True
                        print(f"\n  !! SUSTAINED OVERLOAD — GPC limit: {total} matches "
                              f"/ {total * PPM} players !!")
                        break
                time.sleep(4)
            print()

            # Aggregate this step
            if samples:
                avg_am  = sum(s[0] for s in samples) / len(samples)
                avg_avg = sum(s[1] for s in samples) / len(samples)
                avg_max = sum(s[2] for s in samples) / len(samples)
                avg_ov  = sum(s[3] for s in samples) / len(samples)
                bp = avg_max / BUDGET_US * 100
                status = ("GOOD" if bp < 60 else "OK" if bp < 85
                          else "WARNING" if bp < 100 else "OVERLOAD")
            else:
                avg_am = avg_avg = avg_max = avg_ov = bp = 0
                status = "NO DATA"

            results.append(dict(
                step=step_num, matches=total, players=total * PPM,
                active=round(avg_am, 1),
                tick_avg=round(avg_avg),
                tick_max=round(avg_max),
                budget_pct=round(bp, 1),
                status=status,
            ))
            print(f"  → avg={avg_avg:.0f}µs  max={avg_max:.0f}µs  "
                  f"budget={bp:.1f}%  {status}")

            if overload:
                break

    finally:
        print("\n  Stopping keepalive...")
        ka.stop()

    # ── Final report ──────────────────────────────────────────────────────────
    W = 76
    print()
    print("╔" + "═"*(W-2) + "╗")
    print("║  C++ Server GPC Benchmark — 10 Players/Match" + " "*(W-48) + "║")
    print("╠" + "═"*(W-2) + "╣")
    print(f"║  {'Step':>4}  {'Matches':>7}  {'Bots':>5}  {'Active':>6}  "
          f"{'tick_avg':>9}  {'tick_max':>9}  {'Budget%':>8}  {'Status':<10}║")
    print("╠" + "═"*(W-2) + "╣")
    for r in results:
        flag = " ←" if r["status"] in ("OVERLOAD", "WARNING") else ""
        print(f"║  {r['step']:>4}  {r['matches']:>7}  {r['players']:>5}  "
              f"{r['active']:>6.0f}  {r['tick_avg']:>8}µs  {r['tick_max']:>8}µs  "
              f"{r['budget_pct']:>7.1f}%  {r['status']:<10}{flag}║")
    print("╠" + "═"*(W-2) + "╣")
    print(f"║  Budget = {BUDGET_US:,}µs (60 Hz)  ·  PPM = {PPM}  ·  "
          f"shots = {KEEPALIVE_HZ//SHOOT_EVERY*PPM}/match/s"
          + " "*(W - 59) + "║")
    print("╚" + "═"*(W-2) + "╝")
    print()

if __name__ == "__main__":
    main()
