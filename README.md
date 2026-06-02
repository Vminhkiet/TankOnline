# TankOnline — SE315.Q21

Game bắn tank online nhiều người chơi, kiến trúc microservices + game server C++ + Unity client.

---

## Mục lục

1. [Kiến trúc tổng quan](#kiến-trúc-tổng-quan)
2. [Yêu cầu hệ thống](#yêu-cầu-hệ-thống)
3. [Cài đặt môi trường](#cài-đặt-môi-trường)
4. [Cấu hình IP](#cấu-hình-ip)
5. [Khởi động nhanh](#khởi-động-nhanh)
6. [Khởi động thủ công](#khởi-động-thủ-công)
7. [Benchmark & Đo hiệu năng](#benchmark--đo-hiệu-năng)
8. [Dừng hệ thống](#dừng-hệ-thống)
9. [Logs & Monitoring](#logs--monitoring)
10. [Troubleshooting](#troubleshooting)

---

## Kiến trúc tổng quan

```
┌─────────────────────────────────────────────────────────────────────┐
│  WINDOWS HOST                                                        │
│                                                                     │
│  Unity Client (Tank Legends.exe)                                    │
│    HTTP  → API Gateway :8080          (đăng nhập, matchmaking)      │
│    UDP   → Game Server :8080          (gameplay realtime)           │
│                                                                     │
│  C++ Game Server (server_tank.exe)          [IOCP, 60Hz tick]       │
│    UDP  ← Unity clients              (nhận input, gửi snapshot)    │
│    Kafka → WSL2 :9092                (match.create / match.result)  │
│                                                                     │
│  anticheat.exe   — scan process + memory, ban qua HTTP              │
│  Admin Web       — Vite+React :5173, quản lý user/shop/leaderboard  │
└──────────────────────────────────┬──────────────────────────────────┘
                                   │ WSL2 bridge
┌──────────────────────────────────▼──────────────────────────────────┐
│  WSL2 (Ubuntu)                                                      │
│                                                                     │
│  Docker:  Zookeeper:2181  Kafka:9092  PostgreSQL:5432               │
│           Redis:6379       MySQL:3307                               │
│                                                                     │
│  Java (Spring Boot):                                                │
│    Eureka          :8761   API Gateway  :8080   auth_service :8081  │
│    matchmaking     :8085   history      :8086   profile      :8087  │
│    shop            :8088   monitoring   :8090                       │
│                                                                     │
│  Monitoring:  Prometheus:9090   Grafana:3000                        │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Yêu cầu hệ thống

| Thành phần | Yêu cầu |
|-----------|---------|
| OS | Windows 10/11 + WSL2 (Ubuntu 20.04+) |
| CPU | ≥ 4 core (khuyến nghị 8+ core cho benchmark) |
| RAM | ≥ 8 GB |
| Disk | ≥ 10 GB trống |
| Java | JDK 17+ |
| Maven | 3.9+ |
| Docker | Docker Desktop hoặc Docker Engine trong WSL2 |
| Visual Studio | 2022 Community (C++ build tools, MSBuild) |
| Node.js | v18+ (cho Admin Web) |
| Python | 3.8+ |

---

## Cài đặt môi trường

### 1. WSL2

```powershell
# Chạy PowerShell với quyền Admin
wsl --install -d Ubuntu
wsl --set-default-version 2
```

### 2. Docker

```bash
# Trong WSL2
sudo apt update && sudo apt install -y docker.io docker-compose
sudo usermod -aG docker $USER
newgrp docker
```

Kiểm tra:
```bash
docker run hello-world
```

### 3. Java & Maven

```bash
# Java 17
sudo apt install -y openjdk-17-jdk
java -version   # phải thấy openjdk 17

# Maven — tải bản 3.9.x
wget https://dlcdn.apache.org/maven/maven-3/3.9.9/binaries/apache-maven-3.9.9-bin.tar.gz -P /tmp
tar -xzf /tmp/apache-maven-3.9.9-bin.tar.gz -C ~/Downloads/
# Sửa MVN path trong start.sh nếu cần
```

### 4. Visual Studio 2022

Cài **Visual Studio 2022 Community** với workload:
- **Desktop development with C++**
- MSBuild tools
- Windows SDK (10.0.x)

### 5. Node.js (Admin Web)

```bash
# Dùng nvm
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.0/install.sh | bash
source ~/.bashrc
nvm install 20
node -v   # v20.x.x
```

Cài dependencies Admin Web:
```bash
cd "Tank Legends Management Web"
npm install
```

### 6. Python packages

```bash
pip3 install prometheus-client requests
```

### 7. Prometheus & Grafana (Docker)

```bash
cd monitoring
bash start_monitoring_stack.sh
# Grafana: http://localhost:3000  (admin/admin)
# Prometheus: http://localhost:9090
```

---

## Cấu hình IP

> **QUAN TRỌNG:** WSL2 IP thay đổi sau mỗi lần reboot Windows. Phải cập nhật trước khi chạy.

### Lấy IP hiện tại

```bash
ip addr show eth0 | grep "inet " | awk '{print $2}' | cut -d/ -f1
```

### Cập nhật IP tự động

`start.sh` tự động patch IP vào `main.cpp` và `anticheat.cpp`. Tuy nhiên nếu chạy thủ công:

```bash
# Xem IP_CONFIG.md để biết tất cả chỗ cần đổi
cat IP_CONFIG.md
```

Các file cần cập nhật thủ công:

| File | Biến cần đổi |
|------|-------------|
| `Tank/server_tank/src/main.cpp` | `getEnv("KAFKA_BROKERS", "<WSL2_IP>:9092")` |
| `tools/anticheat/anticheat.cpp` | `AUTH_HOST = "<WSL2_IP>"` |
| `start.sh` | Tự động patch (không cần đổi tay) |

---

## Khởi động nhanh

```bash
# Khởi động toàn bộ hệ thống (Docker + Java + Tank server + Admin Web)
./start.sh
```

Script sẽ tự động:
1. Kill các process cũ
2. Khởi động Docker: Zookeeper → Kafka → PostgreSQL → Redis → MySQL
3. Tạo Kafka topics: `match.create`, `match.result`, `match.cancel`
4. Tạo PostgreSQL databases
5. Build Java JARs (nếu chưa có)
6. Khởi động Eureka → các service còn lại
7. Patch IP, build và chạy `server_tank.exe`
8. Khởi động Admin Web (:5173)

**Dừng hệ thống:**
```bash
./stop.sh
```

---

## Khởi động thủ công

### Bước 1: Docker containers

```bash
WSL2_IP=$(ip addr show eth0 | grep "inet " | awk '{print $2}' | cut -d/ -f1)

docker run -d --name zookeeper \
  -e ZOOKEEPER_CLIENT_PORT=2181 -e ZOOKEEPER_TICK_TIME=2000 \
  -p 2181:2181 confluentinc/cp-zookeeper:7.3.0

docker run -d --name kafka --hostname kafka --link zookeeper:zookeeper \
  -p 9092:9092 \
  -e KAFKA_BROKER_ID=1 \
  -e KAFKA_ZOOKEEPER_CONNECT=zookeeper:2181 \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT \
  -e KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:29092,PLAINTEXT_HOST://0.0.0.0:9092 \
  -e "KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://kafka:29092,PLAINTEXT_HOST://${WSL2_IP}:9092" \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  confluentinc/cp-kafka:7.3.0

docker run -d --name postgres_container \
  -e POSTGRES_DB=auth_service_db -e POSTGRES_USER=auth_user \
  -e POSTGRES_PASSWORD=userpassword -p 5432:5432 postgres:16.0

docker run -d --name redis \
  -p 6379:6379 redis:alpine redis-server --requirepass 123456

docker run -d --name mysql_container \
  -e MYSQL_DATABASE=shoptank_db -e MYSQL_ROOT_PASSWORD=Ts171201@ \
  -p 3307:3306 mysql:8.0
```

### Bước 2: Kafka topics

```bash
# Đợi ~15s cho Kafka khởi động
sleep 15
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.create  --partitions 1 --replication-factor 1
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.result  --partitions 1 --replication-factor 1
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.cancel  --partitions 1 --replication-factor 1
```

### Bước 3: Java services

```bash
BASE=/home/minhk/project/new/SE315.Q21/BE_CNGOL/java-meta-services
MVN=/mnt/c/Users/minhk/Downloads/apache-maven-3.9.9/bin/mvn

# Build (chỉ cần làm 1 lần)
cd $BASE && $MVN package -DskipTests -q

# Start Eureka trước, đợi UP
java -jar $BASE/discovery_service/target/discovery_service-0.0.1-SNAPSHOT.jar \
  > /tmp/eureka.log 2>&1 &
sleep 20

# Start các service còn lại
java -jar $BASE/auth_service/target/auth-service-0.0.1-SNAPSHOT.jar          > /tmp/auth.log        2>&1 &
java -jar $BASE/api_gateway/target/api_gateway-0.0.1-SNAPSHOT.jar             > /tmp/gateway.log     2>&1 &
java -jar $BASE/matchmaking_service/target/matchmaking_service-0.0.1-SNAPSHOT.jar > /tmp/matchmaking.log 2>&1 &
java -jar $BASE/history_service/target/history_service-0.0.1-SNAPSHOT.jar     > /tmp/history.log     2>&1 &
java -jar $BASE/profile_service/target/profile_service-0.0.1-SNAPSHOT.jar     > /tmp/profile.log     2>&1 &
java -jar $BASE/shop/target/shop_service-0.0.1-SNAPSHOT.jar                   > /tmp/shop.log        2>&1 &
java -jar $BASE/monitoring_service/target/monitoring_service-0.0.1-SNAPSHOT.jar > /tmp/monitoring.log 2>&1 &
```

Kiểm tra:
```bash
curl -s http://localhost:8761/actuator/health   # Eureka
curl -s http://localhost:8080/actuator/health   # Gateway
```

### Bước 4: Tank server (Windows)

```bash
# Build
cmd.exe /c "D:\\Unity\\TankOnline\\game\\SE315.Q21\\Tank\\build_server_tank.bat"

# Chạy
python3 -c "
import subprocess
exe = '/mnt/d/Unity/TankOnline/game/SE315.Q21/Tank/out/build/x64-Release/server_tank/Release/server_tank.exe'
cwd = '/mnt/d/Unity/TankOnline/game/SE315.Q21/Tank/out/build/x64-Release/server_tank/Release'
p = subprocess.Popen([exe, '--backend=iocp'], cwd=cwd,
                     stdout=open(cwd+'/server.log','w'), stderr=subprocess.STDOUT)
print('Tank server PID:', p.pid)
"
```

### Bước 5: Admin Web

```bash
cd "Tank Legends Management Web"
npm run dev -- --host
# Mở: http://<WSL2_IP>:5173
# Login: admin / admin123
```

---

## Benchmark & Đo hiệu năng

### Chạy Real GPC benchmark

```bash
# Cú pháp
./run_real_gpc.sh [backend] [players/match] [số match] [thời gian giây] [rebuild]

# Ví dụ
./run_real_gpc.sh iocp 5 10 300          # 10 match × 5 player, 5 phút
./run_real_gpc.sh iocp 5 20 300          # 20 match × 5 player
./run_real_gpc.sh blocking 5 10 300       # so sánh blocking backend
./run_real_gpc.sh iocp rebuild 5 10 300  # rebuild trước khi chạy
```

**Luồng tự động:**
1. Phase 1: Benchmark baseline (bench_wc_live.exe, ~45s, core sạch)
2. Phase 2: Real server (server_tank.exe + 100 load_client instances)
3. Kết quả live trên Grafana: `http://localhost:3000/d/wc-live-bench/`

**Chỉ số cần quan tâm:**

| Metric | Bình thường | Cảnh báo | Nguy hiểm |
|--------|------------|---------|----------|
| `budget_pct` | < 30% | 30–70% | > 80% |
| `overrun_pct` | 0% | > 0.1% | > 1% |
| `pool_pending` | 0 | > 0 liên tục | tăng dần |

### Tìm điểm gãy

```bash
./run_real_gpc.sh iocp 5 20  120
./run_real_gpc.sh iocp 5 40  120
./run_real_gpc.sh iocp 5 80  120
./run_real_gpc.sh iocp 5 100 120   # điểm gãy ~100 match trên laptop 4-core
```

### Worst-case benchmark độc lập

```bash
# Benchmark in-process (không cần server chạy)
./run_wc_benchmark.sh

# Hoặc Windows
run_wc_benchmark.bat
```

---

## Dừng hệ thống

```bash
./stop.sh
```

Hoặc thủ công:
```bash
pkill -f "java -jar"
docker rm -f zookeeper kafka postgres_container redis mysql_container
powershell.exe -Command "Stop-Process -Name server_tank -Force -ErrorAction SilentlyContinue"
```

---

## Logs & Monitoring

### Service logs

| Service | Log file |
|---------|---------|
| Eureka | `/tmp/eureka.log` |
| Auth | `/tmp/auth.log` |
| API Gateway | `/tmp/gateway.log` |
| Matchmaking | `/tmp/matchmaking.log` |
| History | `/tmp/history.log` |
| Profile | `/tmp/profile.log` |
| Shop | `/tmp/shop.log` |
| Tank server | `<build_dir>/server.log` |
| Admin Web | `/tmp/admin.log` |

### Grafana dashboards

URL: `http://localhost:3000` (admin/admin)

| Dashboard | Nội dung |
|-----------|---------|
| Worst-Case Live Benchmark | GPC, P99, overruns, bullet count real-time |
| Benchmark vs Production | So sánh benchmark lý thuyết vs real server |

### Prometheus metrics

| Port | Agent | Nội dung |
|------|-------|---------|
| `:9103` | bench_wc_live_agent | GPC, P99, budget từ benchmark |
| `:9104` | real_gpc_agent | GPC, P99, overrun từ production |

---

## Troubleshooting

### Kafka `INVALID_REPLICATION_FACTOR`
```bash
# Restart kafka với --hostname kafka
docker rm -f kafka
# Chạy lại lệnh docker run kafka ở trên
```

### Login trả HTTP 403 (BCrypt mismatch)
```python
import bcrypt, subprocess
new_hash = bcrypt.hashpw(b'password123', bcrypt.gensalt(rounds=10)).decode()
sql = f"UPDATE users SET password = '{new_hash}' WHERE username IN ('player1','player2');"
subprocess.run(['docker','exec','postgres_container','psql',
                '-U','auth_user','-d','auth_service_db','-c', sql], check=True)
```

### WSL2 IP thay đổi sau reboot
```bash
# Chỉ cần chạy lại start.sh — script tự patch IP
./start.sh
```

### Tank server không nhận Kafka
Kafka broker hardcode trong `main.cpp`:
```cpp
const std::string kafkaBrokers = getEnv("KAFKA_BROKERS", "<WSL2_IP>:9092");
```
Sửa IP và rebuild, hoặc để `start.sh` tự patch.

### Grafana không hiện data
```bash
# Kiểm tra agents đang chạy
curl -s http://localhost:9104/metrics/prometheus | grep real_gpc
curl -s http://localhost:9103/metrics/prometheus | grep wc_gpc

# Đổi time range sang "Last 30 minutes"
```

### `docker-compose up` lỗi `KeyError: ContainerConfig`
Đây là bug của docker-compose v1.29.2. Dùng `docker run` riêng từng container như hướng dẫn ở trên.

### Benchmark hiện `matches=0`
Exe chưa được rebuild sau khi sửa code:
```bash
./run_real_gpc.sh iocp rebuild 5 10 300
```

---

## Cấu trúc thư mục

```
SE315.Q21/
├── start.sh                          # Khởi động toàn bộ hệ thống
├── stop.sh                           # Dừng toàn bộ hệ thống
├── run_real_gpc.sh                   # Benchmark Real GPC
├── run_wc_benchmark.sh               # Worst-case benchmark
├── IP_CONFIG.md                      # Hướng dẫn đổi IP
│
├── BE_CNGOL/
│   └── java-meta-services/
│       ├── discovery_service/        # Eureka :8761
│       ├── api_gateway/              # Spring Cloud Gateway :8080
│       ├── auth_service/             # Auth :8081
│       ├── matchmaking_service/      # Matchmaking :8085
│       ├── history_service/          # Lịch sử :8086
│       ├── profile_service/          # Profile :8087
│       ├── shop/                     # Shop :8088
│       └── monitoring_service/       # Spring Boot Admin :8090
│
├── Tank/
│   ├── server_tank/                  # C++ game server source
│   │   ├── src/
│   │   │   ├── main.cpp              # Entry point
│   │   │   ├── Core/                 # Match, MatchScheduler
│   │   │   └── Network/              # IOCP, BlockingBackend
│   │   └── include/
│   ├── load_client/                  # UDP load client (benchmark)
│   ├── bench_worst_case/             # In-process benchmark
│   ├── real_gpc_agent.py             # Parse [Real_GPC] → Prometheus :9104
│   └── bench_wc_live_agent.py        # Parse [WC_Final] → Prometheus :9103
│
├── tools/
│   ├── anticheat/anticheat.cpp       # AntiCheat client
│   └── tank_hp_hack.cpp              # ESP demo (educational)
│
├── Tank Legends Management Web/      # Admin frontend (Vite + React)
└── monitoring/
    ├── prometheus.yml                # Prometheus scrape config
    ├── grafana_wc_live_dashboard.json
    └── start_monitoring_stack.sh
```

---

## Tài khoản mặc định

| Service | Username | Password |
|---------|---------|---------|
| Admin Web | admin | admin123 |
| Grafana | admin | admin |
| PostgreSQL | auth_user | userpassword |
| Redis | — | 123456 |
| MySQL root | root | Ts171201@ |
