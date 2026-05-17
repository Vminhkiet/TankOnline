# Tick Dispatch — Cơ chế Chia Tick cho Matches

## 1. Tổng quan

Server duy trì **một game loop duy nhất** chạy ở **60 Hz** trên một thread riêng (`_tickThread`). Mỗi lần loop, nó chia công việc tick cho tất cả các match đang hoạt động thông qua **ThreadPool** và chờ tất cả hoàn thành trước khi sang frame tiếp theo.

```
_tickThread (1 thread, 60 Hz)
     │
     ├─ snapshot active matches
     ├─ submit match[0].tick(dt) ──► worker[0]  ┐
     ├─ submit match[1].tick(dt) ──► worker[1]  │ chạy song song
     ├─ submit match[2].tick(dt) ──► worker[2]  │
     ├─ submit match[N].tick(dt) ──► worker[?]  ┘
     ├─ f.get() × N  ◄── barrier: chờ tất cả xong
     └─ sleep_for(budget − elapsed)
```

---

## 2. Vòng lặp Tick Dispatcher

File: `src/Core/MatchManager.cpp` — hàm `tickDispatcher()`

```
┌──────────────────────────────────────────────────────┐
│  Bước 1: Đo dt thực tế                               │
│  Bước 2: Snapshot danh sách match đang chạy          │
│  Bước 3: Submit mỗi match lên ThreadPool             │
│  Bước 4: Barrier — chờ tất cả futures xong           │
│  Bước 5: Dọn dẹp match đã kết thúc                   │
│  Bước 6: Ghi stats (mỗi 600 ticks = 10 giây)         │
│  Bước 7: Sleep phần budget còn lại                   │
└──────────────────────────────────────────────────────┘
```

### Bước 1 — Đo `dt` thực tế

```cpp
float dt = duration<float>(tickStart - _lastTickStart).count();
dt = clamp(dt, 0.001f, 0.05f);   // sàn 1ms, trần 50ms
```

**Tại sao đo thay vì dùng `dt = 1/60f` cố định?**

`std::this_thread::sleep_for` trên Windows có độ phân giải ~15 ms. Dùng dt cố định khiến vật lý chạy nhanh hơn ý muốn khoảng 6.7% so với Unity `FixedUpdate`. Đo wall-clock thực tế triệt tiêu drift này — mỗi tick tự hiệu chỉnh theo thời gian thực trôi qua.

Giới hạn `[1ms, 50ms]` ngăn hai trường hợp biên:
- **< 1ms**: chia cho dt quá nhỏ → vận tốc/lực vật lý bùng nổ số học
- **> 50ms**: server bị treo lâu (debug, GC, OS preemption) → vật lý nhảy vọt bất hợp lý

### Bước 2 — Snapshot danh sách match

```cpp
std::shared_lock lock(_matchesMutex);
for (auto& [id, m] : _matches)
    if (m->isRunning()) active.push_back(m.get());
```

Dùng `shared_mutex` (reader-writer lock):
- `_tickThread` và các IOCP worker gọi `routeCommand()` đều chỉ cần **shared_lock** → không block nhau
- Chỉ `createMatch()` (Kafka consumer thread) dùng **unique_lock**

Snapshot vào vector cục bộ (`active`) để giải phóng lock ngay, tránh giữ lock trong suốt quá trình tick.

### Bước 3 — Fan-out lên ThreadPool

```cpp
vector<future<void>> futures;
for (Match* m : active)
    futures.push_back(_pool.submit([m, dt] { m->tick(dt); }));
```

Mỗi match được gói vào một **task** và đẩy vào hàng đợi của ThreadPool. Worker nào rảnh sẽ lấy task. Nếu số match ≤ số worker → tất cả chạy hoàn toàn song song (1 wave). Nếu match > worker → chia thành nhiều wave tuần tự.

Tất cả match trong cùng frame dùng **cùng một giá trị `dt`** → đảm bảo đồng bộ thời gian vật lý giữa các match.

### Bước 4 — Barrier đồng bộ

```cpp
for (auto& f : futures) f.get();
```

`_tickThread` **block** tại đây cho đến khi tất cả match trong frame này hoàn tất. Đây là barrier tường minh — không có frame nào bắt đầu khi frame trước chưa xong. Cách này đơn giản hơn lock-free ring buffer và phù hợp với yêu cầu determinism của game loop.

### Bước 5 — Dọn dẹp match kết thúc

```cpp
unique_lock lock(_matchesMutex);
for (auto it = _matches.begin(); it != _matches.end(); )
    it = it->second->isRunning() ? next(it) : _matches.erase(it);
```

Dùng `unique_lock` vì erase thay đổi map. Match bị xóa khỏi map ngay trong tick — không để lại "zombie" match tiêu tốn tài nguyên.

### Bước 6 — Rolling stats (mỗi 600 ticks)

```cpp
// Mỗi tick: ghi vào _statSamples
_statSamples.push_back(elUs);

// Mỗi 600 ticks (~10 giây): sort và lấy percentile
sort(_statSamples.begin(), _statSamples.end());
int64_t p95 = _statSamples[size * 95 / 100];
int64_t p99 = _statSamples[size * 99 / 100];

LOG_INFO("[Perf] ticks={} matches={} pool_pending={} | "
         "tick avg={:.0f}µs p95={}µs p99={}µs min={}µs max={}µs | overruns={} ({:.1f}%)",
         ...);
```

Log này là nguồn dữ liệu cho Python metrics agent → Prometheus → Grafana.

### Bước 7 — Sleep budget còn lại

```cpp
auto sleepFor = tickBudget - (Clock::now() - tickStart);
if (sleepFor > 0) sleep_for(sleepFor);
```

Budget = 1,000,000 µs ÷ 60 = **16,667 µs/tick**. Nếu tick mất 437 µs → sleep 16,230 µs. Nếu tick vượt budget (`elUs > 16,667`) → ghi nhận **overrun**, không sleep.

---

## 3. Bên trong `Match::tick(dt)`

File: `src/Core/Match.cpp`

```
tick(dt)
  │
  ├─ 1. Drain command queue  ──────────────────────── swap deque (1 lock)
  │       ↑ IOCP workers push vào _cmdQueue (lock)
  │
  ├─ 2. Dispatch commands  ─────────────────────────── move / shoot handlers
  │
  ├─ 3. Physics update  ────────────────────────────── world.update(dt)
  │       tanks di chuyển, đạn bay, va chạm, damage
  │
  ├─ 4. Broadcast snapshot  ────────────────────────── mỗi tick (60 Hz)
  │       1 packet per player (có localPlayerId riêng)
  │
  ├─ 5. Timeout detection  ─────────────────────────── 5s không packet → kick
  │
  └─ 6. Win condition check  ───────────────────────── kill / draw / timeout
```

### Command Queue — lock tối thiểu

```cpp
// IOCP worker thread (bất kỳ lúc nào):
void Match::pushCommand(GameCommand cmd) {
    lock_guard lock(_queueMutex);
    _cmdQueue.push_back(move(cmd));
}

// Tick thread (trong tick()):
deque<GameCommand> local;
{
    lock_guard lock(_queueMutex);
    local.swap(_cmdQueue);   // O(1) — chỉ hoán đổi con trỏ
}
// Xử lý `local` mà không giữ lock
```

`swap` thay vì copy: lock chỉ giữ trong thời gian hoán đổi pointer (~nanosecond), không phải trong suốt quá trình xử lý lệnh. IOCP workers tiếp tục push vào `_cmdQueue` trong khi tick đang xử lý `local`.

### Snapshot — 60 Hz, per-player packet

```cpp
for (uint32_t pid : _config.playerIds) {
    SnapshotHeader hdr;
    hdr.localPlayerId = pid;  // mỗi client biết tank nào là của mình
    // gửi riêng cho từng player qua UDP
    _network.send(addr, pkt.data(), pkt.size());
}
```

Không có broadcast chung — mỗi player nhận packet riêng với `localPlayerId` của mình để client Unity biết phân biệt camera.

### Player join muộn — lazy spawn

```cpp
bool Match::resolvePlayer(const sockaddr_in& addr, uint32_t& outPid) {
    if (_sessions.getPlayerID(addr, outPid)) return true; // fast path

    lock_guard lock(_slotMutex);
    outPid = _config.playerIds[_nextSlot++];
    _sessions.addSession(outPid, addr);
    _world.addPlayer(outPid, spawnPosition); // spawn tank khi packet đầu tiên đến
    return true;
}
```

Tank không được tạo trước khi matchmake — chỉ spawn khi player gửi packet UDP đầu tiên. Tránh tốn tài nguyên cho player chưa kết nối.

---

## 4. ThreadPool

File: `include/Utils/ThreadPool.hpp`

```
ThreadPool(hardware_concurrency)
    ├─ workers[0..N-1]  — mỗi cái chạy workerLoop()
    └─ _tasks: queue<function<void()>>  — FIFO

submit(f):
    packaged_task → push queue → notify_one()
    trả về future<void>

workerLoop():
    wait until !empty
    pop task → unlock → execute
    (kết quả ghi vào promise → f.get() trả về)
```

- **Không có work-stealing**: FIFO thuần túy, đơn giản, đủ dùng cho game loop
- **`_pending` atomic counter**: Python agent đọc `pool_pending` để phát hiện backlog
- **`hardware_concurrency()` workers**: trên máy test = 8 threads, cố định khi khởi động

---

## 5. Mô hình Thời gian — N Matches trên 8 Workers

### Trường hợp N ≤ 8 (1 wave)

```
t=0µs     submit 4 matches vào queue
          worker[0] ← match A
          worker[1] ← match B
          worker[2] ← match C
          worker[3] ← match D
          worker[4..7] idle

t=437µs   tất cả 4 matches xong
          f.get() trả về

t=437µs   sleep_for(16,667 - 437) = 16,230µs

t=16,667µs  tick tiếp theo
```

Thời gian tick ≈ thời gian của match **chậm nhất** trong wave (không phải tổng cộng).

### Trường hợp N > 8 (nhiều waves)

```
Wave 1: match[0..7]  → ~144µs
Wave 2: match[8..15] → ~144µs   (chờ wave 1 xong)
...
T_tick ≈ 144µs × ceil(N / 8)
```

| Concurrent matches | Waves | T_tick dự báo | % Budget |
|------------------:|------:|--------------:|---------:|
| 1 | 1 | ~144 µs | 0.9% |
| 8 | 1 | ~144 µs | 0.9% |
| 16 | 2 | ~288 µs | 1.7% |
| 32 | 4 | ~576 µs | 3.5% |
| 64 (MAX) | 8 | ~1,152 µs | 6.9% |

`MAX_CONCURRENT_MATCHES = 64` được chọn để giữ tick budget < 10% ở worst case.

---

## 6. Isolation giữa các Matches

Mỗi `Match` sở hữu hoàn toàn:

| Resource | Cơ chế isolation |
|----------|-----------------|
| Command queue | `_cmdQueue` + `_queueMutex` riêng |
| Vật lý / game world | `GameWorld` + `PhysicsWorld` riêng |
| Session map | `SessionManager` riêng (IP:port → playerId) |
| Elapsed time | `_elapsed` float riêng |
| Kết quả trận | callback `_onEnd` → `MatchManager::onMatchEnd()` |

Không có shared state giữa các match — worker thread A tick match A và worker B tick match B **không tranh chấp mutex nào với nhau**.

---

## 7. Luồng Dữ liệu Đầy đủ

```
Kafka (match.create)
    │
    ▼
MatchManager::createMatch()          ← Kafka consumer thread
    │  unique_lock(_matchesMutex)
    │  _matches.emplace(id, Match)
    │
    ▼
tickDispatcher()  @60 Hz             ← _tickThread
    │  shared_lock → snapshot active[]
    │  submit match[i].tick(dt) × N
    │
    ├──────────────────────────────────────────────┐
    ▼                                              ▼
Match::tick(dt)   worker[0]          Match::tick(dt)   worker[1]
    │                                     │
    ├─ swap _cmdQueue                     ├─ swap _cmdQueue
    ├─ dispatch (move/shoot)              ├─ dispatch
    ├─ world.update(dt)                   ├─ world.update(dt)
    ├─ broadcastSnapshot()                ├─ broadcastSnapshot()
    └─ checkOutcome() → _onEnd()          └─ checkOutcome()
                │
                ▼
        MatchManager::onMatchEnd()       ← vẫn trong worker thread
                │
                ▼
        KafkaProducer::publish("match.result", json)
```

---

## 8. Vấn đề Đã Biết

### `sTick` static trong `broadcastSnapshot()`

```cpp
// Match.cpp:131
static uint16_t sTick = 0;   // ← KHÔNG có mutex, dùng chung toàn process
const uint16_t tick = sTick++;
```

Khi nhiều match tick đồng thời trên các worker thread khác nhau, `sTick++` là **data race** (undefined behavior theo C++ standard). Trên x86 thường không crash vì `uint16_t` increment de facto atomic, nhưng tiêu chuẩn không đảm bảo.

**Fix**: thay bằng `std::atomic<uint16_t>` static, hoặc đổi thành member `_serverTick` của từng `Match`.

---

## 9. Tóm tắt Thiết kế

| Quyết định | Lý do |
|-----------|-------|
| 1 tick thread duy nhất | Tất cả match giữ cùng frame index, không drift giữa match |
| ThreadPool fan-out | N match chạy song song → tick time = match chậm nhất, không phải tổng |
| Barrier `f.get()` | Đơn giản, deterministic — frame mới chỉ bắt đầu khi frame cũ xong hoàn toàn |
| `dt` đo thực tế | Triệt tiêu drift từ Windows sleep imprecision |
| `swap` command queue | Lock thời gian nanosecond, không block IOCP workers |
| `shared_mutex` | Cho phép đọc song song (tick + route) — chỉ write mới block |
| Lazy player spawn | Không tốn tài nguyên cho slot chưa có người |
