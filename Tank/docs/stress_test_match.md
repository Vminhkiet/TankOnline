# Stress Test Match — C++ Game Server

## 1. Tổng quan

C++ Tank Server được stress test bằng **load_client** — một chương trình Windows riêng biệt mô phỏng N *virtual players* gửi UDP packets vào server theo đúng protocol bit-packed.

```
load_client.exe                        server_tank.exe
───────────────                        ───────────────
WorkerThread[0]                        IOCP Workers (×16)
  VirtualPlayer[0] ──── UDP MOVE ───►  NetworkManager::recv()
  VirtualPlayer[1] ──── UDP MOVE ───►    → routeCommand(matchId)
  VirtualPlayer[2] ──── UDP SHOOT ──►       → Match::pushCommand()
  ...                                           → _cmdQueue
WorkerThread[1]                        MatchManager::tickDispatcher()
  VirtualPlayer[N] ──── UDP MOVE ───►    → Match::tick(dt)  @60Hz
                                            → drain queue
                                            → physics.update()
                                            → broadcastSnapshot()
```

---

## 2. Kiến trúc load_client

### 2.1 Sơ đồ các class

```
main()
  │
  ├─ parseArgs() → Config {host, port, clients, threads,
  │                        duration, tickRate, shootChance, matchId}
  │
  ├─ WorkerThread[0..N-1]   ← mỗi thread quản lý (clients/threads) players
  │   │
  │   └─ run():
  │       ├─ init() → VirtualPlayer.init()  → UDP socket open (bind port=0)
  │       ├─ sendLogin() → C2S_LOGIN packet  → 1 lần khi khởi động
  │       └─ loop @ tickRate Hz:
  │           └─ VirtualPlayer.tick(shootChance)
  │               ├─ buildMove()  → sendTo(server)   ← mỗi tick
  │               ├─ buildShoot() → sendTo(server)   ← xác suất shootChance
  │               └─ recvFrom()   non-blocking drain  ← đo RTT nếu server echo
  │
  └─ stats loop (main thread):
      └─ mỗi giây: printSnapshot(pps, rps, bandwidth)
```

### 2.2 VirtualPlayer — 1 simulated player

Mỗi `VirtualPlayer` có:
- **1 UDP socket riêng** (non-blocking, OS chọn source port)
- **`_seq`** — counter tăng đơn điệu, dùng để đo RTT
- **`_tick`** — local tick counter (không đồng bộ với server tick)
- **`_matchId`** — match nào player này thuộc về

```cpp
// WorkerThread.cpp:22 — mỗi player nhận matchId từ Config
_players.emplace_back(firstClientId + i, serverAddr, _metrics, cfg.matchId);
```

### 2.3 Fixed-rate tick loop

```cpp
// WorkerThread.cpp:45
const Micros tickInterval{ 1'000'000 / tickRate };  // µs giữa 2 tick
auto nextTick = Clock::now();

while (_running) {
    if (now < nextTick) sleep_until(nextTick);
    nextTick = now + tickInterval;

    for (auto& p : _players)
        p.tick(shootChance);           // drive tất cả players trong thread này
}
```

Mỗi thread **không** sleep giữa các player — tick tất cả players trong thread liên tiếp, rồi sleep cho đến đầu tick tiếp theo. Đây là fixed-rate loop tương tự tick dispatcher của server.

---

## 3. Packet Protocol

### 3.1 Định dạng bit-packed (WriteStream)

Tất cả packet dùng `WriteStream` — ghi từng field vào bit stream, serialize thành array of `uint32_t` words.

```
PacketHeader (59 bits → padded lên 2 words = 8 bytes):
  size   [11 bits]  — total packet size in bytes
  opcode [16 bits]  — C2S_LOGIN=1000, C2S_MOVE=1001, C2S_SHOOT=1002
  matchId[20 bits]  — target match ID
  flags  [ 8 bits]  — reserved
  seq    [ 8 bits]  — sequence number (wraps at 256)
  tick   [16 bits]  — client-side tick counter (không phải server tick)
```

### 3.2 LOGIN (8 bytes, 1 lần)

```cpp
// PacketBuilder.cpp:27
h.size   = 8;
h.opcode = C2S_LOGIN;
h.matchId = matchId;
h.seq    = seq;
```

Login packet không có payload sau header. Server dùng packet đầu tiên từ một địa chỉ IP:port mới để **tạo session** cho player đó trong `Match::resolvePlayer()`.

### 3.3 MOVE (12 bytes, mỗi tick)

```cpp
// PacketBuilder.cpp:49 — thêm PacketMovement sau header
mv.dirX  = (int8_t)(rand()%3 - 1) + 1;  // -1→0, 0→1, +1→2
mv.dirZ  = (int8_t)(rand()%3 - 1) + 1;
mv.speed = 128;
```

Mỗi tick gửi 1 MOVE với hướng di chuyển ngẫu nhiên — mô phỏng player đang di chuyển khắp map.

### 3.4 SHOOT (8 bytes, xác suất `shootChance`)

```cpp
// VirtualPlayer.cpp:60
if ((float)rand()/RAND_MAX < shootChance)
    buildShoot(...);
```

Default `shootChance=0.05` → trung bình 1 shot mỗi 20 ticks (1 giây). Tăng lên `1.0` để stress test bullet physics.

### 3.5 Throughput tính toán

| Config | MOVE pps | SHOOT pps | Total pps | Bandwidth |
|--------|:--------:|:---------:|:---------:|:---------:|
| 1 client, 20 Hz, shoot=0.05 | 20 | 1 | ~21 | ~252 B/s |
| 100 clients, 20 Hz, shoot=0.05 | 2,000 | 100 | ~2,100 | ~25 KB/s |
| 500 clients, 20 Hz, shoot=0.05 | 10,000 | 500 | ~10,500 | ~126 KB/s |
| 128 clients, 22 Hz, shoot=0.1 | 2,816 | 282 | ~3,100 | ~37 KB/s |

---

## 4. Flow end-to-end khi stress test

```
Bước 1: Tạo match qua Kafka
────────────────────────────
  (Java matchmaking publish) → Kafka topic "match.create"
  → KafkaConsumer::poll() trong server_tank.exe
  → MatchManager::createMatch({matchId=1, playerIds=[...], mapName="world"})
  → _matches.emplace(1, std::make_unique<Match>(...))

Bước 2: load_client.exe khởi động
───────────────────────────────────
  load_client.exe --match 1 --clients 100 --rate 20 --duration 60

  Mỗi VirtualPlayer gửi LOGIN(matchId=1) → server nhận
  → IOCP worker nhận packet → routeCommand(matchId=1)
  → Match::pushCommand(cmd) → _cmdQueue

  Lần đầu packet từ IP:port mới:
  → Match::resolvePlayer(addr) → _nextSlot++ → _sessions.addSession()
  → _world.addPlayer(pid, spawnPos)  ← tank spawn lúc này

Bước 3: tick loop (server) @ 60 Hz
────────────────────────────────────
  tickDispatcher():
    active = [match_1]
    future = pool.submit([match_1, dt] { match_1->tick(dt); })
    f.get()  ← chờ xong

  Match::tick(dt):
    1. swap _cmdQueue → local (tất cả MOVE/SHOOT từ load_client)
    2. dispatch → handleMove() → world.processInput(pid, {dx, dz})
                → handleShoot() → world.processInput(pid, {shoot=true})
    3. world.update(dt):
       - tanks di chuyển theo input
       - bullets bay theo physics
       - CollisionDetection (UniformGrid)
       - damage calculation
    4. broadcastSnapshot() → S2C_SNAPSHOT gửi về từng player

Bước 4: Metrics được ghi [Perf] mỗi 600 ticks (~10s)
──────────────────────────────────────────────────────
  [Perf] ticks=600 matches=1 pool_pending=0 |
         tick avg=228µs p95=341µs p99=540µs min=103µs max=21412µs |
         overruns=1 (0.2%)

  Python agent đọc log → expose /metrics/prometheus
  Prometheus scrape → Grafana render
```

---

## 5. Cách chạy stress test

### 5.1 Bước 1 — Đảm bảo server đang chạy và có match

```bash
# Kiểm tra server process
ps aux | grep server_tank | grep -v grep

# Tạo match qua Kafka (nếu chưa có)
docker exec kafka kafka-console-producer \
  --bootstrap-server localhost:9092 \
  --topic match.create << 'EOF'
{"matchId":1,"mapName":"world","maxDuration":300,"players":[1,2]}
EOF

# Xác nhận match được tạo
tail -20 /mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release/server_tank.log \
  | grep "match 1"
```

### 5.2 Bước 2 — Build load_client (nếu chưa có)

```powershell
# Trong Developer Command Prompt (Windows)
cd D:\Unity\TankOnline\Tank
cmake -S . -B out\build\x64-Release -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build out\build\x64-Release --target load_client --config Release
# Output: out\build\x64-Release\load_client\Release\load_client.exe
```

```python
# Từ WSL2 (tránh exit-code 144)
import subprocess
msbuild = '/mnt/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe'
subprocess.run([msbuild,
    r'D:\Unity\TankOnline\Tank\load_client\load_client.sln',
    '/p:Configuration=Release', '/p:Platform=x64', '/m', '/v:m'], check=True)
```

### 5.3 Bước 3 — Chạy các scenarios

```powershell
$exe = "D:\Unity\TankOnline\Tank\out\build\x64-Release\load_client\Release\load_client.exe"

# --- Scenario 1: Baseline (kiểm tra không có error) ---
& $exe --host 127.0.0.1 --port 8080 --match 1 --clients 2 --rate 20 --duration 30

# --- Scenario 2: Medium load ---
& $exe --host 127.0.0.1 --port 8080 --match 1 --clients 50 --rate 20 --duration 60

# --- Scenario 3: High load (benchmark chính) ---
& $exe --host 127.0.0.1 --port 8080 --match 1 --clients 128 --rate 22 --duration 60

# --- Scenario 4: Shoot storm (stress bullet physics) ---
& $exe --host 127.0.0.1 --port 8080 --match 1 --clients 50 --rate 20 --shoot 1.0 --duration 30

# --- Scenario 5: Burst (nhiều player, ít rate) ---
& $exe --host 127.0.0.1 --port 8080 --match 1 --clients 500 --rate 5 --duration 30
```

### 5.4 Bước 4 — Xem kết quả trên Grafana

Mở Dashboard 1 — C++ Game Server: **http://localhost:3000/d/tank-cpp-server**  
Đặt time range → **Last 5 minutes**, auto-refresh 10s.

Theo dõi trong lúc test chạy:

| Panel | Quan sát khi load tăng |
|-------|------------------------|
| **The Budget Line** | Tick avg/p99 có tăng không? Vẫn xa vạch đỏ? |
| **Stability Score** | Overruns vẫn = 0? |
| **Budget Headroom** | Còn > 90%? |
| **Load vs Latency** | P99 flat khi matches tăng? |

---

## 6. Metrics đọc từ load_client output

```
╔══════════════════════════════════════════════════╗
║          IOCP LOAD TEST CLIENT                   ║
╚══════════════════════════════════════════════════╝
  Target   : 127.0.0.1:8080
  Clients  : 128  (virtual players)
  Threads  : 16   (worker threads)
  Tick rate: 22   pkt/s/client
  Duration : 60   seconds
  Shoot p  : 5%

  Time   Sent (total / pps)             Recv (total / rps)   BW out
  ────────────────────────────────────────────────────────────────────
[  1s]  sent=   2816 ( 2816 pps)  recv=      0 (   0 rps)  err=   0  bw=    34 KB/s
[  2s]  sent=   5632 ( 2816 pps)  recv=      0 (   0 rps)  err=   0  bw=    34 KB/s
...
[ 60s]  sent= 168960 ( 2816 pps)  recv=      0 (   0 rps)  err=   0  bw=    34 KB/s

══════════════════════════════════════════
  LOAD TEST SUMMARY  (60 seconds)
══════════════════════════════════════════
  Packets sent     : 168,960  (avg 2,816 pps)
  Packets received : 0        (server broadcast chưa wire về client socket)
  Send/recv errors : 0
  Bytes out        : 1,980 KB
  Loss rate        : 0.00%
══════════════════════════════════════════
```

**Giải thích các trường:**

| Trường | Ý nghĩa |
|--------|---------|
| `pps` | Packets per second gửi đi — đây là tải thực sự server phải xử lý |
| `recv=0` | Server broadcast snapshot về địa chỉ trong `SessionManager`, không phải source port của load_client socket — snapshot vẫn gửi nhưng load_client không nhận |
| `err=0` | OS socket buffer không bị tràn — load đang trong ngưỡng an toàn |
| `bw KB/s` | Bandwidth ra — 128×22 pkt/s × 12 bytes/pkt ÷ 1024 ≈ 34 KB/s |

---

## 7. Đối chiếu với [Perf] log của server

Trong lúc load_client chạy, server ghi log mỗi 10 giây:

```
# Baseline (2 players, 20Hz):
[Perf] ticks=600 matches=1 pool_pending=0 |
       tick avg=228µs p95=341µs p99=540µs min=103µs max=21412µs |
       overruns=1 (0.2%)

# High load (128 clients, 22Hz = 2,816 pps):
[Perf] ticks=600 matches=1 pool_pending=0 |
       tick avg=437µs p95=680µs p99=1364µs min=103µs max=32120µs |
       overruns=0 (0.0%)
```

**Tick avg tăng từ 228 → 437µs** khi load tăng từ 40 → 2,816 pps, nhưng vẫn chỉ chiếm **2.62% budget** (16,667µs) — headroom 97.4%.

---

## 8. Kịch bản Test cho Buổi Bảo vệ

| # | Scenario | Lệnh | Mục tiêu chứng minh |
|---|---------|------|---------------------|
| 1 | **Baseline** | `--clients 2 --rate 20 --duration 30` | Server chạy ổn định, overrun=0 |
| 2 | **High load** | `--clients 128 --rate 22 --duration 60` | Tick avg tăng nhẹ nhưng không vượt budget |
| 3 | **Shoot storm** | `--clients 50 --shoot 1.0 --duration 30` | Bullet physics không làm tick explode |
| 4 | **Ramp up** | Chạy 1→2→3 liên tiếp | Grafana Budget Line vẫn flat trước vạch đỏ |

Chạy theo thứ tự 1→2→3→4 trong buổi demo, Grafana sẽ hiển thị tick avg tăng dần nhưng **không bao giờ chạm vạch đỏ 16.7ms** — đây là bằng chứng trực quan nhất cho optimization của IOCP + ThreadPool architecture.

---

## 9. Giới hạn hiện tại

| Vấn đề | Nguyên nhân | Ảnh hưởng |
|--------|------------|-----------|
| `recv=0` trong load_client | Server gửi snapshot về địa chỉ trong SessionManager, không phải source port load_client | Không đo được RTT, nhưng không ảnh hưởng stress test |
| Max ~2,000 clients/machine | OS giới hạn file descriptors (~16K ephemeral ports) | Dùng `--clients 500` thay vì 2000 để tránh port exhaustion |
| Tất cả clients cùng matchId | `--match` chỉ nhận 1 giá trị | Để stress multi-match: chạy nhiều load_client.exe song song với `--match` khác nhau |
