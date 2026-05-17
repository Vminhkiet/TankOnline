# SE315.Q21 — TankOnline Backend + Game Server

## Architecture

```
WSL2 (Ubuntu)                          Windows Host
─────────────────────────────          ──────────────────────────────
Docker:                                D:\Unity\TankOnline\Tank\
  zookeeper     :2181                    build_full/server_tank/
  kafka         :9092  ──────────────►   Release/server_tank.exe
  postgres      :5432                      - UDP :8080  (game clients)
  redis         :6379                      - Kafka consumer: match.create
  mysql         :3307                      - Kafka producer: match.result

Java (bare process):
  discovery_service  :8761  (Eureka)
  auth_service       :8081
  api_gateway        :8080  (entry point for clients)
  matchmaking_service :8085
```

**Critical IPs (WSL2 resets on reboot):**
- WSL2 eth0: `172.25.203.168` — used in Kafka advertised listener
- Windows host: `172.25.192.1` — game server UDP endpoint

Get current WSL2 IP: `ip addr show eth0 | grep "inet " | awk '{print $2}' | cut -d/ -f1`

If WSL2 IP changes, update:
1. `KAFKA_ADVERTISED_LISTENERS` in docker-compose.yml → `PLAINTEXT_HOST://<new-ip>:9092`
2. Kafka container must be restarted
3. `main.cpp` default KAFKA_BROKERS string (see Tank Server section)

---

## 1. Start Docker Services

### Problem: docker-compose fails with `KeyError: 'ContainerConfig'`
This is a docker-compose v1.29.2 bug. Use individual `docker run` instead.

```bash
# Stop and clean old containers first
docker rm -f zookeeper kafka postgres_container redis mysql_container 2>/dev/null

# Zookeeper
docker run -d --name zookeeper \
  -e ZOOKEEPER_CLIENT_PORT=2181 -e ZOOKEEPER_TICK_TIME=2000 \
  -p 2181:2181 confluentinc/cp-zookeeper:7.3.0

# Kafka — MUST use --hostname kafka (inter-broker uses hostname "kafka")
WSL2_IP=$(ip addr show eth0 | grep "inet " | awk '{print $2}' | cut -d/ -f1)
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

# Postgres (auth DB)
docker run -d --name postgres_container \
  -e POSTGRES_DB=auth_service_db -e POSTGRES_USER=auth_user -e POSTGRES_PASSWORD=userpassword \
  -p 5432:5432 postgres:16.0

# Redis
docker run -d --name redis \
  -p 6379:6379 redis:alpine redis-server --requirepass 123456

# MySQL (shop service)
docker run -d --name mysql_container \
  -e MYSQL_DATABASE=shoptank_db -e MYSQL_ROOT_PASSWORD=Ts171201@ \
  -p 3307:3306 mysql:8.0
```

### Create Kafka topics (wait ~15s after kafka starts)
```bash
sleep 15
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.create --partitions 1 --replication-factor 1
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.result --partitions 1 --replication-factor 1
docker exec kafka kafka-topics --bootstrap-server localhost:9092 --list
```

**Why --hostname kafka is required:** Without it, the container hostname becomes a random ID.
The broker advertises `kafka:29092` for inter-broker traffic, and the controller can't resolve
it → "available brokers: 0" → all topic creation fails with INVALID_REPLICATION_FACTOR.

---

## 2. Start Java Services

JARs are pre-built in `target/` directories. If missing, build first (see Build section).

```bash
BASE=/home/minhk/project/SE315.Q21/BE_CNGOL/java-meta-services

# Kill any running services
pkill -f "java -jar" 2>/dev/null; sleep 3

# Start Eureka first and wait for it
java -jar "$BASE/discovery_service/target/discovery_service-0.0.1-SNAPSHOT.jar" > /tmp/eureka.log 2>&1 &
sleep 20

# Then start remaining services
java -jar "$BASE/auth_service/target/auth-service-0.0.1-SNAPSHOT.jar" > /tmp/auth.log 2>&1 &
java -jar "$BASE/api_gateway/target/api_gateway-0.0.1-SNAPSHOT.jar" > /tmp/gateway.log 2>&1 &
java -jar "$BASE/matchmaking_service/target/matchmaking_service-0.0.1-SNAPSHOT.jar" > /tmp/matchmaking.log 2>&1 &

sleep 30
curl -s http://localhost:8761/actuator/health  # Eureka
curl -s http://localhost:8080/actuator/health  # Gateway
```

Logs: `/tmp/eureka.log`, `/tmp/auth.log`, `/tmp/gateway.log`, `/tmp/matchmaking.log`

### Service ports
| Service | Port |
|---------|------|
| Eureka (discovery) | 8761 |
| API Gateway | 8080 |
| Auth service | 8081 |
| Matchmaking | 8085 |

### Fix: BCrypt password mismatch (403 on login)
If login returns HTTP 403, the BCrypt hashes in PostgreSQL don't match "password123":
```python
import bcrypt, subprocess
new_hash = bcrypt.hashpw(b'password123', bcrypt.gensalt(rounds=10)).decode()
sql = f"UPDATE users SET password = '{new_hash}' WHERE username IN ('player1','player2');"
subprocess.run(['docker','exec','postgres_container','psql',
                '-U','auth_user','-d','auth_service_db','-c', sql], check=True)
```

---

## 3. Start Tank Server (Windows)

The tank server is a Windows .exe launched via WSL2 interop.

**CRITICAL: Cannot pass env vars to Windows .exe via WSL2 shell.**
WSLENV, export, inline prefix — none work. The KAFKA_BROKERS default is hardcoded in main.cpp.

Use this Python script (avoids exit code 144 / SIGSTKFLT from bash):

```python
# /tmp/start_server.py
import subprocess, os

exe = '/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release/server_tank.exe'
cwd = '/mnt/d/Unity/TankOnline/Tank/build_full/server_tank/Release'

p = subprocess.Popen([exe], cwd=cwd,
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
print(p.pid)
```

```bash
python3 /tmp/start_server.py
```

Verify it's running:
```bash
ps aux | grep server_tank | grep -v grep
```

### Tank server Kafka config
In `/mnt/d/Unity/TankOnline/Tank/server_tank/src/main.cpp`:
```cpp
// Hardcoded default so WSL2 interop can't silently drop env var
const std::string kafkaBrokers = getEnv("KAFKA_BROKERS", "172.25.203.168:9092");
```

If WSL2 IP changes, edit this line and rebuild (see Build section).

### Kafka topics consumed/produced by tank server
- **Consumes**: `match.create` — creates a match in memory when received
- **Produces**: `match.result` — when match ends (kill/timeout)

`match.create` payload: `{"matchId":N,"mapName":"world","maxDuration":300,"players":[pid1,pid2]}`
`match.result` payload: `{"matchId":N,"outcome":"win|draw|timeout","winnerId":N,"durationSecs":N.N,"kills":{"pid":count}}`

---

## 4. Build

### Java services
```bash
cd /home/minhk/project/SE315.Q21/BE_CNGOL/java-meta-services
/mnt/c/Users/minhk/Downloads/apache-maven-3.9.9/bin/mvn package -DskipTests -q
```
Or per-service: `cd <service_dir> && mvn package -DskipTests -q`

### Tank server (C++ Windows, MSBuild)
```bash
python3 -c "
import subprocess
msbuild = '/mnt/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe'
sln = r'D:\Unity\TankOnline\Tank\server_tank\server_tank.sln'
subprocess.run([msbuild, sln, '/p:Configuration=Release', '/p:Platform=x64', '/m', '/v:m'],
               check=True)
"
```

---

## 5. E2E Test

Full test script: `/tmp/tank_full_e2e.py`

```bash
python3 /tmp/tank_full_e2e.py
```

Tests the complete pipeline:
1. Login player1 + player2 via API Gateway → JWT
2. Both call `/api/matchmaking/find` simultaneously → matchId, serverHost, serverPort, playerId
3. Verify `match.create` event in Kafka
4. Tank server (Windows) consumes event, creates match in memory
5. UDP gameplay: move + shoot packets at 20Hz for 22s
6. Read `match.result` from Kafka → winner, kills, duration

### Key API endpoints
| Endpoint | Method | Auth |
|----------|--------|------|
| `/api/auth/login` | POST | none |
| `/api/matchmaking/find` | POST | Bearer JWT |

### UDP packet format (bit-packed, Little Endian uint32 words)
Uses `bits_required(min, max)` = `ceil(log2(max-min+1))` bits per field.

**Move packet** (C2S_MOVE = 1001):
```
packetSize  [8..1400] → 11 bits
opcode      [0..65535] → value=1001
matchId     [0..1000000]
playerId    [0..255]
seq         [0..255]
unk         [0..65535] → 0
dir_x       [0..2]  (0=left, 1=none, 2=right)
dir_z       [0..2]  (0=back, 1=none, 2=forward)
unk2        [0..255] → 0
```

**Shoot packet** (C2S_SHOOT = 1002):
```
packetSize  [8..1400]
opcode      [0..65535] → value=1002
matchId     [0..1000000]
playerId    [0..255]
seq         [0..255]
unk         [0..65535] → 0
force       [15..30]
```

**Snapshot header** (S2C_SNAPSHOT = 2000, raw binary, NOT bit-packed):
`<IHHHH` = matchId(4) opcode(2) tick(2) tankCount(2) localPlayerId(2)
Each tank: `<IffffhB` = tankId(4) x(4) y(4) z(4) yaw(4) hp(2) flags(1)

---

## 6. Common Problems & Fixes

| Problem | Cause | Fix |
|---------|-------|-----|
| Kafka `INVALID_REPLICATION_FACTOR` | Broker sees 0 available brokers | Restart kafka with `--hostname kafka` |
| Kafka CLI "Timed out waiting for a node assignment" | Kafka not fully ready yet | Wait 15s after start before creating topics |
| `docker-compose up` fails `KeyError: ContainerConfig` | docker-compose v1.29.2 bug | Use individual `docker run` commands |
| Login HTTP 403 | BCrypt hash mismatch in DB | Re-hash passwords with Python bcrypt |
| Tank server env var not received | WSL2 interop doesn't pass env to Windows .exe | Hardcode default in main.cpp |
| Bash exits 144 (SIGSTKFLT) when running .exe | Complex bash + Windows interop | Use Python subprocess.Popen script |
| `kafka_read_new` returns None | Consumer `--timeout-ms` too short (JVM startup ~2-3s) | Only filter lines starting with `{`, use 8s+ timeout |

---

## 7. File Locations

| File | Purpose |
|------|---------|
| `/tmp/start_server.py` | Launch tank server.exe from WSL2 |
| `/tmp/start_services.sh` | Launch all Java services |
| `/tmp/tank_full_e2e.py` | Full E2E test script |
| `/tmp/eureka.log` | Eureka logs |
| `/tmp/auth.log` | Auth service logs |
| `/tmp/gateway.log` | API Gateway logs |
| `/tmp/matchmaking.log` | Matchmaking service logs |
| `/mnt/d/Unity/TankOnline/Tank/server_tank/src/main.cpp` | Tank server entry point (Kafka broker config) |
| `BE_CNGOL/java-meta-services/` | All Spring Boot service source code |
| `BE_CNGOL/docker-compose.yml` | Docker config (reference only, use `docker run` instead) |
