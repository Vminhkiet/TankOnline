# Cơ chế Backend — SE315.Q21 WSL2 + Windows Hybrid

> Tài liệu kỹ thuật mô tả toàn bộ luồng backend: từ login → matchmaking → gameplay → kết quả.

---

## 1. Tổng quan kiến trúc

```
                    WSL2 (Ubuntu)
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│  Docker containers:                                           │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────┐           │
│  │  Zookeeper   │  │    Kafka     │  │ Postgres │           │
│  │  :2181       │◄─│  :9092       │  │  :5432   │           │
│  └──────────────┘  └──────┬───────┘  └──────────┘           │
│                           │          ┌──────────┐            │
│                           │          │  Redis   │            │
│                           │          │  :6379   │            │
│                           │          └──────────┘            │
│                           │          ┌──────────┐            │
│                           │          │  MySQL   │            │
│                           │          │  :3307   │            │
│                           │          └──────────┘            │
│  Java services (bare process):                                │
│  ┌────────────┐  ┌──────────┐  ┌───────────────┐            │
│  │  Eureka    │  │   Auth   │  │  Matchmaking  │            │
│  │  :8761     │  │  :8081   │  │  :8085        │            │
│  └────────────┘  └──────────┘  └───────┬───────┘            │
│  ┌────────────┐  ┌──────────┐          │ Kafka              │
│  │  Gateway   │  │ History  │          │ match.create ──────┼──┐
│  │  :8080     │  │  :8086   │          │                    │  │
│  └────────────┘  └──────────┘  ┌───────────────┐           │  │
│  ┌────────────┐  ┌──────────┐  │  Monitoring   │           │  │
│  │  Profile   │  │  Shop    │  │  :8090        │           │  │
│  │  :8087     │  │  :8088   │  └───────────────┘           │  │
│  └────────────┘  └──────────┘                               │  │
└───────────────────────────────────────────────────────────────┘  │
                                                                    │
                    Windows Host                                    │
┌───────────────────────────────────────────────────────────────┐  │
│  server_tank.exe  (C++, UDP :8080)                            │◄─┘
│    ├── Consumes: match.create  (Kafka)                        │
│    ├── Produces: match.result  (Kafka) ──────────────────────►│ history_service
│    └── UDP gameplay ◄──────────────── Tank Legends.exe        │
└───────────────────────────────────────────────────────────────┘
```

---

## 2. Infrastructure — Docker

### 2.1 Tại sao dùng `docker run` thay `docker-compose`

`docker-compose v1.29.2` có bug `KeyError: 'ContainerConfig'` với Docker Engine mới. Mỗi container được khởi động riêng bằng `docker run`.

### 2.2 Kafka networking — điểm quan trọng nhất

Kafka cần 2 listener phân biệt:

| Listener | Địa chỉ | Dùng cho |
|----------|---------|----------|
| `PLAINTEXT` | `kafka:29092` | Inter-broker (container↔container) |
| `PLAINTEXT_HOST` | `<WSL2_IP>:9092` | Từ bên ngoài container (Java services, Windows server) |

```bash
# KAFKA_ADVERTISED_LISTENERS phải dùng hostname "kafka" (không phải container ID)
# → bắt buộc thêm --hostname kafka khi docker run
docker run --hostname kafka ...
  -e "KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://kafka:29092,PLAINTEXT_HOST://${WSL2_IP}:9092"
```

**Nếu thiếu `--hostname kafka`**: broker tự đặt hostname = container ID random, advertise địa chỉ không resolve được → controller báo "available brokers: 0" → `INVALID_REPLICATION_FACTOR` khi tạo topic.

### 2.3 WSL2 IP thay đổi sau reboot

```bash
WSL2_IP=$(ip addr show eth0 | grep "inet " | awk '{print $2}' | cut -d/ -f1)
```

Mỗi lần khởi động lại WSL2, `eth0` nhận IP mới từ Hyper-V NAT. Phải cập nhật:
1. `KAFKA_ADVERTISED_LISTENERS` → restart Kafka container
2. `kafka.bootstrap-servers` trong `application.yaml` của matchmaking
3. Hardcoded default trong `main.cpp` của server_tank (vì WSL2 interop không pass env var cho Windows .exe)

### 2.4 Các database

| Container | Engine | DB / User | Dùng cho |
|-----------|--------|-----------|---------|
| `postgres_container` | PostgreSQL 16 | `auth_service_db / auth_user` | Users, sessions, JWT |
| `postgres_container` | PostgreSQL 16 | `history_service_db` | Lịch sử trận đấu |
| `postgres_container` | PostgreSQL 16 | `profile_service_db` | Hồ sơ người chơi |
| `redis` | Redis alpine | — (requirepass 123456) | Session tokens, matchmaking queue |
| `mysql_container` | MySQL 8.0 | `shoptank_db` | Danh mục shop, giao dịch |

---

## 3. Java Microservices

### 3.1 Thứ tự khởi động

```
Eureka (8761) → Auth + Shop + Matchmaking + History + Profile + Monitoring → Gateway (8080)
```

Gateway phải start **sau cùng** vì nó cần resolve service names qua Eureka. Nếu start trước, route `lb://auth-service` không tìm thấy instance → 503.

### 3.2 Discovery Service — Eureka (:8761)

Service registry trung tâm. Mọi Java service đăng ký tại đây với:

```yaml
eureka:
  instance:
    prefer-ip-address: true
  client:
    service-url:
      defaultZone: http://localhost:8761/eureka/
```

Gateway dùng `lb://` prefix để load balance qua Eureka thay vì hardcode host:port.

### 3.3 API Gateway (:8080)

Entry point duy nhất cho client. Thực hiện 3 việc trước khi forward request:

```
Bước 1: JwtAuthenticationFilter
        Đọc "Authorization: Bearer <token>"
        → verify signature bằng JWT_SECRET
        → nếu sai/hết hạn → 401 ngay, không forward

Bước 2: Inject headers
        X-User-Id:    <userId từ JWT claims>
        X-User-Roles: <roles từ JWT claims>
        X-Gateway-Origin: MySecretKey123   ← secret downstream dùng để verify đúng từ gateway

Bước 3: Route
        /api/auth/**     → lb://auth-service   (no JWT required)
        /api/shop/**     → http://localhost:8088
        /api/matchmaking/** → lb://matchmaking-service
```

### 3.4 Auth Service (:8081)

**Login flow:**

```
POST /api/auth/login  {username, password}
  │
  ├── UserRepository.findByUsername() → PostgreSQL
  ├── BCryptPasswordEncoder.matches(input, stored_hash)
  │     Nếu không khớp → 401
  │
  ├── Tạo accessToken (JWT, 1h TTL):
  │     claims: {sub: userId, roles: [...], iat, exp}
  │     sign bằng HS256 + JWT_SECRET
  │
  ├── Tạo refreshToken (UUID):
  │     lưu vào Redis: key="session:<userId>" value=refreshToken TTL=7d
  │
  └── Trả về: {jwt: "...", refreshToken: "...", userId, roles}
```

**Logout flow:**
```
DELETE Redis key "session:<userId>"
→ refresh token bị vô hiệu hoá ngay lập tức
```

**Fix BCrypt hash mismatch:**
Nếu DB được seed với hash từ bcrypt rounds khác, login trả 403. Chạy:
```python
import bcrypt, subprocess
new_hash = bcrypt.hashpw(b'password123', bcrypt.gensalt(rounds=10)).decode()
sql = f"UPDATE users SET password = '{new_hash}' WHERE username IN ('player1','player2');"
subprocess.run(['docker','exec','postgres_container','psql',
                '-U','auth_user','-d','auth_service_db','-c', sql])
```

### 3.5 Matchmaking Service (:8085)

**Luồng tìm trận:**

```
POST /api/matchmaking/find  (Bearer JWT required)
  │
  ├── Xóa entry cũ của userId trong Redis queue (tránh A vs A)
  ├── Push userId vào QUEUE_KEY = "matchmaking:queue" (Redis List)
  ├── setStatus(userId, "waiting")
  │
  ├── Poll loop (500ms interval, timeout 10s):
  │     queue.size >= 2?
  │     └── tryFormMatch():
  │           Pop tối đa 10 entry, lọc entry có status="waiting"
  │           Nếu tìm đủ 2 → createMatch([p1, p2])
  │           Nếu không đủ → đẩy lại queue
  │
  ├── Timeout → createMatch([userId, "bot-1"])  ← bot match
  │
  └── createMatch():
        matchId = Redis INCR "matchmaking:counter"
        Gán slot ID: p1→1, p2→2 (server dùng int, không phải username)
        Lưu match vào Redis hash
        Publish Kafka "match.create":
          {"matchId":N, "players":[1,2], "userIds":{"1":"player1","2":"player2"},
           "mapName":"world", "maxDuration":180}
        Trả về: {matchId, serverHost, serverPort, playerId}
```

**Tại sao dùng slot ID thay username?**

Server C++ quản lý session bằng `uint16_t playerId`. Username là string (`"player1"`) → `Integer.parseInt("player1")` → `NumberFormatException`. Fix: map username → sequential int [1, 2, ...] trước khi publish Kafka.

### 3.6 Các service còn lại

| Service | Port | DB | Chức năng |
|---------|------|-----|-----------|
| History | 8086 | PostgreSQL `history_service_db` | Lưu kết quả trận từ `match.result` Kafka |
| Profile | 8087 | PostgreSQL `profile_service_db` | Thống kê người chơi (win/loss/kills) |
| Shop | 8088 | MySQL `shoptank_db` | Danh mục xe tăng, mua bán items |
| Monitoring | 8090 | — (in-memory) | Nhận metrics từ `game.perf` Kafka, expose Prometheus |

---

## 4. Kafka Message Flow

### 4.1 match.create

**Producer**: Matchmaking Service  
**Consumer**: server_tank.exe (Windows)

```json
{
  "matchId": 716578,
  "players": [1, 2],
  "userIds": {"1": "player1", "2": "player2"},
  "mapName": "world",
  "maxDuration": 180
}
```

Server nhận → tạo `Match` object trong memory → spawn tanks tại vị trí spawn → bắt đầu nhận UDP từ client.

### 4.2 match.result

**Producer**: server_tank.exe (Windows)  
**Consumer**: History Service, Profile Service

```json
{
  "matchId": 716578,
  "outcome": "win",
  "winnerId": 1,
  "durationSecs": 47.3,
  "kills": {"1": 3, "2": 1}
}
```

History Service lưu vào PostgreSQL. Profile Service cập nhật stats người chơi.

### 4.3 Vì sao Kafka thay vì REST call trực tiếp?

Server tank là Windows `.exe`, không có HTTP server. Kafka là kênh duy nhất để Java (WSL2) ↔ C++ (Windows) giao tiếp mà không cần mở thêm port hay custom protocol.

---

## 5. Tank Server (Windows) — Tương tác với WSL2

### 5.1 Vấn đề WSL2 interop

WSL2 không truyền được env var sang Windows `.exe`:

```bash
# Không hoạt động — .exe không nhận được KAFKA_BROKERS
KAFKA_BROKERS=172.25.x.x:9092 /mnt/d/.../server_tank.exe

# Không hoạt động — WSLENV cũng không đủ
export WSLENV=KAFKA_BROKERS/w
```

**Fix**: Hardcode WSL2 IP như default value trong `main.cpp`:
```cpp
const std::string kafkaBrokers = getEnv("KAFKA_BROKERS", "172.25.203.168:9092");
```

Mỗi khi WSL2 IP đổi (reboot) → phải sửa dòng này + rebuild.

### 5.2 Khởi động server_tank.exe từ WSL2

Dùng Python `subprocess.Popen` thay vì bash trực tiếp (tránh exit code 144 / SIGSTKFLT):

```python
import subprocess
exe = '/mnt/d/Unity/TankOnline/SE315.Q21/Tank/out/build/x64-Release/server_tank/Release/server_tank.exe'
cwd = '/mnt/d/Unity/TankOnline/SE315.Q21/Tank/out/build/x64-Release/server_tank/Release'
p = subprocess.Popen([exe], cwd=cwd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
```

### 5.3 UDP Packet Format

Client gửi lên server (bit-packed, Little Endian):

**C2S_MOVE (1001)**
```
packetSize [8..1400] → 11 bits
opcode     [0..65535] = 1001
matchId    [0..1000000]
playerId   [0..255]
seq        [0..255]
unk        [0..65535] = 0
dir_x      [0..2]  (0=left, 1=none, 2=right)
dir_z      [0..2]  (0=back, 1=none, 2=forward)
unk2       [0..255] = 0
```

**C2S_SHOOT (1002)**
```
packetSize [8..1400]
opcode     = 1002
matchId, playerId, seq, unk
force      [15..30]
```

Server gửi xuống client (raw binary, NOT bit-packed):

**S2C_SNAPSHOT (2000)**
```
SnapshotHeader (14 bytes):
  matchId             uint32
  opcode              uint16 = 2000
  serverTick          uint16
  tankCount           uint16
  localPlayerId       uint16
  timeRemainingTenths uint16

tankCount (repeat)    uint16  ← body prefix, client skip 2 bytes này

TankState × N (26 bytes mỗi tank):
  tankId    uint32
  x,y,z     float × 3
  yaw       float      ← radian
  health    int16
  flags     uint8      bit0=isAlive, bit1=inBush
  score     uint16
  placement uint8
```

---

## 6. Bảo mật theo tầng

```
Internet
    │
    ▼
API Gateway (:8080)
    ├─ [Layer 1] JwtAuthenticationFilter
    │    verify JWT signature + expiry
    │    → 401 nếu thiếu/sai/hết hạn token
    │    Ngoại lệ: /api/auth/** (public)
    │
    └─ Forward + inject headers
         X-Gateway-Origin: MySecretKey123
              │
              ▼
         Shop / Matchmaking / ...
              ├─ [Layer 2] GatewayFilter
              │    kiểm tra X-Gateway-Origin == secret
              │    → 403 nếu request đến trực tiếp (bypass gateway)
              │
              └─ [Layer 3] GatewayHeaderAuthFilter
                   đọc X-User-Id, X-User-Roles từ header
                   set Spring Security context
                   → controller nhận Authentication object
```

Tấn công bypass điển hình:
```bash
# Thử gọi thẳng shop:8088 với JWT hợp lệ → bị chặn ở Layer 2
curl http://localhost:8088/api/shop/items -H "Authorization: Bearer $JWT"
# → 403 Forbidden (thiếu X-Gateway-Origin)
```

---

## 7. Luồng đầy đủ từ Login đến Match

```
1. Login
   Client → POST /api/auth/login → Gateway → Auth Service → PostgreSQL
   ← JWT (1h) + refreshToken (7d)

2. Tìm trận
   Client → POST /api/matchmaking/find (Bearer JWT)
   → Gateway verify JWT + inject headers
   → Matchmaking Service → Redis queue
   → 2 player khớp → createMatch()
   → Kafka publish match.create
   ← {matchId, serverHost: "10.11.1.68", serverPort: 8080, playerId: 1}

3. Gameplay
   Tank Legends.exe nhận match info
   ← Kafka match.create → server_tank.exe spawn tanks
   Client ←UDP→ server_tank.exe (8080)
   server broadcast S2C_SNAPSHOT mỗi 50ms

4. Kết thúc
   server_tank.exe → Kafka publish match.result
   → History Service lưu PostgreSQL
   → Profile Service cập nhật stats
```

---

## 8. start.sh — Script khởi động tự động

`/home/minhk/project/SE315.Q21/start.sh` thực hiện 5 bước:

| Bước | Việc làm | Thời gian |
|------|----------|-----------|
| 0 | Kill Java processes cũ | ~2s |
| 1 | Start 5 Docker containers (Zookeeper, Kafka, Postgres, Redis, MySQL) | ~10s |
| 2 | Tạo Kafka topics `match.create`, `match.result` | ~15s (chờ Kafka ready) |
| 3 | Build JAR nếu thiếu, start Eureka → chờ UP → start 7 services còn lại | ~60s |
| 4 | Kiểm tra `server_tank.exe`, build nếu thiếu, start | ~5s |

---

## 9. Troubleshooting nhanh

| Triệu chứng | Nguyên nhân | Fix |
|-------------|-------------|-----|
| Kafka `INVALID_REPLICATION_FACTOR` | Thiếu `--hostname kafka` | Restart với `--hostname kafka` |
| Login → 403 | BCrypt hash mismatch | Re-hash bằng Python bcrypt |
| Service → 503 | Chưa đăng ký Eureka | Đợi thêm 30s |
| Tank server không nhận match | WSL2 IP đổi sau reboot | Cập nhật `main.cpp` + rebuild |
| `kafka_read_new` trả None | Consumer timeout quá ngắn | Dùng `--timeout-ms 8000+` |
| Redis `NOAUTH` | Spring Boot 3 + Redis 7 | Config `spring.data.redis.password` |
| Bash exit 144 khi chạy .exe | WSL2 interop bug | Dùng Python `subprocess.Popen` |

---

## 10. Logs

| File | Service |
|------|---------|
| `/tmp/eureka.log` | Eureka Discovery |
| `/tmp/auth.log` | Auth Service |
| `/tmp/gateway.log` | API Gateway |
| `/tmp/matchmaking.log` | Matchmaking |
| `/tmp/history.log` | History |
| `/tmp/profile.log` | Profile |
| `/tmp/shop.log` | Shop |
| `/tmp/monitoring.log` | Monitoring |
