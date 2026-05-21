# Kiến trúc Hệ thống Backend — Tank Online

## 1. Tổng quan

Tank Online sử dụng kiến trúc **microservices** kết hợp với **event-driven** thông qua Apache Kafka. Toàn bộ backend chia làm hai lớp:

- **Java Layer (WSL2/Linux):** Các Spring Boot microservice xử lý auth, matchmaking, lịch sử trận đấu, cửa hàng
- **Game Server Layer (Windows):** C++ UDP server xử lý game logic thời gian thực 60 Hz

```
Client (Unity Game / Browser)
        │  HTTP (REST)           UDP (game packets)
        │                              │
        ▼                              ▼
┌───────────────────────────────────────────────────────────────────┐
│                     WSL2 (Ubuntu 22.04)                           │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │             API Gateway  :8080  (entry point)               │  │
│  │  JWT validation · routing · header injection                │  │
│  └────┬──────────┬───────────┬──────────────┬──────────────────┘  │
│       │          │           │              │                      │
│       ▼          ▼           ▼              ▼                      │
│  ┌────────┐ ┌────────┐ ┌─────────┐ ┌────────────┐ ┌───────────┐  │
│  │  Auth  │ │Matchmk │ │ History │ │   Shop     │ │Monitoring │  │
│  │  :8081 │ │  :8085 │ │  :8086  │ │   :8088    │ │   :8090   │  │
│  └───┬────┘ └────┬───┘ └────┬────┘ └─────┬──────┘ └───────────┘  │
│      │           │          │             │                        │
│  ┌───▼───┐  ┌────▼──────────▼──────┐ ┌───▼──────┐                │
│  │Postgres│  │       Kafka :9092    │ │  MySQL   │                │
│  │ :5432  │  │  match.create        │ │  :3307   │                │
│  │        │  │  match.result        │ │          │                │
│  └────────┘  │  user.session.       │ └──────────┘                │
│              │    invalidated       │                              │
│  ┌───────┐   └───────────┬──────────┘                             │
│  │ Redis │               │                                        │
│  │ :6379 │               │  Kafka consumer (librdkafka)           │
│  └───────┘               │                                        │
└──────────────────────────┼────────────────────────────────────────┘
                           │  TCP :9092
                           │
┌──────────────────────────┼────────────────────────────────────────┐
│          Windows Host (172.25.192.1)                              │
│                          │                                         │
│        ┌─────────────────▼────────────────────┐                   │
│        │     server_tank.exe  (C++ IOCP)       │                   │
│        │  UDP :8080  ·  60 Hz tick loop        │                   │
│        │  Kafka consumer: match.create         │                   │
│        │  Kafka consumer: user.session.        │                   │
│        │    invalidated                        │                   │
│        │  Kafka producer: match.result         │                   │
│        └──────────────────────────────────────┘                   │
└───────────────────────────────────────────────────────────────────┘
```

---

## 2. Danh sách Service

| Service | Port | Công nghệ | Vai trò |
|---------|------|-----------|---------|
| **Discovery Service** | 8761 | Spring Cloud Eureka | Service registry — tất cả service đăng ký ở đây |
| **API Gateway** | 8080 | Spring Cloud Gateway | Entry point duy nhất — JWT validation, routing |
| **Auth Service** | 8081 | Spring Boot + PostgreSQL + Redis | Đăng ký, đăng nhập, cấp JWT, phát hiện đăng nhập trùng |
| **Matchmaking Service** | 8085 | Spring Boot + Redis + Kafka | Ghép cặp người chơi, sinh token, publish `match.create` |
| **History Service** | 8086 | Spring Boot + PostgreSQL + Kafka | Lưu kết quả trận, thống kê, bảng xếp hạng |
| **Shop Service** | 8088 | Spring Boot + MySQL | Cửa hàng vật phẩm in-game |
| **Monitoring Service** | 8090 | Spring Boot Admin | Dashboard health/metrics toàn hệ thống |
| **Game Server** | UDP 8080 | C++ (IOCP) + librdkafka | Game loop 60 Hz, xử lý UDP packets, publish `match.result` |

---

## 3. Hạ tầng (Infrastructure)

### Docker Containers

| Container | Image | Port | Dùng bởi |
|-----------|-------|------|---------|
| `zookeeper` | cp-zookeeper:7.3.0 | 2181 | Kafka metadata |
| `kafka` | cp-kafka:7.3.0 | 9092 | Message broker |
| `postgres_container` | postgres:16.0 | 5432 | Auth, History |
| `redis` | redis:alpine | 6379 | Auth sessions, Matchmaking |
| `mysql_container` | mysql:8.0 | 3307 | Shop |

### Kafka Topics

| Topic | Producer | Consumer | Payload |
|-------|----------|----------|---------|
| `match.create` | Matchmaking Service | Game Server (C++) | `{matchId, mapName, maxDuration, players:[p1,p2], userIds:{pid:uid}, tokens:{pid:token}}` |
| `match.result` | Game Server (C++) | History Service | `{matchId, outcome, winnerId, durationSecs, kills:{pid:count}, userIds:{}}` |
| `user.session.invalidated` | Auth Service | Matchmaking Service, Game Server (C++) | `{userId, code, message, timestamp}` |

### Cơ sở dữ liệu

**PostgreSQL (:5432)**

```
auth_service_db
└── users (id, username, password[bcrypt], email, address, role, created_at)

history_service_db
└── match_history (id, match_id, player_id, user_id, opponent_id, opponent_user_id,
                   result[WIN/LOSE/DRAW], kills, deaths, duration_secs, map_name, played_at)
```

**MySQL (:3307)**

```
shoptank_db
├── items (id, name, description, image_url, price, category, available)
└── shop_categories (id, name, description)
```

**Redis (:6379, password: 123456)**

```
DB 0 — Auth sessions
  Key serializer  : StringRedisSerializer (plain text)
  Value serializer: GenericJackson2JsonRedisSerializer
  Key pattern: refresh:<userId>
  Value:  UserSession {refreshToken, roles, createdAt}
  TTL:    7 ngày

DB 1 — Matchmaking state
  Key serializer  : StringRedisSerializer (plain text)  ← fix so KEYS pattern hoạt động đúng
  Value serializer: GenericJackson2JsonRedisSerializer

  matchmaking:searching:<userId>         "searching"          TTL 120s
    → Đánh dấu user đang trong hàng đợi; tự xóa sau khi match được tạo

  matchmaking:token:<token>              "<matchId>:<playerId>"  TTL 120s
    → Map UUID token → matchId + playerId slot; dùng để C++ verify client kết nối

  matchmaking:match:<matchId>            "<userId1>,<userId2>"   TTL 600s
    → Lưu danh sách userId tham gia trận đang diễn ra
```

---

## 4. Chi tiết từng Service

### 4.1 Discovery Service (Eureka)

- **Port:** 8761
- **Vai trò:** Registry trung tâm. Tất cả service đăng ký khi khởi động, API Gateway dùng Eureka để load-balance
- **Cấu hình:**
  ```properties
  eureka.client.fetch-registry=false
  eureka.client.register-with-eureka=false
  ```
- **UI:** http://localhost:8761

---

### 4.2 API Gateway

- **Port:** 8080
- **Vai trò:** Entry point duy nhất. Validate JWT, inject header, route request
- **Route table:**

| Path | Route đến | Ghi chú |
|------|-----------|---------|
| `/api/auth/**` | `lb://auth-service` | Public (không cần JWT) |
| `/api/user/**` | `lb://auth-service` | Public |
| `/api/matchmaking/**` | `lb://matchmaking-service` | Cần JWT + thêm header `X-Gateway-Origin` |
| `/api/history/leaderboard` | `lb://history-service` | Public |
| `/api/history/**` | `lb://history-service` | Cần JWT |
| `/api/shop/**` | `http://localhost:8088` | Cần JWT + thêm header `X-Gateway-Origin` |
| `/api/tank/**` | `lb://monitoring-service` | Cần JWT (Admin) |
| `/api/monitoring/**` | `lb://monitoring-service` | Cần JWT (Admin) |

- **JWT Filter (JwtAuthenticationFilter):**
  1. Đọc `Authorization: Bearer <token>`
  2. Verify chữ ký với `jwt.secret-key`
  3. Inject `X-User-Id`, `X-User-Roles` vào header cho downstream service

- **Bảo vệ 3 tầng:**
  - Gateway từ chối request thiếu JWT → `401`
  - Shop/Matchmaking từ chối request không qua Gateway (thiếu `X-Gateway-Origin`) → `403`
  - Spring Security trong từng service dùng `X-User-Id` header để xác định user

---

### 4.3 Auth Service

- **Port:** 8081
- **DB:** PostgreSQL `auth_service_db`
- **Cache:** Redis DB 0 (session + refresh token, TTL 7 ngày)
- **JWT Secret:** `MySuperSecretKey12345678901234567890`

**Endpoints:**

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `/api/auth/signup` | — | Đăng ký tài khoản mới |
| POST | `/api/auth/login` | — | Đăng nhập, trả JWT + refresh token |
| POST | `/api/auth/logout` | JWT | Xóa session trong Redis |
| POST | `/api/auth/refresh` | — | Đổi refresh token lấy access token mới |
| GET | `/api/user/users` | JWT (Admin) | Danh sách tất cả user |

**Login response:**
```json
{
  "jwt": "<access_token>",
  "refreshToken": "<refresh_token>",
  "expiresIn": 86400
}
```

**Flow đăng nhập:**
```
POST /api/auth/login {username, password}
  → Truy vấn PostgreSQL → verify BCrypt hash
  → Tạo JWT {userId, authorities, exp=24h}
  → Lưu refresh token vào Redis DB0 (TTL 7 ngày)
  → Trả {jwt, refreshToken, expiresIn}
```

**Saga-4 — Duplicate Login Kick:**
```
Khi user đăng nhập từ thiết bị thứ hai:
  → Xóa session cũ khỏi Redis
  → Publish Kafka: user.session.invalidated {userId, code:1003, message, timestamp}
    ├── Matchmaking: xóa userId khỏi lobby (tránh player zombie chờ mãi)
    └── Game Server: force-disconnect player đang chơi (WebSocket close code 1003)
```

---

### 4.4 Matchmaking Service

- **Port:** 8085
- **Cache:** Redis DB 1 (trạng thái tìm trận, token map)
- **Kafka Producer:** topic `match.create`
- **Kafka Consumer:** topic `user.session.invalidated` (group `matchmaking-service-saga`)
- **Game server host:** `10.11.1.177:8080` (WiFi IP — mobile và PC cùng mạng kết nối được)

**Endpoints:**

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `/api/matchmaking/find` | JWT | Tìm trận. Long-poll đến khi ghép được cặp |

**Response khi ghép thành công:**
```json
{
  "matchId": 1000,
  "serverHost": "10.11.1.177",
  "serverPort": 8080,
  "playerId": 1,
  "token": "a29ff97a83404af5a870045bf53e1e6f"
}
```

**Kafka message publish (`match.create`):**
```json
{
  "matchId": 1000,
  "mapName": "world",
  "maxDuration": 300,
  "players": [1, 2],
  "userIds": {"1": "2", "2": "3"},
  "tokens": {"1": "a29ff97a83404af5a870045bf53e1e6f", "2": "5b4b3976b2354afdb6cb5f0686c2d3a6"}
}
```

> **Lưu ý:** `players` là sequential slot ID (1, 2, …), KHÔNG phải DB user ID.  
> `userIds` map slot ID → DB user ID (string).  
> `tokens` map slot ID → UUID token 32 hex chars.

**Thuật toán ghép cặp (LobbyManager):**

```
Player gọi POST /find
  ├── Đọc userId từ JWT principal (numeric DB ID)
  ├── Redis SET matchmaking:searching:<userId> = "searching" (TTL 120s)
  ├── LobbyManager.addAndTryForm(entry, matchSize=2)  [synchronized]
  │     ├── Nếu cùng userId đã có trong lobby: complete future cũ với HTTP 409,
  │     │   xóa entry cũ, thêm entry mới (tìm lại sau khi cancel)
  │     ├── Thêm vào ArrayList lobby
  │     └── Nếu lobby.size() >= 2: trích batch, trả về
  │
  └── Nếu batch != null (match formed):
        ├── Sinh UUID token riêng cho mỗi player
        ├── Redis DELETE searching:<userId>  (cho cả hai)
        ├── Redis SET matchmaking:token:<token> = "<matchId>:<playerId>"  (TTL 120s)
        ├── Redis SET matchmaking:match:<matchId> = "<uid1>,<uid2>"  (TTL 600s)
        ├── Kafka publish: match.create {matchId, players, userIds, tokens}
        └── CompletableFuture.complete() → trả HTTP 200 cho cả hai request đang block
```

**Idempotency — tránh match A vs A:**
`LobbyManager` kiểm tra `userId` trước khi thêm. Nếu cùng userId gọi lại (tab mới, reconnect), entry cũ bị xóa và HTTP 409 được trả về cho request cũ.

**Saga-4 — Session Cleanup:**
`SessionInvalidatedKafkaListener` nhận `user.session.invalidated` → gọi `LobbyManager.removePlayer(userId)` → player bị xóa khỏi lobby, future hoàn thành với exception.

---

### 4.5 History Service

- **Port:** 8086
- **DB:** PostgreSQL `history_service_db`
- **Kafka Consumer:** topic `match.result`, group `history-service`, offset reset `earliest`

**Endpoints:**

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| GET | `/api/history/me` | JWT | 10 trận gần nhất của user |
| GET | `/api/history/me/stats` | JWT | Thống kê tổng: win rate, total kills |
| GET | `/api/history/leaderboard` | — | Top 10 người chơi theo số kill |

**Kafka consumer (`match.result`):**
```
Nhận: {matchId, winnerId, outcome, durationSecs, kills:{pid:count}, userIds:{pid:uid}}
  → Tạo 2 bản ghi MatchHistory (1 cho mỗi player)
  → Lưu vào PostgreSQL history_service_db
```

**Schema match_history:**
```
match_id, player_id, user_id, opponent_id, opponent_user_id,
result (WIN/LOSE/DRAW), kills, deaths, duration_secs, map_name, played_at
```

---

### 4.6 Shop Service

- **Port:** 8088 (direct HTTP, không qua Eureka)
- **DB:** MySQL `shoptank_db` (:3307)
- **Bảo vệ:** Chỉ nhận request có header `X-Gateway-Origin: MySecretKey123`

**Endpoints:**

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| GET | `/api/shop/items` | JWT | Danh sách tất cả vật phẩm |
| GET | `/api/shop/items/category/{cat}` | JWT | Lọc theo danh mục |
| GET | `/api/shop/items/{id}` | JWT | Chi tiết vật phẩm |
| POST | `/api/shop/purchase` | JWT | Mua vật phẩm |
| POST | `/api/shop/admin/items` | JWT (Admin) | Thêm vật phẩm |
| PUT | `/api/shop/admin/items/{id}` | JWT (Admin) | Sửa vật phẩm |
| DELETE | `/api/shop/admin/items/{id}` | JWT (Admin) | Xóa vật phẩm |

---

### 4.7 Monitoring Service

- **Port:** 8090
- **Vai trò:** Spring Boot Admin dashboard + proxy metrics từ C++ game server
- **Game server metrics port:** `localhost:9100` (C++ HTTP endpoint)

**Endpoints:**

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| GET | `/api/tank/metrics` | JWT (Admin) | Metrics từ C++ server |
| GET | `/api/tank/health` | JWT (Admin) | Health status C++ server |
| GET | `/api/monitoring/**` | JWT (Admin) | Spring Boot Admin dashboard |

---

### 4.8 Game Server (C++ — Windows)

- **Protocol:** UDP :8080
- **Host:** Windows host (truy cập từ WSL2 và game client):
  - WiFi LAN: `10.11.1.177` — mobile + PC cùng mạng WiFi kết nối được
  - WSL2 portproxy: `172.25.192.1` — routing WSL2 → Windows
- **Kafka:**
  - Consumer: `match.create` (group `tank-server`) — tạo match trong memory
  - Consumer: `user.session.invalidated` (group `tank-server`) — force-disconnect player
  - Producer: `match.result` — khi match kết thúc
- **Kafka broker:** `172.25.203.168:9092` (WSL2 eth0 IP — hardcoded trong `main.cpp`, thay đổi sau mỗi lần reboot)

**MatchConfig (C++):**
```cpp
struct MatchConfig {
    uint32_t matchId;
    std::vector<uint32_t> playerIds;                        // sequential slot IDs
    std::unordered_map<uint32_t, std::string> userIds;      // playerId → DB user ID
    std::unordered_map<uint32_t, std::string> playerTokens; // playerId → match token
    std::string mapName;
    int maxDurationSecs;
};
```

**Kiến trúc nội bộ:**

```
main.cpp
  ├── KafkaConsumer:
  │     ├── match.create             → MatchManager::createMatch(cfg)
  │     │     cfg bao gồm playerTokens từ Java matchmaking
  │     └── user.session.invalidated → MatchManager::forceLogoutByUserId(userId, code, msg, delayMs)
  │
  ├── NetworkManager (IOCP, hardware_concurrency×2 threads) → UDP recv → MatchManager::routeCommand()
  │
  └── MatchManager
        ├── tickDispatcher thread (1 jthread, 60 Hz)
        │     ├── snapshot active matches (shared_mutex)
        │     ├── pool.submit(match->tick(dt)) → ThreadPool workers
        │     ├── futures.get() [BARRIER]
        │     └── cleanup finished matches
        └── KafkaProducer → match.result khi match kết thúc
```

**Match lifecycle:**

| Giai đoạn | Điều kiện | Hành động |
|-----------|-----------|-----------|
| Tạo match | Nhận `match.create` từ Kafka | Tạo Match instance, lưu playerTokens |
| Nhận player | UDP packet đến, `resolvePlayer()` | Spawn tank tại vị trí cố định |
| Game loop | 60 Hz tick | Physics, bullets, collision detection |
| Kết thúc — win | Một tank HP = 0 | `outcome=win`, winnerId = người còn sống |
| Kết thúc — draw | Tất cả disconnect | `outcome=draw`, winnerId = 0 |
| Kết thúc — timeout | `elapsed >= maxDuration` | `outcome=timeout`, winner theo kill count |
| Publish | Sau khi kết thúc | Publish `match.result` to Kafka |
| Force logout | Nhận `user.session.invalidated` | Gửi WS close frame sau `disconnectAfterMs` ms |

**Packet format (UDP, bit-packed):**

```
C2S Move  (opcode 1001): packetSize · opcode · matchId · playerId · seq · dir_x · dir_z
C2S Shoot (opcode 1002): packetSize · opcode · matchId · playerId · seq · force
S2C Snapshot (opcode 2000, raw binary):
  Header: matchId(4B) · opcode(2B) · tick(2B) · tankCount(2B) · localPlayerId(2B)
  Per tank: tankId(4B) · x(4B) · y(4B) · z(4B) · yaw(4B) · hp(2B) · flags(1B)
```

---

## 5. Luồng xử lý chính

### 5.1 Authentication Flow

```
[Client] POST /api/auth/login {username, password}
    ↓
[Gateway] Bỏ qua JWT (public endpoint)
    ↓
[Auth Service]
  1. Query PostgreSQL: SELECT * FROM users WHERE username=?
  2. BCrypt.matches(password, hash)
  3. Tạo JWT: {userId (numeric DB ID), authorities, exp=24h}
  4. Lưu Redis DB0: refresh:<userId> → {refreshToken, roles} TTL=7d
  5. Trả {jwt, refreshToken, expiresIn}
    ↓
[Client] Lưu JWT, gửi kèm mọi request:
  Authorization: Bearer <jwt>
    ↓
[Gateway] JwtAuthenticationFilter:
  1. Parse & verify JWT signature
  2. Inject X-User-Id: <userId>, X-User-Roles: <roles>
    ↓
[Downstream Service]
  SecurityContext.getAuthentication().getPrincipal() → userId (String)
  parseLong(principal) → long userId (numeric DB ID)
```

### 5.2 Match Lifecycle với Token Flow (End-to-End)

```
[Client 1] POST /api/matchmaking/find  (JWT: userId=2)
[Client 2] POST /api/matchmaking/find  (JWT: userId=3)
    ↓
[Matchmaking Service]
  userId=2 vào lobby → chờ (CompletableFuture blocks HTTP thread)
  userId=3 vào lobby → đủ 2 → tạo match
  matchId=1000, playerId(slot): p1=1, p2=2
  Token p1: "a29ff97a..."  Token p2: "5b4b3976..."
  Redis:
    SET matchmaking:searching:2 = "searching"  TTL 120s
    SET matchmaking:searching:3 = "searching"  TTL 120s
    (sau khi match formed)
    DEL matchmaking:searching:2
    DEL matchmaking:searching:3
    SET matchmaking:token:a29ff97a... = "1000:1"  TTL 120s
    SET matchmaking:token:5b4b3976... = "1000:2"  TTL 120s
    SET matchmaking:match:1000 = "2,3"           TTL 600s
  Kafka publish: match.create {matchId:1000, players:[1,2],
    userIds:{"1":"2","2":"3"}, tokens:{"1":"a29ff97a...","2":"5b4b3976..."}}
  HTTP 200 trả về cả hai:
    {matchId:1000, serverHost:"10.11.1.177", serverPort:8080, playerId:1, token:"a29ff97a..."}
    {matchId:1000, serverHost:"10.11.1.177", serverPort:8080, playerId:2, token:"5b4b3976..."}
    ↓
[Game Server — Windows]
  Kafka consumer nhận match.create
  MatchManager::createMatch(cfg) → Match instance
  cfg.playerTokens = {1:"a29ff97a...", 2:"5b4b3976..."}
  Chờ UDP packet từ clients
    ↓
[Client 1 + 2] Gửi UDP packets đến 10.11.1.177:8080
  playerId + token trong packet header → resolvePlayer() verify
  NetworkManager (IOCP) nhận → routeCommand()
  Match::pushCommand() → command queue
    ↓
[Game Server — tick loop 60 Hz]
  tick(): swap queue → dispatch commands → physics → broadcastSnapshot()
  Snapshot S2C gửi về cho mỗi client
    ↓
[Game over: một tank HP=0 hoặc timeout]
  MatchManager::onMatchEnd() → KafkaProducer
  Publish: match.result {matchId, outcome, winnerId, durationSecs, kills}
    ↓
[History Service]
  Kafka consumer nhận match.result
  Tạo 2 MatchHistory records → lưu PostgreSQL
    ↓
[Client] GET /api/history/me → xem kết quả trận
         GET /api/history/leaderboard → bảng xếp hạng
```

### 5.3 Saga-4 — Duplicate Login & Session Cleanup

```
[Client B] POST /api/auth/login  (cùng account đã đăng nhập trên Client A)
    ↓
[Auth Service]
  1. Xóa session cũ của Client A khỏi Redis DB0
  2. Tạo session mới cho Client B
  3. Publish Kafka: user.session.invalidated
     {userId, code:1003, message:"Logged in from another device", timestamp}
    ↓
[Matchmaking Service — SessionInvalidatedKafkaListener]
  Nếu userId đang trong lobby:
    LobbyManager.removePlayer(userId)
    → future.completeExceptionally("Session invalidated")
    → HTTP request của Client A nhận lỗi, Unity hiển thị thông báo
    ↓
[Game Server — main.cpp KafkaConsumer]
  Nếu userId đang trong match:
    MatchManager::forceLogoutByUserId(userId, code=1003, msg, delay=10000ms)
    → Gửi disconnect packet sau 10s
    → Client A bị đá ra khỏi game
```

---

## 6. Technology Stack

| Layer | Công nghệ | Phiên bản |
|-------|-----------|-----------|
| Java microservices | Spring Boot | 3.x |
| Service discovery | Spring Cloud Eureka | — |
| API gateway | Spring Cloud Gateway | — |
| Authentication | Spring Security + JWT (JJWT) | — |
| ORM | Spring Data JPA / Hibernate | — |
| Cache | Spring Data Redis (Lettuce, RESP2) | — |
| Message broker (Java) | Spring Kafka | — |
| PostgreSQL driver | postgresql JDBC | — |
| MySQL driver | mysql-connector-j | — |
| Message broker | Apache Kafka | 7.3.0 (Confluent) |
| Game server | C++17, Windows IOCP (WinSock2) | MSVC 2022 |
| Kafka client (C++) | librdkafka | — |
| JSON (C++) | nlohmann/json | — |
| Build (Java) | Apache Maven | 3.9.9 |
| Build (C++) | MSBuild (Visual Studio 2022) | — |
| Container | Docker | — |
| Runtime (Java) | Java | 17 |
| OS (services) | WSL2 Ubuntu 22.04 | — |
| OS (game server) | Windows 11 | — |

---

## 7. Biến số cấu hình quan trọng

| Biến / Property | Giá trị | Ý nghĩa |
|-----------------|---------|---------|
| WSL2 eth0 IP | `172.25.203.168` | Kafka advertised listener — thay đổi sau reboot |
| Windows host IP (WSL2 portproxy) | `172.25.192.1` | Dùng khi test từ WSL2 |
| Windows WiFi IP | `10.11.1.177` | Game server UDP — mobile + PC cùng mạng dùng IP này |
| `KAFKA_BROKERS` (main.cpp default) | `172.25.203.168:9092` | Hardcoded vì WSL2 interop không truyền env var vào .exe |
| `BACKEND` | `iocp` | Network backend game server: `iocp` hoặc `blocking` |
| `UDP_PORT` | `8080` | Port UDP game server |
| `jwt.secretKey` | `MySuperSecretKey12345678901234567890` | Dùng chung giữa auth-service và api-gateway |
| `tank.server.host` | `10.11.1.177` | Địa chỉ server trả về trong `/find` response |
| `tank.server.udp-port` | `8080` | Port UDP trả về trong `/find` response |

**Lưu ý:** Nếu WSL2 IP thay đổi sau reboot:
1. Cập nhật `KAFKA_ADVERTISED_LISTENERS` trong lệnh `docker run kafka` → restart kafka container
2. Cập nhật `KAFKA_BROKERS` default trong `main.cpp` → rebuild C++ server
3. Cập nhật `spring.kafka.bootstrap-servers` trong `application.yaml` của các Java service → rebuild JAR

---

## 8. Sơ đồ phụ thuộc Service

```
                    ┌─────────────┐
                    │   Eureka    │ ← tất cả service đăng ký
                    │   :8761     │
                    └──────┬──────┘
                           │ service discovery
          ┌────────────────┼────────────────┐
          │                │                │
    ┌─────▼──────┐   ┌─────▼──────┐  ┌─────▼──────────────┐
    │  Gateway   │   │    Auth    │  │    Matchmaking     │
    │  :8080     │──▶│   :8081   │  │      :8085         │
    │            │   │ PostgreSQL │  │  Redis DB1         │
    └────────────┘   │ Redis DB0  │  │  Kafka producer    │
                     └──────┬─────┘  │  Kafka consumer ←─┐│
                            │        └────────┬───────────┘│
                   user.session.invalidated   │             │
                            │           match.create        │
                            ▼                 ▼             │
                    ┌───────────────────Kafka :9092──────┐  │
                    │   match.create  /  match.result    │  │
                    │   user.session.invalidated         │  │
                    └──────┬──────────────────┬──────────┘  │
                           │ match.create     │ match.result │
                           ▼                  ▼              │
                ┌────────────────┐  ┌──────────────┐         │
                │  Game Server   │  │   History    │         │
                │ Win :8080 UDP  │  │    :8086     │         │
                │ C++ IOCP       │  │  PostgreSQL  │         │
                │ Kafka consumer │  └──────────────┘         │
                │ (match.create +│                           │
                │  session.inval)│                           │
                └────────────────┘                           │
                     │ user.session.invalidated ─────────────┘
                       (cùng topic, 2 consumer groups)

    ┌──────────┐    ┌─────────────┐
    │  Shop    │    │  Monitoring │
    │  :8088   │    │    :8090    │
    │  MySQL   │    │             │
    └──────────┘    └─────────────┘
```
