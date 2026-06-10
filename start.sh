#!/bin/bash
set -e

MVN=/mnt/c/Users/minhk/Downloads/apache-maven-3.9.9/bin/mvn
BASE=/home/minhk/project/new/SE315.Q21/BE_CNGOL/java-meta-services
TANK_EXE=/mnt/d/Unity/TankOnline/game/SE315.Q21/Tank/out/build/x64-Release/server_tank/Release/server_tank.exe
TANK_CWD=/mnt/d/Unity/TankOnline/game/SE315.Q21/Tank/out/build/x64-Release/server_tank/Release
TANK_SLN="D:\\Unity\\TankOnline\\game\\SE315.Q21\\Tank\\build_server_tank.bat"

# ── 0. Kill services cũ ───────────────────────────────────────────────────────
echo "[0/5] Cleaning up old processes..."
pkill -f "java -jar" 2>/dev/null || true
sleep 2

# ── 1. Docker containers ──────────────────────────────────────────────────────
echo "[1/5] Starting Docker containers..."
docker rm -f zookeeper kafka postgres_container redis mysql_container 2>/dev/null || true

WSL2_IP=$(ip addr show eth0 | grep "inet " | awk '{print $2}' | cut -d/ -f1)
echo "      WSL2 IP: $WSL2_IP"

docker run -d --name zookeeper \
  -e ZOOKEEPER_CLIENT_PORT=2181 -e ZOOKEEPER_TICK_TIME=2000 \
  -p 2181:2181 confluentinc/cp-zookeeper:7.3.0 > /dev/null

docker run -d --name kafka --hostname kafka --link zookeeper:zookeeper \
  -p 9092:9092 \
  -e KAFKA_BROKER_ID=1 \
  -e KAFKA_ZOOKEEPER_CONNECT=zookeeper:2181 \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT \
  -e KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:29092,PLAINTEXT_HOST://0.0.0.0:9092 \
  -e "KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://kafka:29092,PLAINTEXT_HOST://${WSL2_IP}:9092" \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  -e KAFKA_AUTO_CREATE_TOPICS_ENABLE=true \
  confluentinc/cp-kafka:7.3.0 > /dev/null

docker run -d --name postgres_container \
  -e POSTGRES_DB=auth_service_db -e POSTGRES_USER=auth_user -e POSTGRES_PASSWORD=userpassword \
  -p 5432:5432 postgres:16.0 > /dev/null

docker run -d --name redis \
  -p 6379:6379 redis:alpine redis-server --requirepass 123456 > /dev/null

docker run -d --name mysql_container \
  -e MYSQL_DATABASE=shoptank_db -e MYSQL_ROOT_PASSWORD=Ts171201@ \
  -p 3307:3306 mysql:8.0 > /dev/null

echo "      Docker OK"

# ── 2. Kafka topics ───────────────────────────────────────────────────────────
echo "[2/5] Waiting for Kafka..."
until docker exec kafka kafka-topics --bootstrap-server localhost:9092 --list > /dev/null 2>&1; do
    sleep 3; printf "."
done
echo " Kafka ready"

docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.create --partitions 1 --replication-factor 1 > /dev/null 2>&1 || true
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.result --partitions 1 --replication-factor 1 > /dev/null 2>&1 || true
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.cancel --partitions 1 --replication-factor 1 > /dev/null 2>&1 || true
echo "      Kafka topics OK"

# ── 2.5 Tạo PostgreSQL databases nếu chưa có ─────────────────────────────────
echo "      Creating PostgreSQL databases..."
for db in history_service_db profile_service_db; do
    docker exec postgres_container psql -U auth_user -d auth_service_db \
        -c "CREATE DATABASE $db;" 2>/dev/null || true
done
echo "      Databases OK"

# ── 3. Build Java nếu JAR chưa có ────────────────────────────────────────────
echo "[3/5] Checking Java JARs..."
NEED_BUILD=0
for svc in discovery_service auth_service api_gateway matchmaking_service history_service monitoring_service profile_service shop; do
    jar=$(find "$BASE/$svc/target" -name "*.jar" -not -name "*sources*" 2>/dev/null | head -1)
    if [ -z "$jar" ]; then
        echo "      $svc: JAR missing → will build"
        NEED_BUILD=1
    fi
done

if [ "$NEED_BUILD" = "1" ]; then
    echo "      Building Java services (Maven)..."
    for svc in discovery_service auth_service api_gateway matchmaking_service history_service monitoring_service profile_service shop; do
        cd "$BASE/$svc" && $MVN package -DskipTests -q
        echo "      $svc built"
    done
else
    echo "      All JARs present, skipping build"
fi

# Start Eureka
java -jar "$BASE/discovery_service/target/discovery_service-0.0.1-SNAPSHOT.jar" > /tmp/eureka.log 2>&1 &
echo "      Eureka starting..."
until curl -s --max-time 2 http://localhost:8761/actuator/health 2>/dev/null | grep -q "UP"; do
    sleep 3; printf "."
done
echo " Eureka UP"

# Start remaining services
java -jar "$BASE/auth_service/target/auth-service-0.0.1-SNAPSHOT.jar"                    > /tmp/auth.log        2>&1 &
java -jar "$BASE/api_gateway/target/api_gateway-0.0.1-SNAPSHOT.jar"                       > /tmp/gateway.log     2>&1 &
java -Dtank.server.host=192.168.100.31 \
     -jar "$BASE/matchmaking_service/target/matchmaking_service-0.0.1-SNAPSHOT.jar"       > /tmp/matchmaking.log 2>&1 &
java -jar "$BASE/history_service/target/history_service-0.0.1-SNAPSHOT.jar"                > /tmp/history.log     2>&1 &
java -jar "$BASE/monitoring_service/target/monitoring_service-0.0.1-SNAPSHOT.jar"          > /tmp/monitoring.log  2>&1 &
java -jar "$BASE/profile_service/target/profile_service-0.0.1-SNAPSHOT.jar"                > /tmp/profile.log     2>&1 &
java -jar "$BASE/shop/target/shop_service-0.0.1-SNAPSHOT.jar"                              > /tmp/shop.log        2>&1 &

until curl -s --max-time 2 http://localhost:8080/actuator/health 2>/dev/null | grep -q "UP"; do
    sleep 3; printf "."
done
echo " Gateway UP"

for svc in "Eureka:8761" "Auth:8081" "Gateway:8080" "Matchmaking:8085" "History:8086" "Profile:8087" "Shop:8088" "Monitoring:8090"; do
    name=${svc%%:*}; port=${svc##*:}
    status=$(curl -s --max-time 2 http://localhost:$port/actuator/health 2>/dev/null \
        | python3 -c "import sys,json; print(json.load(sys.stdin).get('status','?'))" 2>/dev/null || echo "DOWN")
    echo "      $name ($port): $status"
done

# ── 4. Build Tank server ──────────────────────────────────────────────────────
echo "[4/5] Checking Tank server..."
MAIN_CPP_WIN="/mnt/d/Unity/TankOnline/game/SE315.Q21/Tank/server_tank/src/main.cpp"
MAIN_CPP_WSL="/home/minhk/project/new/SE315.Q21/Tank/server_tank/src/main.cpp"

# Patch WSL2 IP vào Kafka broker default, sync cả 2 bản
sed -i "s|getEnv(\"KAFKA_BROKERS\", \"[^\"]*\")|getEnv(\"KAFKA_BROKERS\", \"${WSL2_IP}:9092\")|g" "$MAIN_CPP_WIN"
cp "$MAIN_CPP_WIN" "$MAIN_CPP_WSL"
echo "      Kafka broker patched → ${WSL2_IP}:9092"

# Patch WSL2 IP vào anticheat ban host
AC_CPP_WIN="/mnt/d/Unity/TankOnline/game/SE315.Q21/tools/anticheat/anticheat.cpp"
AC_CPP_WSL="/home/minhk/project/new/SE315.Q21/tools/anticheat/anticheat.cpp"
sed -i "s|static const char\* AUTH_HOST  = \"[^\"]*\"|static const char* AUTH_HOST  = \"${WSL2_IP}\"|g" "$AC_CPP_WIN"
cp "$AC_CPP_WIN" "$AC_CPP_WSL"
echo "      Anticheat host patched → ${WSL2_IP}"

# Kill old tank server
powershell.exe -Command "Stop-Process -Name 'server_tank' -Force -ErrorAction SilentlyContinue" 2>/dev/null || true
sleep 1

echo "      Building tank server..."
cmd.exe /c "$TANK_SLN"
echo "      Tank server built"

python3 -c "
import subprocess, sys
exe = '$TANK_EXE'
cwd = '$TANK_CWD'
p = subprocess.Popen([exe], cwd=cwd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
print('      Tank server PID:', p.pid)
"

# ── 5. Summary ────────────────────────────────────────────────────────────────
echo ""
echo "[5/5] All services started"
echo "      WSL2 IP  : $WSL2_IP"
echo "      Eureka   : http://localhost:8761"
echo "      Gateway  : http://localhost:8080"
echo "      Logs     : /tmp/eureka.log, /tmp/auth.log, /tmp/gateway.log, /tmp/matchmaking.log"
