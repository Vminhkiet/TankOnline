# Hướng Dẫn Chạy Hệ Thống TankOnline

**Môi trường:** WSL2 (Ubuntu) + Windows Host  
**WSL2 IP hiện tại:** `172.25.203.168` *(reset mỗi lần reboot — xem bước 0)*  
**Windows Host IP:** `172.25.192.1`

---

## Bước 0 — Kiểm tra IP sau khi reboot

```bash
WSL2_IP=$(ip addr show eth0 | grep "inet " | awk '{print $2}' | cut -d/ -f1)
echo $WSL2_IP
```

Nếu IP thay đổi, cập nhật:
1. `KAFKA_ADVERTISED_LISTENERS` trong lệnh `docker run kafka` bên dưới
2. Hardcode default trong `Tank/server_tank/src/main.cpp` → rebuild

---

## Bước 1 — Start Docker Services

```bash
# Xoá container cũ nếu còn
docker rm -f zookeeper kafka postgres_container redis mysql_container 2>/dev/null

WSL2_IP=$(ip addr show eth0 | grep "inet " | awk '{print $2}' | cut -d/ -f1)

# Zookeeper
docker run -d --name zookeeper \
  -e ZOOKEEPER_CLIENT_PORT=2181 -e ZOOKEEPER_TICK_TIME=2000 \
  -p 2181:2181 confluentinc/cp-zookeeper:7.3.0

# Kafka
docker run -d --name kafka --hostname kafka --link zookeeper:zookeeper \
  -p 9092:9092 \
  -e KAFKA_BROKER_ID=1 \
  -e KAFKA_ZOOKEEPER_CONNECT=zookeeper:2181 \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT \
  -e KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:29092,PLAINTEXT_HOST://0.0.0.0:9092 \
  -e "KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://kafka:29092,PLAINTEXT_HOST://${WSL2_IP}:9092" \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  -e KAFKA_AUTO_CREATE_TOPICS_ENABLE=true \
  confluentinc/cp-kafka:7.3.0

# Postgres
docker run -d --name postgres_container \
  -e POSTGRES_DB=auth_service_db -e POSTGRES_USER=auth_user -e POSTGRES_PASSWORD=userpassword \
  -p 5432:5432 postgres:16.0

# Redis
docker run -d --name redis \
  -p 6379:6379 redis:alpine redis-server --requirepass 123456

# MySQL
docker run -d --name mysql_container \
  -e MYSQL_DATABASE=shoptank_db -e MYSQL_ROOT_PASSWORD=Ts171201@ \
  -p 3307:3306 mysql:8.0
```

### Tạo Kafka topics (chờ ~15s sau khi kafka start)

```bash
sleep 15
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.create --partitions 1 --replication-factor 1
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.result --partitions 1 --replication-factor 1
```

---

## Bước 2 — Start Prometheus + Grafana

```bash
# Prometheus
docker start prometheus 2>/dev/null || docker run -d --name prometheus \
  -p 9090:9090 \
  -v /home/minhk/project/SE315.Q21/monitoring/prometheus.yml:/etc/prometheus/prometheus.yml \
  prom/prometheus

# Grafana image renderer
docker start renderer 2>/dev/null || docker run -d --name renderer \
  -p 8082:8081 grafana/grafana-image-renderer:latest

# Grafana
docker start grafana 2>/dev/null || docker run -d --name grafana \
  -p 3000:3000 \
  -e GF_RENDERING_SERVER_URL="http://172.25.203.168:8082/render" \
  -e GF_RENDERING_CALLBACK_URL="http://172.25.203.168:3000/" \
  grafana/grafana:latest
```

**Grafana UI:** `http://localhost:3000` (admin / admin)  
**Dashboard C++ Server:** `http://localhost:3000/d/cpp-game-server`

> Nếu dashboard bị mất (container recreate): import lại từ `monitoring/grafana_dashboard.json`
> ```bash
> curl -s -u admin:admin -X POST -H 'Content-Type: application/json' \
>   -d "{\"dashboard\":$(cat monitoring/grafana_dashboard.json),\"overwrite\":true}" \
>   http://localhost:3000/api/dashboards/import
> ```

---

## Bước 3 — Start Java Services

```bash
BASE=/home/minhk/project/SE315.Q21/BE_CNGOL/java-meta-services

# Khởi động Eureka trước
java -jar "$BASE/discovery_service/target/discovery_service-0.0.1-SNAPSHOT.jar" > /tmp/eureka.log 2>&1 &
sleep 20

# Các service còn lại
java -jar "$BASE/auth_service/target/auth-service-0.0.1-SNAPSHOT.jar"        > /tmp/auth.log 2>&1 &
java -jar "$BASE/api_gateway/target/api_gateway-0.0.1-SNAPSHOT.jar"           > /tmp/gateway.log 2>&1 &
java -jar "$BASE/matchmaking_service/target/matchmaking_service-0.0.1-SNAPSHOT.jar" > /tmp/matchmaking.log 2>&1 &
sleep 30

# Kiểm tra
curl -s http://localhost:8761/actuator/health   # Eureka   :8761
curl -s http://localhost:8080/actuator/health   # Gateway  :8080
```

| Service | Port | Log |
|---------|------|-----|
| Eureka | 8761 | `/tmp/eureka.log` |
| API Gateway | 8080 | `/tmp/gateway.log` |
| Auth | 8081 | `/tmp/auth.log` |
| Matchmaking | 8085 | `/tmp/matchmaking.log` |

---

## Bước 4 — Start Tank Server (Windows .exe)

```python
# Lưu thành /tmp/start_server.py rồi chạy
import subprocess

exe = '/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release/server_tank.exe'
cwd = '/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release'
p = subprocess.Popen([exe], cwd=cwd,
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
print(f'tank server PID: {p.pid}')
```

```bash
python3 /tmp/start_server.py
```

Kiểm tra server đang chạy:
```bash
ps aux | grep server_tank | grep -v grep
```

Server log: `/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release/server.log`

---

## Bước 5 — Start Metrics Agent

```bash
cd /tmp && python3 /home/minhk/project/SE315.Q21/test/tank_metrics_agent.py > /tmp/agent.log 2>&1 &
echo "agent PID: $!"
```

Agent expose:
- `http://localhost:9100/metrics/prometheus` — Prometheus scrape endpoint
- `http://localhost:9100/metrics` — JSON state

---

## Bước 6 — Chạy Stress Test (GPC Benchmark)

```bash
python3 /home/minhk/project/SE315.Q21/test/tank_stress_match.py \
  --step 5 --duration 120 --max 50 --base-id 30000
```

| Tham số | Mặc định | Ý nghĩa |
|---------|---------|---------|
| `--step` | 5 | Số match thêm mỗi bước |
| `--duration` | 120 | Giây quan sát mỗi bước |
| `--max` | 50 | Giới hạn tổng số match |
| `--base-id` | 30000 | Match ID bắt đầu |

---

## Tắt Hệ Thống

```bash
# Processes
kill $(pgrep -f tank_metrics_agent) 2>/dev/null
kill $(pgrep -f 'java -jar') 2>/dev/null
kill $(pgrep -f server_tank.exe) 2>/dev/null

# Docker
docker stop prometheus grafana renderer zookeeper kafka postgres_container redis mysql_container
```

---

## Rebuild

### Java services
```bash
cd /home/minhk/project/SE315.Q21/BE_CNGOL/java-meta-services
/mnt/c/Users/minhk/Downloads/apache-maven-3.9.9/bin/mvn package -DskipTests -q
```

### Tank server (C++ Windows)
```python
# python3 /tmp/build_server.py
import subprocess
msbuild = '/mnt/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe'
sln_win = subprocess.check_output(
    ['wslpath', '-w', '/mnt/d/Unity/TankOnline/Tank/server_tank/build/Project.sln']
).decode().strip()
subprocess.run([msbuild, sln_win, '/p:Configuration=Release', '/p:Platform=x64', '/m', '/v:m'],
               check=True)
```

---

## Lưu Ý Quan Trọng

| Vấn đề | Nguyên nhân | Cách fix |
|--------|------------|---------|
| Exit code 144 (SIGSTKFLT) | Bash + Windows .exe qua WSL2 interop | Dùng `python3 script.py` thay vì bash trực tiếp |
| `recv_parse=0` trong [Perf] | Server dùng IOCP backend (default) | Đã fix — cả NetworkManager và BlockingBackend đều có `drainRecvStats()` |
| Kafka `INVALID_REPLICATION_FACTOR` | Broker chưa ready hoặc thiếu `--hostname kafka` | Restart kafka với đúng `--hostname kafka` |
| Dashboard mất sau `docker rm grafana` | Data lưu trong container filesystem | Import lại từ `monitoring/grafana_dashboard.json` |
| `docker-compose up` lỗi `KeyError: ContainerConfig` | docker-compose v1.29.2 bug | Dùng `docker run` riêng từng container |
