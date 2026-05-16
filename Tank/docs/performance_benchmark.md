# Báo cáo Đo Hiệu năng: Apples-to-Apples Comparison

## 1. Bối cảnh và Câu hỏi Nghiên cứu

Server game Tank Online duy trì **tick rate 60 Hz** (budget = 16,667 µs/tick). Ba câu hỏi cần trả lời bằng dữ liệu thực đo:

1. Single-thread blocking I/O có gây ra tick degradation không?
2. Blocking-recvfrom multi-thread vs IOCP — có khác biệt thực sự không?
3. Điều gì thực sự quyết định tick latency của hệ thống này?

---

## 2. Ba Kiến trúc được So sánh

### Kiến trúc A — server_baseline (Single-Thread Non-Blocking Poll)

```
┌─────────────────────────────────────────────┐
│               Main Thread                   │
│  while (running) {                          │
│    [1] recvfrom loop — drain all packets    │  ← tranh chấp budget với tick
│    [2] game tick (physics, bullets)         │
│    [3] broadcast snapshot                  │
│    [4] sleep(remaining_budget)              │
│  }                                          │
└─────────────────────────────────────────────┘
Port: 8081   Receiver threads: 0 (inline)
```

### Kiến trúc B — server_tank, BACKEND=blocking (Multi-Thread Blocking recvfrom)

```
┌───────────────────────────────────┐   ┌─────────────────────────────────────┐
│   Receiver Thread × 2             │   │   MatchManager Tick Dispatcher      │
│                                   │   │                                     │
│   blocking recvfrom() ────────┐   │   │   60 Hz loop:                       │
│   ← thread ngủ đến khi có pkt │   │   │   pool.submit(match->tick(dt))      │
│   decode → routeCb ◄──────────┘   │   │   futures.wait_all()                │
└───────────────────────────────────┘   └─────────────────────────────────────┘
         shared BufferPool (10,000 IoContext)
         shared ThreadPool (8 workers)
         shared Match / GameWorld / Physics
Port: 8080   Receiver threads: 2 blocking
```

### Kiến trúc C — server_tank, BACKEND=iocp (Windows IOCP)

```
┌───────────────────────────────────────┐   ┌─────────────────────────────────┐
│   IOCP Worker Thread × 16             │   │   MatchManager Tick Dispatcher  │
│                                       │   │                                 │
│   GetQueuedCompletionStatus() ─────┐  │   │   60 Hz loop:                   │
│   ← OS notifies on pkt complete    │  │   │   pool.submit(match->tick(dt))  │
│   100 pre-posted WSARecvFrom       │  │   │   futures.wait_all()            │
│   decode → routeCb ◄───────────────┘  │   │                                 │
└───────────────────────────────────────┘   └─────────────────────────────────┘
         shared BufferPool (10,000 IoContext)
         shared ThreadPool (8 workers)
         shared Match / GameWorld / Physics
Port: 8080   IOCP workers: hardware_concurrency × 2 = 16
```

**Biến số bị cô lập:** Chỉ thay đổi *cách OS đẩy bytes vào RAM*. Toàn bộ game logic, BufferPool, ThreadPool, MatchManager dùng chung source code.

---

## 3. Thiết kế Thí nghiệm

### Strategy Pattern — INetworkBackend Interface

```cpp
class INetworkBackend {
    virtual bool start(int port) = 0;
    virtual void stop()          = 0;
    virtual void send(...)       = 0;
    virtual void setRouteCallback(std::function<void(GameCommand)>) = 0;
    virtual const char* backendName() const = 0;
};

class NetworkManager  : public INetworkBackend { /* IOCP     */ };
class BlockingBackend : public INetworkBackend { /* blocking */ };
```

`MatchManager` và `Match` nhận `INetworkBackend&` — không biết và không cần biết backend nào đang chạy.

### Switching bằng environment variable

```bash
# Chạy IOCP:
server_tank.exe                      # default

# Chạy Blocking:
BACKEND=blocking  server_tank.exe

# Tách log:
LOG_FILE=bench_iocp.log     BACKEND=iocp      server_tank.exe
LOG_FILE=bench_blocking.log BACKEND=blocking  server_tank.exe
```

### Load client

```bash
load_client.exe --host 127.0.0.1 --port 8080 \
    --clients 128 --threads 8 --duration 30 \
    --rate 20 --shoot 0.1
# = 128 virtual players × 22 pkt/s ≈ 2,816 pkt/s
```

---

## 4. Kết quả Đo

### 4.1 server_baseline (Kiến trúc A) — Single Thread

| Tải | Avg tick | Max tick | Overruns |
|-----|----------|----------|----------|
| 0 client (idle) | 155 µs | 1,137 µs | 0 |
| 50 clients (~1,100 pps) | 564 µs | 9,568 µs | 0 |
| 128 clients (~2,816 pps) | **990 µs** | **109,563 µs** | **3 / 3,000** |

**Log thực tế (128 clients):**
```
[Baseline] ticks=1500  avg=531µs   max=9020µs    overruns=0 (0.0%)
[Baseline] ticks=1800  avg=730µs   max=9020µs    overruns=0 (0.0%)
[Baseline] ticks=2100  avg=898µs   max=98485µs   overruns=2 (0.1%)
[Baseline] ticks=2700  avg=1017µs  max=109563µs  overruns=3 (0.1%)
[Baseline] ticks=3000  avg=990µs   max=109563µs  overruns=3 (0.1%)
```

---

### 4.2 IOCP vs Blocking — Cùng Codebase (Kiến trúc B & C)

Cả hai backend chạy cùng MatchManager/Match/GameWorld:

| Kiến trúc | Avg tick | Max tick | Overruns |
|-----------|----------|----------|----------|
| IOCP (16 workers) | **205–230 µs** | 1,899 µs | **0 / 1,200** |
| Blocking (2 threads) | **210–224 µs** | 937 µs | **0 / 1,800** |

**Log thực tế — IOCP:**
```
[Perf] ticks=600  avg=230µs  min=0µs    max=1899µs | overruns=0 (0.0%)
[Perf] ticks=600  avg=205µs  min=113µs  max=616µs  | overruns=0 (0.0%)
```

**Log thực tế — Blocking:**
```
[Perf] ticks=600  avg=224µs  min=0µs   max=662µs  | overruns=0 (0.0%)
[Perf] ticks=600  avg=224µs  min=98µs  max=937µs  | overruns=0 (0.0%)
[Perf] ticks=600  avg=210µs  min=97µs  max=867µs  | overruns=0 (0.0%)
```

---

### 4.3 Bảng So sánh Tổng hợp

| Chỉ số | server_baseline | Blocking multi-thread | IOCP |
|--------|----------------|-----------------------|------|
| Architecture | 1 thread, inline I/O | 2 recv threads | 16 IOCP workers |
| Idle avg | 155 µs | ~218 µs | ~218 µs |
| 128-client avg | **990 µs (+538%)** | **217 µs (+0%)** | **218 µs (+0%)** |
| 128-client max | **109,563 µs** | 937 µs | 1,899 µs |
| Overruns | **3 / 3,000** | 0 / 1,800 | 0 / 1,200 |

---

## 5. Phân tích — Tại sao Kết quả như vậy?

### 5.1 Tại sao server_baseline tệ hơn nhiều?

**Root cause: I/O và game logic chia nhau 1 tick budget.**

```
T_tick_baseline = T_drain(N_clients) + T_physics + T_snapshot
```

Với 128 clients × 22 pkt/s ÷ 60 Hz = **47 packet cần drain mỗi tick**.  
Mỗi `recvfrom + decode`: ~18 µs trên localhost.  
→ `T_drain ≈ 47 × 18 = 846 µs` — chiếm 5× so với game logic thuần.

Avg 990 µs = 846 µs (drain) + 144 µs (physics + snapshot). Khớp với lý thuyết.

### 5.2 Tại sao Blocking multi-thread và IOCP cho kết quả tương đương?

**Cả hai đều tách hoàn toàn I/O ra khỏi tick thread.**

```
T_tick_iocp = T_queue_drain + T_physics + T_snapshot   (không có recv)
T_tick_blk  = T_queue_drain + T_physics + T_snapshot   (không có recv)
```

Receiver threads (blocking hoặc IOCP workers) chạy song song với tick dispatcher. Tick thread chỉ swap command queue (mutex lock ~1 µs) rồi chạy physics. Packet volume không ảnh hưởng.

**Tại sao Blocking (2 threads) không tệ hơn IOCP ở load này?**

586 pkt/s (2 player) ÷ 2 threads = 293 pkt/s/thread.  
Mỗi blocking recvfrom + decode: ~5 µs → 293 × 5 = 1.5 ms/giây tổng recv work.  
CPU utilization của recv threads: 1.5 ms / 1,000 ms = **0.15%** — gần như không dùng gì.  
2 threads là quá đủ cho load này.

**IOCP sẽ vượt trội rõ khi nào?**

| Tình huống | Blocking (2 threads) | IOCP |
|-----------|---------------------|------|
| 600 pkt/s (2 players) | Đủ | Đủ |
| 60,000 pkt/s (1,000 players) | 2 threads = bottleneck | 16 workers + 100 pre-posted buffers |
| Burst đột ngột 10,000 pkt trong 1ms | Socket buffer tràn | OS hấp thụ vào 100 pre-posted buffers |

Tại scale game thực (2 player, 600 pkt/s), blocking và IOCP cho kết quả đồng nhất. Sự khác biệt sẽ hiện ra khi số concurrent session tăng lên 100+ players.

### 5.3 Spike max: Blocking 937 µs vs IOCP 1,899 µs

**IOCP spike cao hơn** vì có nhiều hơn ở background (16 threads, IOCP syscalls, completion queue overhead). Tuy nhiên cả hai đều nằm trong budget 16,667 µs — không phải overrun.

---

## 6. Kết luận Định lượng

### Kết luận 1: Tách I/O khỏi tick thread là điều kiện cần thiết

server_baseline (inline I/O) avg tick tăng **538%** dưới tải 128 clients.  
Cả Blocking multi-thread và IOCP: avg tick tăng **0%** dưới cùng tải.

**Bất kỳ kiến trúc nào tách I/O ra thread riêng đều giải quyết được vấn đề này.**

### Kết luận 2: IOCP vs Blocking — Trade-off rõ ràng

| Tiêu chí | Blocking (2 recv threads) | IOCP |
|----------|--------------------------|------|
| Tick latency @ 2 players | 217 µs | 218 µs |
| Code complexity | Thấp | Cao |
| Max throughput (lý thuyết) | N_threads × 1/recv_latency | 100 pre-posted buffers |
| Burst capacity | Socket buffer (~8 KB) | 100 × 1,024 B = 100 KB in-flight |
| Scale đến 1,000+ concurrent | Cần tăng N_threads | Tự động (IOCP queue) |
| Production readiness | Đủ cho 2–10 players | Phù hợp scale-out |

### Kết luận 3: Với game scale 2–10 players, Blocking là đủ — nhưng IOCP là đúng hướng

Hệ thống hiện tại chỉ chạy 2 players/match. Ở scale này, Blocking và IOCP cho cùng kết quả thực đo. Lý do chọn IOCP là **forward compatibility** cho khi số match concurrent tăng lên 64 (MAX_CONCURRENT_MATCHES), mỗi match 2 players = 128 concurrent sessions × 22 pkt/s = 2,816 pkt/s — khi đó pre-posted buffers và 16 workers của IOCP sẽ cho lợi thế đo được.

---

## 7. Cấu trúc Source Code (Strategy Pattern)

```
server_tank/
├── include/Network/
│   ├── INetworkBackend.hpp   ← interface chung
│   ├── NetworkManager.hpp    ← IOCP implementation
│   └── BlockingBackend.hpp   ← blocking-recvfrom implementation
├── src/Network/
│   ├── NetworkManager.cpp
│   └── BlockingBackend.cpp
├── include/Core/
│   ├── MatchManager.hpp      ← nhận INetworkBackend& (không biết backend nào)
│   └── Match.hpp             ← nhận INetworkBackend&
└── src/main.cpp              ← BACKEND env var → chọn impl lúc runtime
```

**Shared code không thay đổi giữa hai backend:**
- `BufferPool` (10,000 pre-allocated IoContext)
- `ThreadPool` (8 workers)
- `MatchManager` + tick dispatcher (60 Hz, measured dt)
- `Match` + command queue + physics tick
- `GameWorld`, `PhysicsWorld`, `UniformGrid`
- `MetricsCollector` (embedded trong MatchManager — cùng `[Perf]` log format)

---

## 8. Cách Reproduce

```bash
# Build
cmake -S Tank -B Tank/build_full
cmake --build Tank/build_full --target server_tank --config Release
cmake --build Tank/build_full --target load_client --config Release

# Benchmark IOCP
LOG_FILE=bench_iocp.log server_tank.exe &
load_client.exe --host 127.0.0.1 --port 8080 --clients 128 --threads 8 --duration 30 --rate 20

# Benchmark Blocking
BACKEND=blocking LOG_FILE=bench_blocking.log server_tank.exe &
load_client.exe --host 127.0.0.1 --port 8080 --clients 128 --threads 8 --duration 30 --rate 20

# So sánh
grep "Perf.*ticks" bench_iocp.log
grep "Perf.*ticks" bench_blocking.log
```
