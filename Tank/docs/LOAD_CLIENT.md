# Load Client – IOCP Stress Test

> Đo khả năng chịu tải của server IOCP bằng cách mô phỏng N virtual players gửi UDP packets.
> Tham chiếu: `load_client/`

---

## Mục tiêu đo lường

| Metric | Ý nghĩa |
|--------|---------|
| **pps (packets/sec)** | Tổng throughput gửi vào server |
| **rps (recv/sec)** | Server có gửi ngược lại không (khi send() được wire) |
| **error rate** | Tỷ lệ gói bị lỗi do OS buffer full / port exhaustion |
| **RTT latency** | Round-trip time (khi server echo) theo histogram |
| **bandwidth out** | KB/s tổng, dùng kiểm tra OS socket buffer limit |

---

## Kiến trúc

```
main()
 ├─ parseArgs() → Config
 ├─ WSAStartup()
 ├─ spawn N WorkerThread (default: hardware_concurrency)
 │   └─ mỗi thread quản lý  (clients / threads)  VirtualPlayer
 │       └─ mỗi VirtualPlayer:
 │           ├─ 1 UDP socket riêng (bind port=0, OS chọn source port)
 │           ├─ sendLogin() – 1 lần
 │           └─ tick():
 │               ├─ buildMove() → sendTo(server)
 │               ├─ buildShoot() nếu rand < shootChance
 │               └─ recvFrom() non-blocking → đo RTT
 │
 └─ stats loop: in 1 dòng mỗi giây → printSummary() khi hết duration
```

---

## Packet gửi đi

| Packet | Opcode | Bytes | Tần suất |
|--------|--------|-------|---------|
| LOGIN  | 1000   | 8     | 1 lần khi khởi động player |
| MOVE   | 1001   | 12    | Mỗi tick (mặc định 20 Hz) |
| SHOOT  | 1002   | 8     | Xác suất 5% mỗi tick |

**Ví dụ 100 clients @ 20Hz:**
- MOVE: 100 × 20 = **2,000 pps**
- SHOOT (5%): 100 × 20 × 0.05 = **100 pps**
- Total: ~2,100 pps, ~25 KB/s

---

## Build

```bash
# Thêm vào root CMakeLists.txt (đã thêm):
#   add_subdirectory(load_client)

# Configure (nếu lần đầu)
cmake -S d:\SOURCE_C++\Tank -B d:\SOURCE_C++\Tank\out\build\x64-Debug -G Ninja

# Build (trong Developer Command Prompt / sau vcvars64.bat)
cmake --build out/build/x64-Debug --target load_client
# Output: out/build/x64-Debug/load_client/load_client.exe
```

---

## Run

```bash
cd out/build/x64-Debug/load_client

# Mặc định: 100 clients, 30s, 20 pkt/s/client
.\load_client.exe

# Tùy chỉnh
.\load_client.exe --host 127.0.0.1 --port 8080 --clients 500 --duration 60 --rate 30

# Các option
--host     <ip>    IP của server (default: 127.0.0.1)
--port     <n>     Port         (default: 8080)
--clients  <n>     Số virtual players (default: 100)
--threads  <n>     Worker threads    (default: cpu_count)
--duration <n>     Giây chạy test    (default: 30)
--rate     <n>     Ticks/sec/client  (default: 20)
--shoot    <0..1>  Xác suất bắn/tick (default: 0.05)
--verbose         In log per-player
```

---

## Output mẫu

```
╔══════════════════════════════════════════════════╗
║          IOCP LOAD TEST CLIENT                   ║
╚══════════════════════════════════════════════════╝
  Target   : 127.0.0.1:8080
  Clients  : 200  (virtual players)
  Threads  : 16   (worker threads)
  Tick rate: 20   pkt/s/client
  Duration : 30   seconds
  Shoot p  : 5%

  Workers started. Running for 30 seconds...

  Time   Sent (total / pps)             Recv (total / rps)   BW out
  ────────────────────────────────────────────────────────────────────
[  1s]  sent=   4200 ( 4200 pps)  recv=      0 (   0 rps)  err=   0  bw=    50 KB/s
[  2s]  sent=   8400 ( 4200 pps)  recv=      0 (   0 rps)  err=   0  bw=    50 KB/s
...
[ 30s]  sent= 126000 ( 4200 pps)  recv=      0 (   0 rps)  err=   0  bw=    50 KB/s

══════════════════════════════════════════
  LOAD TEST SUMMARY  (30 seconds)
══════════════════════════════════════════
  Packets sent     : 126000  (avg 4200 pps)
  Packets received : 0       (avg 0 pps)
  Send/recv errors : 0
  Bytes out        : 1512 KB
  Bytes in         : 0 KB
  Loss rate        : 0.00%
══════════════════════════════════════════
```

---

## Gợi ý test scenarios

| Scenario | Params | Mục tiêu |
|----------|--------|---------|
| **Baseline** | 100 clients, 20Hz, 30s | Verify không có error |
| **Medium load** | 500 clients, 20Hz, 60s | Server CPU / IOCP saturation |
| **High load** | 1000 clients, 30Hz, 120s | OS socket buffer limit |
| **Shoot storm** | 200 clients, 20Hz, shoot=1.0 | Bullet spawn/destroy performance |
| **Burst** | 2000 clients, 5Hz, 30s | Nhiều player, ít rate |

---

## Giới hạn hiện tại

| Vấn đề | Lý do | Fix tương lai |
|--------|-------|--------------|
| recv = 0 | Server chưa wire `send()` | Implement `NetworkManager::send()` + broadcast |
| RTT = N/A | Cần server echo seq | S2C packet cần echo seq field |
| Max ~2000 clients/machine | OS giới hạn socket descriptors (~16K) | Dùng shared socket per thread thay vì 1 socket/player |
| No reconnect | Player socket đóng khi WorkerThread stop | Không ảnh hưởng stress test |

---

## Files quan trọng

| File | Vai trò |
|------|---------|
| [load_client/src/main.cpp](../load_client/src/main.cpp) | Entry point, CLI, stats loop |
| [load_client/src/WorkerThread.cpp](../load_client/src/WorkerThread.cpp) | Fixed-rate tick loop per thread |
| [load_client/src/VirtualPlayer.cpp](../load_client/src/VirtualPlayer.cpp) | Simulate 1 player (login+move+shoot+recv) |
| [load_client/src/PacketBuilder.cpp](../load_client/src/PacketBuilder.cpp) | Build bit-packed C2S packets |
| [load_client/src/Metrics.cpp](../load_client/src/Metrics.cpp) | Atomic counters + histogram |
| [load_client/src/UdpSocket.cpp](../load_client/src/UdpSocket.cpp) | RAII non-blocking UDP socket |
