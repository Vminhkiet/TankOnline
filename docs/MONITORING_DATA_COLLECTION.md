# Monitoring Data Collection — Tank Online

> Tài liệu mô tả **từng nguồn dữ liệu, cơ chế thu thập, luồng xử lý, và ánh xạ sang UI**
> cho hệ thống monitoring của Tank Online.

---

## 1. Tổng quan kiến trúc thu thập

```
┌─────────────────────────────────────────────────────────────────────┐
│                        DATA SOURCES                                 │
│                                                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │
│  │ auth-service │  │  matchmaking │  │   history    │  ...         │
│  │    :8081     │  │    :8085     │  │    :8086     │              │
│  │  /actuator   │  │  /actuator   │  │  /actuator   │              │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘              │
│         │                 │                 │                       │
│         └─────────────────┼─────────────────┘  Eureka Discovery    │
│                           │  (pull mỗi 10 s)                       │
│                    ┌──────▼──────────┐                              │
│                    │ monitoring_svc  │ ← Spring Boot Admin          │
│                    │    :8090        │   @EnableAdminServer         │
│                    └──────┬──────────┘                              │
│                           │  GET /api/tank/metrics                  │
│                           ▼                                         │
│                    ┌──────────────────┐                             │
│                    │ tank_metrics_    │ ← Python agent (WSL2)       │
│                    │ agent.py  :9100  │   tail log + PowerShell     │
│                    └──────┬───────────┘                             │
│                           │  tail (file I/O)                       │
│                           ▼                                         │
│              server_tank.log  ←  server_tank.exe (Windows)         │
│              (D:\...\Release\)    stdout redirect                   │
└─────────────────────────────────────────────────────────────────────┘
                           │
                    ┌──────▼──────────┐
                    │ /tank.html      │  Dashboard tổng hợp
                    │ Spring Boot     │  refresh 2 s
                    │ Admin UI (/)    │  per-service drill-down
                    └─────────────────┘
```

Hệ thống có **2 luồng thu thập song song**:

| Luồng | Nguồn | Cơ chế | Nơi hiển thị |
|-------|-------|--------|--------------|
| **A — Java services** | Spring Boot Actuator | Eureka discovery → Spring Boot Admin pull | `http://localhost:8090` (Admin UI) |
| **B — Game server** | C++ stdout `[Perf]` log | Python agent tail → HTTP :9100 → proxy | `http://localhost:8090/tank.html` |

---

## 2. Luồng A — Java Microservices

### 2.1 Nguồn dữ liệu: Spring Boot Actuator

Mỗi Java service expose endpoint tại `/actuator`:

| Endpoint | Dữ liệu | Ví dụ |
|----------|---------|-------|
| `/actuator/health` | UP/DOWN + component health | `{"status":"UP","components":{"db":{"status":"UP"}}}` |
| `/actuator/metrics` | Tên tất cả metric | `{"names":["jvm.memory.used","http.server.requests",...]}` |
| `/actuator/metrics/{name}` | Giá trị cụ thể | `{"measurements":[{"statistic":"VALUE","value":134217728}]}` |
| `/actuator/info` | Build info, git commit | `{"build":{"version":"0.0.1-SNAPSHOT"}}` |
| `/actuator/env` | Config properties | Environment variables, application.yml |
| `/actuator/loggers` | Log level các package | Có thể thay đổi runtime |
| `/actuator/threaddump` | Stack trace tất cả thread | Debug deadlock |
| `/actuator/heapdump` | JVM heap dump | Debug memory leak |

Cấu hình expose trong `application.yml` (tất cả service):
```yaml
management:
  endpoints:
    web:
      exposure:
        include: "*"
  endpoint:
    health:
      show-details: always
```

### 2.2 Cơ chế: Spring Boot Admin + Eureka auto-discovery

```
1. Mỗi service khởi động → đăng ký vào Eureka (:8761)
   POST http://localhost:8761/eureka/apps/{SERVICE-NAME}
   Body: {ipAddr, port, healthCheckUrl, statusPageUrl, ...}

2. monitoring_service (Spring Boot Admin) poll Eureka mỗi 30 s:
   GET http://localhost:8761/eureka/apps
   → nhận danh sách tất cả service instance đang UP

3. Admin Server gọi /actuator/health của từng instance
   → cập nhật trạng thái trong memory

4. Admin UI (browser) poll Admin Server mỗi 10 s
   → hiển thị health, metrics, logs
```

### 2.3 Metrics Java được thu thập tự động

| Category | Metric Key | Mô tả |
|----------|-----------|-------|
| **JVM Memory** | `jvm.memory.used` | Heap + non-heap đang dùng |
| | `jvm.memory.max` | Max heap được cấp |
| | `jvm.gc.pause` | GC pause time (ms) |
| **Thread** | `jvm.threads.live` | Số thread đang live |
| | `jvm.threads.daemon` | Daemon threads |
| | `jvm.threads.peak` | Peak thread count |
| **HTTP** | `http.server.requests` | Count, avg, max per endpoint |
| | `http.server.requests[status=5xx]` | Error rate |
| **Database** | `hikari.connections.active` | DB connection pool usage |
| | `hikari.connections.pending` | Requests chờ connection |
| **System** | `process.cpu.usage` | CPU % của JVM process |
| | `system.cpu.usage` | CPU % toàn hệ thống |

### 2.4 Nơi xem

- **Spring Boot Admin Dashboard**: `http://localhost:8090`
- Chọn service → tab **Metrics** để xem từng metric
- Tab **Health** để xem component health (DB, Redis, Kafka)
- Tab **Logfile** để xem log realtime

---

## 3. Luồng B — Game Server (C++)

### 3.1 Nguồn dữ liệu: `[Perf]` log

`MatchManager.cpp` ghi một dòng **mỗi 600 tick (~10 giây)** ra stdout:

```
[2026-05-16 20:42:22] [Perf] ticks=600 matches=1 pool_pending=0 | tick avg=230µs p95=341µs p99=540µs  min=103µs max=21412µs | overruns=1 (0.2%)
```

**Cấu trúc dòng `[Perf]`:**

| Field | Ví dụ | Ý nghĩa |
|-------|-------|---------|
| `ticks` | `600` | Tổng tick đã chạy kể từ lần flush trước |
| `matches` | `1` | Số active match hiện tại |
| `pool_pending` | `0` | Jobs đang chờ trong ThreadPool queue |
| `tick avg` | `230µs` | Trung bình thời gian mỗi tick (physics + snapshot) |
| `p95` | `341µs` | 95th percentile tick time |
| `p99` | `540µs` | 99th percentile tick time |
| `min` | `103µs` | Tick nhanh nhất |
| `max` | `21412µs` | Tick chậm nhất (spike) |
| `overruns` | `1 (0.2%)` | Số tick vượt budget 16,667 µs |

**Nguồn tính p95/p99 trong C++ (`MatchManager.cpp`):**
```cpp
// Tích lũy samples mỗi tick
_statSamples.push_back(elUs);

// Khi flush (mỗi 600 tick):
std::sort(_statSamples.begin(), _statSamples.end());
int64_t p95 = _statSamples[_statSamples.size() * 95 / 100];
int64_t p99 = _statSamples[_statSamples.size() * 99 / 100];
```

### 3.2 Bước 1: Redirect stdout → log file

`start_server.py` mở file trước khi spawn process:

```python
# /tmp/start_server.py
exe = '/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release/server_tank.exe'
cwd = '/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release'
log = cwd + '/server_tank.log'

with open(log, 'w') as f:                        # truncate on start
    p = subprocess.Popen([exe], cwd=cwd,
                         stdout=f, stderr=f)      # redirect both streams
```

File log được ghi tại:
- **Windows path**: `D:\Unity\TankOnline\Tank\build_full\server_tank\Release\server_tank.log`
- **WSL2 path**: `/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release/server_tank.log`

> **Lý do dùng Python thay vì bash**: Bash khi gọi Windows `.exe` qua WSL2 interop trả về exit code 144 (SIGSTKFLT). Python `subprocess.Popen` không có vấn đề này.

### 3.3 Bước 2: Python agent tail log + parse

`tank_metrics_agent.py` chạy trên WSL2, làm 3 việc song song:

```
Thread 1: tail_log()
  ├─ Mở log file, seek to end (chỉ đọc line mới)
  ├─ Mỗi line có "[Perf]" → parse bằng regex
  ├─ Cập nhật _state dict (thread-safe với Lock)
  └─ Ghi row vào CSV /tmp/tank_metrics_timeseries.csv

Thread 2: check_java_services()
  ├─ Mỗi 10 s: GET /actuator/health của 7 service
  ├─ Collect status (UP/DOWN)
  └─ Cập nhật _state["java_services"]

Thread 3: get_process_memory()
  ├─ Mỗi 30 s: powershell.exe Get-Process server_tank
  ├─ Đọc WorkingSet64 (RSS bytes)
  └─ Cập nhật _state["process_memory_mb"]

Main Thread: HTTPServer(:9100)
  └─ GET /metrics → return json(_state)
```

**Regex parse `[Perf]` line:**
```python
PERF_RE = re.compile(
    r'\[Perf\]\s+ticks=(\d+)\s+matches=(\d+)\s+pool_pending=(\d+).*?'
    r'tick avg=(\d+)\S*\s+p95=(\d+)\S*\s+p99=(\d+)\S*\s+min=(\d+)\S*\s+max=(\d+)\S*.*?'
    r'overruns=(\d+)'
)
```

**Output JSON tại `localhost:9100/metrics`:**
```json
{
  "status": "online",
  "timestamp": "2026-05-17T10:30:45",
  "active_matches": 1,
  "threadpool_queue_depth": 0,
  "tick_duration_us_avg": 230,
  "tick_duration_us_p95": 341,
  "tick_duration_us_p99": 540,
  "tick_duration_us_min": 103,
  "tick_duration_us_max": 21412,
  "tick_overruns": 1,
  "total_ticks": 3600,
  "tick_budget_us": 16667,
  "process_memory_mb": 54.2,
  "kafka_events_consumed": 3,
  "kafka_events_produced": 2,
  "java_services": {
    "eureka": "UP",
    "gateway": "UP",
    "auth": "UP",
    "matchmaking": "UP",
    "history": "UP",
    "shop": "UP",
    "monitoring": "UP"
  }
}
```

### 3.4 Bước 3: Monitoring service proxy

`TankMetricsController.java` proxy request từ browser → agent:

```
Browser → GET /api/tank/metrics (monitoring_service :8090)
             ↓
         TankMetricsController.getMetrics()
             ↓
         RestTemplate.getForObject("http://localhost:9100/metrics")
             ↓
         tank_metrics_agent.py → trả JSON
             ↓
         metrics.put("status", "online")   ← gắn thêm field
             ↓
         ResponseEntity.ok(metrics)        ← trả về browser
```

---

## 4. Catalog metrics và ánh xạ UI

### 4.1 Dashboard `/tank.html`

| Field trong JSON | UI Element | Ghi chú |
|-----------------|------------|---------|
| `active_matches` | KPI "Active Matches" | Từ `[Perf]` log |
| `connected_players` | KPI "Players Online" | `null` — cần C++ HTTP endpoint |
| `tick_duration_us_avg` | KPI "Tick Avg (µs)" + Progress bar Avg | Từ `[Perf]` log |
| `packets_received_per_sec` | KPI "Packets/sec" | `null` — cần C++ |
| `tick_duration_us_max` | Progress bar Worst case | Từ `[Perf]` log |
| `tick_duration_us_p95` | (không hiển thị trực tiếp, lưu CSV) | Từ `[Perf]` log |
| `tick_duration_us_p99` | (không hiển thị trực tiếp, lưu CSV) | Từ `[Perf]` log |
| `bullets_active` | Game State table | `null` — cần C++ |
| `physics_collisions_avg` | Game State table | `null` — cần C++ |
| `threadpool_queue_depth` | Game State table | Từ `[Perf]` log |
| `process_memory_mb` | Network table "Process memory" | Từ PowerShell |
| `packets_received_per_sec` | Network table | `null` — cần C++ |
| `packets_sent_per_sec` | Network table | `null` — cần C++ |
| `packets_dropped` | Network table | `null` — cần C++ |
| `kafka_events_consumed` | Kafka table | Đếm từ log text |
| `kafka_events_produced` | Kafka table | Đếm từ log text |
| `java_services.*` | Java Services Health badges | Từ thread check_java_services |

**Màu progress bar theo % budget (16,667 µs):**

| % Budget | Màu | Ý nghĩa |
|----------|-----|---------|
| < 70% | Xanh lá (`#00d4aa`) | Bình thường |
| 70–90% | Vàng (`#ffd93d`) | Cần theo dõi |
| > 90% | Đỏ (`#ff6b6b`) | Nguy hiểm, tick overrun sắp xảy ra |

### 4.2 Spring Boot Admin (/)

Sau khi monitoring_service khởi động và các service đã đăng ký vào Eureka:

| Tab | Dữ liệu | Source |
|-----|---------|--------|
| Applications | Danh sách service + UP/DOWN | `/actuator/health` |
| Details | Health components (DB, Redis, Kafka) | `/actuator/health` |
| Metrics | JVM memory, CPU, thread count | `/actuator/metrics/{name}` |
| HTTP Exchanges | Request log theo endpoint | `/actuator/httpexchanges` |
| Logfile | Log output realtime | `/actuator/logfile` |
| Environment | Config properties | `/actuator/env` |
| Threads | Thread dump | `/actuator/threaddump` |

---

## 5. Time-series CSV (baseline data)

Agent ghi mỗi `[Perf]` window vào CSV `/tmp/tank_metrics_timeseries.csv`:

```
timestamp,active_matches,pool_pending,tick_avg_us,tick_p95_us,tick_p99_us,tick_min_us,tick_max_us,tick_overruns,total_ticks
2026-05-17T10:30:45,1,0,230,341,540,103,21412,1,600
2026-05-17T10:30:55,1,0,207,360,904,108,2425,0,1200
2026-05-17T10:31:05,1,0,260,365,739,101,32120,1,1800
```

Mỗi row = 1 window 600 tick (~10 giây thực).

---

## 6. Thu thập baseline

### Tại sao cần baseline?

Baseline xác định **hiệu năng bình thường** ở từng điều kiện tải. Khi có sự cố, so sánh với baseline để biết đã xấu đi bao nhiêu.

### Baseline đã có (đo bằng load_client.exe)

Từ [performance_benchmark.md](performance_benchmark.md):

| Kiến trúc | Load | avg tick | p99 tick | max tick | Overruns |
|-----------|------|----------|----------|----------|----------|
| **A — Single-thread** | 128 clients (~2,816 pps) | 990 µs | ~14,500 µs | 109,563 µs | 3/3,000 |
| **B — IOCP** | 2 players (~600 pps) | 228 µs | ~700 µs | 32,120 µs | 2/2,400 |
| **B — IOCP** | idle | 155–260 µs | ~500 µs | — | 0 |

> **Ý nghĩa**: IOCP giảm avg tick **76%** và p99 **95%** so với single-thread dưới cùng tải.

### Thu thập baseline hiện tại bằng `collect_baseline.py`

```bash
# Bước 1: Đảm bảo agent đang chạy
python3 /tmp/tank_metrics_agent.py &

# Bước 2: Thu thập 60s khi idle (0 match đang chạy)
python3 /tmp/collect_baseline.py --label idle --duration 60

# Bước 3: Khởi động 1 match (2 players) rồi thu thập
python3 /tmp/collect_baseline.py --label iocp_2p --duration 60

# Bước 4: So sánh 2 file CSV vừa tạo
python3 /tmp/collect_baseline.py compare \
    /tmp/baseline_idle_*.csv \
    /tmp/baseline_iocp_2p_*.csv
```

**Output mẫu:**
```
========================================================
SUMMARY — iocp_2p  (30 samples)
========================================================
  Tick avg  (mean)  : 228 µs
  Tick avg  (median): 220 µs
  Tick p99  (mean)  : 680 µs
  Tick max  (peak)  : 32120 µs
  Overruns  (peak)  : 2
  Budget usage avg  : 1.4%
  Budget usage p99  : 4.1%
  Process memory    : 54.2 MB
========================================================

So sánh với dữ liệu đã đo trong performance_benchmark.md:
  Architecture                   avg µs   p99 µs   max µs
  ─────────────────────────────────────────────────────
  Baseline A (1 thread)             990   14500   109563
  IOCP (16 workers)                 228     700    32120
  >>> Current measurement (iocp_2p) 228     680    32120
```

---

## 7. Chạy toàn bộ stack monitoring

### Thứ tự khởi động

```bash
# 1. Docker services (Kafka, Postgres, Redis, MySQL) — nếu chưa chạy
# (xem CLAUDE.md mục 1)

# 2. Eureka
java -jar .../discovery_service/target/discovery_service-*.jar > /tmp/eureka.log 2>&1 &
sleep 20

# 3. Các Java service
java -jar .../auth_service/target/auth-service-*.jar > /tmp/auth.log 2>&1 &
java -jar .../api_gateway/target/api_gateway-*.jar > /tmp/gateway.log 2>&1 &
java -jar .../matchmaking_service/target/matchmaking_service-*.jar > /tmp/matchmaking.log 2>&1 &
java -jar .../history_service/target/history_service-*.jar > /tmp/history.log 2>&1 &
java -jar .../shop/target/shop-*.jar > /tmp/shop.log 2>&1 &

# 4. Monitoring service
java -jar .../monitoring_service/target/monitoring_service-*.jar > /tmp/monitoring.log 2>&1 &
sleep 10

# 5. Tank server (Windows) — redirect stdout sang log file
python3 /tmp/start_server.py

# 6. Python metrics agent (WSL2) — tail log + expose :9100
python3 /tmp/tank_metrics_agent.py
```

### Xác minh hoạt động

```bash
# Agent đang chạy?
curl -s http://localhost:9100/metrics | python3 -m json.tool | head -20

# Monitoring service proxy hoạt động?
curl -s http://localhost:8090/api/tank/metrics | python3 -m json.tool | head -10

# Java services có UP không?
curl -s http://localhost:8761/actuator/health   # Eureka
curl -s http://localhost:8080/actuator/health   # Gateway
```

### Truy cập dashboard

| URL | Nội dung |
|-----|---------|
| `http://localhost:8090/tank.html` | Tank server metrics + Java services badges |
| `http://localhost:8090` | Spring Boot Admin (Java services full detail) |
| `http://localhost:9100/metrics` | Raw JSON từ Python agent |

---

## 8. Metrics hiện không thu thập được (cần mở rộng C++)

Các trường sau hiện trả về `null` vì C++ server chưa log chúng.
Để thu thập, cần thêm HTTP endpoint vào `main.cpp` (WinSock2 TCP, ~100 dòng):

| Metric | Cách thu thập khi mở rộng |
|--------|--------------------------|
| `connected_players` | Counter trong `MatchManager::resolvePlayer()` |
| `packets_received_per_sec` | Counter trong `NetworkManager` recv callback |
| `packets_sent_per_sec` | Counter trong `broadcastSnapshot()` |
| `packets_dropped` | Counter khi `BufferPool` full |
| `bullets_active` | Tổng `match.bullets.size()` qua các active match |
| `physics_collisions_avg` | Counter trong `PhysicsWorld::step()` |

Khi có HTTP endpoint ở port 9100 trong C++, xóa `tank_metrics_agent.py` và trỏ `TankMetricsController` trực tiếp vào `172.25.192.1:9100`.

---

## 9. Tóm tắt luồng dữ liệu đầy đủ

```
C++ server_tank.exe  (Windows)
  │  stdout → server_tank.log (mỗi 10s: 1 dòng [Perf])
  │
  ▼ (WSL2 file mount /mnt/d/...)
tank_metrics_agent.py  (WSL2, port 9100)
  ├─ Thread 1: tail log → parse → _state (dict)
  ├─ Thread 2: poll /actuator/health → _state["java_services"]
  ├─ Thread 3: powershell Get-Process → _state["process_memory_mb"]
  └─ HTTP: GET /metrics → JSON(_state)
               │
  ┌────────────┘  GET http://localhost:9100/metrics
  │
TankMetricsController.java  (monitoring_service :8090)
  └─ GET /api/tank/metrics → proxy JSON
               │
  ┌────────────┘  fetch('/api/tank/metrics') every 2s
  │
/tank.html  (browser)
  ├─ KPI cards: matches, tick avg
  ├─ Progress bars: tick budget
  ├─ Tables: game state, network, kafka
  └─ Java health badges: UP/DOWN per service

Spring Boot Admin  (auto-discovery via Eureka)
  └─ Full JVM + HTTP + DB metrics per Java service
```
