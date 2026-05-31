# TankOnline — SE315.Q21

Game bắn tank online nhiều người chơi, kiến trúc microservices + game server C++ + Unity client.

---

## Kiến trúc tổng quan

```
┌─────────────────────────────────────────────────────────────────────┐
│  WINDOWS HOST (192.168.x.x WiFi)                                    │
│                                                                     │
│  Unity Client (Tank Legends.exe)                                    │
│    HTTP  → API Gateway :8080          (đăng nhập, matchmaking)      │
│    UDP   → Game Server :8080          (gameplay realtime)           │
│                                                                     │
│  C++ Game Server (server_tank.exe)                                  │
│    UDP  ← Unity clients              (nhận input, gửi snapshot)    │
│    Kafka → WSL2 :9092                (match.create / match.result)  │
│                                                                     │
│  anticheat.exe (Admin)                                              │
│    ReadProcessMemory ← Unity.exe     (scan JWT, matchId, handles)  │
│    HTTP  → API Gateway :8080         (ban user, cancel match)       │
│                                                                     │
│  Admin Web (browser → :5173)                                        │
│    HTTP  → API Gateway :8080         (quản lý user, lịch sử)       │
└──────────────────────────────────┬──────────────────────────────────┘
                                   │ WSL2 bridge (172.25.x.x)
┌──────────────────────────────────▼──────────────────────────────────┐
│  WSL2 (Ubuntu)                                                      │
│                                                                     │
│  Docker:                                                            │
│    Zookeeper       :2181                                            │
│    Kafka           :9092    ←→ tất cả Java services + C++ server   │
│    PostgreSQL      :5432    ← auth, history, profile                │
│    Redis           :6379    ← matchmaking, history, shop, profile   │
│    MySQL           :3307    ← shop                                  │
│                                                                     │
│  Java Services (Spring Boot):                                       │
│    Eureka          :8761    (service discovery)                     │
│    API Gateway     :8080    (entry point duy nhất cho client)       │
│    auth_service    :8081                                            │
│    matchmaking     :8085                                            │
│    history         :8086                                            │
│    profile         :8087                                            │
│    shop            :8088                                            │
│    monitoring      :8090                                            │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Luồng hoạt động

### 1. Đăng nhập

```
Unity → POST /api/auth/login  { username, password }
           ↓
      API Gateway → auth_service
           ↓
      Trả về { jwt, refreshToken }
           ↓
      Unity lưu JWT, dùng cho mọi request sau
```

**auth_service** dùng **BCrypt** để hash password, **JWT (HS256)** cho token.  
JWT payload chứa `userId`, `authorities`, `iat`, `exp` (15 phút TTL).

---

### 2. Tìm trận (Matchmaking)

```
Player1 + Player2 cùng gọi:
POST /api/matchmaking/find  (Authorization: Bearer JWT)
           ↓
      matchmaking_service thêm vào LobbyManager
           ↓
      Khi đủ 2 người → tạo match:
        - Gán slotId (1, 2, ...)
        - Tạo token cho mỗi người
        - Publish Kafka: match.create → C++ server
           ↓
      C++ server nhận, tạo match trong memory
        - Publish Kafka: match.ready → matchmaking_service
           ↓
      matchmaking_service complete CompletableFuture của 2 player
           ↓
      Cả 2 player nhận response:
        { matchId, serverHost, serverPort, playerId }
```

**Redis** lưu trạng thái tìm trận (`matchmaking:searching:{userId}`) với TTL 120s.  
Timeout ACK là 30s — nếu C++ server không trả `match.ready` → trả HTTP 503.

---

### 3. Gameplay (UDP Realtime)

```
Unity → UDP packet đến C++ server (serverHost:serverPort)

Packet đầu tiên từ mỗi player:
  server.resolvePlayer() → spawn tank tại vị trí spawn
  
Mỗi 50ms (20Hz), server broadcast:
  S2C_SNAPSHOT → tất cả player trong match
    Header: matchId | opcode=2000 | tick | tankCount | localPlayerId | timeLeft
    Body:   [TankState × n] [BulletState × m] [ItemState × k]

TankState (31 bytes, packed):
  tankId(4) x(4) y(4) z(4) yaw(4) turretYaw(4) health(2) flags(1) score(2) placement(1) bushRegion(1)

Client gửi lên:
  C2S_MOVE  (opcode 1001) — hướng di chuyển (bit-packed)
  C2S_SHOOT (opcode 1002) — bắn đạn (bit-packed)
  C2S_PING  (opcode 1003) — keep-alive
```

C++ server dùng **IOCP** (Windows I/O Completion Port) làm network backend, **60Hz tick rate**.  
Physics: AABB collision detection, UniformGrid cho spatial query.

---

### 4. Kết thúc trận

```
C++ server (khi có người chết hoặc timeout):
  broadcast S2C_MATCH_END → tất cả player
  publish Kafka: match.result {
    matchId, outcome, winnerId, durationSecs, kills, deaths, userIds
  }
       ↓ fan-out (2 consumer groups độc lập)
       │
       ├─→ history_service: lưu MatchHistory vào PostgreSQL
       │                    cập nhật leaderboard ZSET trong Redis
       │
       └─→ profile_service: cộng RP, cập nhật win/loss stats
```

**Outcome có thể là:** `win` | `draw` | `timeout` | `cheat_void`  
Nếu outcome = `cheat_void` → history_service bỏ qua, không lưu lịch sử.

---

### 5. AntiCheat

```
anticheat.exe chạy liên tục (2s/lần scan):

[1] Handle Scan:
    NtQuerySystemInformation → lấy toàn bộ handle trong hệ thống
    → tìm process nào đang giữ PROCESS_VM_READ handle đến Tank Legends.exe
    → không phải whitelist → DETECTED

[2] Known Cheat Scan:
    Snapshot tất cả process đang chạy
    → khớp tên với danh sách cheat đã biết (tank_hp_hack.exe, cheatengine, x64dbg...)
    → DETECTED

Khi phát hiện cheat:
    ReadProcessMemory(Tank Legends.exe):
      scanJwtUserId()  → quét memory tìm chuỗi JWT "eyJ..." → decode base64 → lấy userId
      scanMatchId()    → quét memory tìm snapshot header (opcode=2000) → lấy matchId

    POST /api/user/anticheat/ban      { userId, reason }  (Header: X-Anticheat-Key)
    POST /api/matchmaking/admin/cancel-cheat { matchId, reason }
         ↓
    matchmaking_service publish Kafka: match.cancel { matchId }
         ↓
    C++ server nhận → cancelMatch(matchId) → broadcast S2C_MATCH_END (cheat_void)
         ↓
    match.result { outcome: "cheat_void" } → history bỏ qua
```

**Whitelist:** explorer.exe, svchost.exe, taskmgr.exe, dwm.exe, gamebar.exe, msmpeng.exe, ...

---

### 6. ESP Cheat Tool (Demo)

```
tank_hp_hack.exe (Admin):
  Attach vào Tank Legends.exe
  
  fullScan() mỗi 50ms:
    Quét tất cả memory region (PAGE_READWRITE | PAGE_EXECUTE_READWRITE)
    Tìm pattern: bytes[+4..+5] = 0x07D0 (opcode 2000)
    parseStrict() validate:
      - matchId != 0, tankCount in [1..8], localId != 0
      - HP >= 0, vị trí isfinite() và trong bounds map
      - localId phải có trong danh sách tank (foundLocal)
    betterSnap() chọn snapshot tốt nhất: ưu tiên matchId cao hơn, tie-break bằng tick
  
  Overlay GDI (WS_EX_TOPMOST | WS_EX_LAYERED):
    Minimap: dot đỏ = enemy, dot xanh = bản thân
    HP số màu vàng bên cạnh enemy
    Line từ tâm đến enemy trên minimap
  
  F8: toggle hiện/ẩn overlay
  ESC: thoát
```

---

### 7. Admin Web

```
Vite + React, chạy tại localhost:5173

Tính năng:
  - Đăng nhập bằng tài khoản ROLE_ADMIN
  - Danh sách người chơi: xem thông tin, ban/unban
  - Leaderboard: top player theo kills (từ Redis ZSET)
  - Lịch sử trận đấu: tra cứu theo userId
  - Quản lý shop: thêm/sửa/xóa item
  - Gift code: tạo, kích hoạt vĩnh viễn, xóa
```

---

## Kafka Topics

| Topic | Producer | Consumer | Mục đích |
|-------|----------|----------|---------|
| `match.create` | matchmaking_service | C++ server | Tạo match mới trong game server |
| `match.ready` | C++ server | matchmaking_service | ACK match đã sẵn sàng |
| `match.result` | C++ server | history_service, profile_service | Kết quả trận đấu |
| `match.cancel` | matchmaking_service | C++ server | Hủy match (anticheat) |
| `user.created` | auth_service | profile_service | Tạo profile sau đăng ký |
| `user.profile.failed` | profile_service | auth_service | Compensation: xóa user mồ côi |
| `user.session.invalidated` | auth_service | matchmaking_service, C++ server | Kick player bị ban/duplicate login |
| `game.perf` | C++ server | monitoring_service | Metrics hiệu năng game server |

---

## Cơ sở dữ liệu

| Service | DB | Bảng chính |
|---------|-----|-----------|
| auth_service | PostgreSQL :5432 `auth_service_db` | `users` (id, username, password BCrypt, email, role, is_banned) |
| history_service | PostgreSQL :5432 `history_service_db` | `match_history` (matchId, playerId, result, kills, deaths, duration) |
| profile_service | PostgreSQL :5432 `profile_service_db` | `profiles` (userId, displayName, rp, wins, losses) |
| shop | MySQL :3307 `shoptank_db` | `items`, `purchases`, `player_items` |
| matchmaking | Redis :6379 DB1 | Keys: `matchmaking:searching:{id}`, `matchmaking:token:{token}`, `matchmaking:match:{id}` |
| history | Redis :6379 | ZSET `leaderboard:kills` (userId → totalKills) |
| shop | Redis :6379 | Cache |

---

## Service Discovery & Load Balancing

**Eureka** (:8761) làm service registry. Tất cả Java service tự đăng ký khi khởi động.  
API Gateway dùng **Spring Cloud LoadBalancer** (`lb://service-name`) để resolve địa chỉ từ Eureka.  
Shop service được route trực tiếp bằng IP thay vì Eureka do cấu hình riêng.

---

## Bảo mật

| Cơ chế | Chi tiết |
|--------|---------|
| JWT (HS256) | TTL 15 phút, secret key trong application.yaml |
| BCrypt (rounds=10) | Hash password trong PostgreSQL |
| `X-Gateway-Origin` header | Gateway thêm header secret vào request đến shop, matchmaking, profile |
| `X-Anticheat-Key` header | anticheat.exe xác thực khi gọi ban/cancel API |
| Role-based | `ROLE_USER` và `ROLE_ADMIN` |
| Ban mechanism | `is_banned=true` → login trả 403, kick session qua Kafka |

---

## Khởi động hệ thống

```bash
# WSL2
./start.sh

# Thứ tự khởi động tự động:
# 1. Docker: Zookeeper → Kafka → PostgreSQL → Redis → MySQL
# 2. Kafka topics: match.create, match.result, match.cancel
# 3. Build Java JARs nếu chưa có
# 4. Eureka → (auth, gateway, matchmaking, history, profile, shop, monitoring)
# 5. Build C++ server (CMake + MSBuild) → chạy server_tank.exe
```

```bash
# Cheat tool demo
bash tools/run_cheat.sh

# AntiCheat
bash tools/anticheat/run_anticheat.sh

# Admin Frontend
cd "Tank Legends Management Web" && npm run dev -- --host
# Mở: http://172.25.203.168:5173  (hoặc localhost:5173 từ Windows)
# Login: admin / admin123
```

---

## Cấu trúc thư mục

```
SE315.Q21/
├── start.sh                          # Khởi động toàn bộ hệ thống
├── IP_CONFIG.md                      # Hướng dẫn đổi IP khi đổi WiFi
├── BE_CNGOL/
│   ├── SAGA_PATTERN.md               # Tài liệu Saga pattern
│   └── java-meta-services/
│       ├── discovery_service/        # Eureka :8761
│       ├── api_gateway/              # Spring Cloud Gateway :8080
│       ├── auth_service/             # Đăng nhập, đăng ký, ban :8081
│       ├── matchmaking_service/      # Tìm trận :8085
│       ├── history_service/          # Lịch sử trận đấu :8086
│       ├── profile_service/          # RP, stats người chơi :8087
│       ├── shop/                     # Cửa hàng item :8088
│       └── monitoring_service/       # Spring Boot Admin :8090
├── Tank/
│   └── server_tank/                  # C++ UDP game server (Windows .exe)
│       └── src/main.cpp              # Entry point: Kafka + IOCP + MatchScheduler
├── tools/
│   ├── tank_hp_hack.cpp              # ESP cheat tool (demo)
│   ├── run_cheat.sh                  # Build + chạy cheat tool
│   ├── anticheat/
│   │   ├── anticheat.cpp             # AntiCheat: handle scan + process scan
│   │   └── run_anticheat.sh          # Build + chạy anticheat
│   └── anticheat_km/                 # Kernel-mode anticheat (demo flag file)
├── Tank Legends Management Web/      # Admin frontend (Vite + React)
└── monitoring/
    └── prometheus.yml                # Prometheus scrape config
```
