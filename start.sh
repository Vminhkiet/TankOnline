#!/bin/bash
set -e

BASE=/home/minhk/project/SE315.Q21/BE_CNGOL/java-meta-services
COMPOSE_DIR=/home/minhk/project/SE315.Q21/BE_CNGOL

echo "[1/4] Starting Docker services..."
cd "$COMPOSE_DIR" && docker-compose up -d
echo "      Docker OK"

echo "[2/4] Waiting for Kafka (20s)..."
sleep 20
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.create --partitions 1 --replication-factor 1 > /dev/null 2>&1 || true
docker exec kafka kafka-topics --bootstrap-server localhost:9092 \
  --create --if-not-exists --topic match.result --partitions 1 --replication-factor 1 > /dev/null 2>&1 || true
echo "      Kafka topics OK"

echo "[3/4] Starting Java services..."
java -jar "$BASE/discovery_service/target/discovery_service-0.0.1-SNAPSHOT.jar" > /tmp/eureka.log 2>&1 &

echo "      Waiting for Eureka (25s)..."
sleep 25
STATUS=$(curl -s --max-time 3 http://localhost:8761/actuator/health | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null || echo "DOWN")
echo "      Eureka: $STATUS"

java -jar "$BASE/auth_service/target/auth-service-0.0.1-SNAPSHOT.jar" > /tmp/auth.log 2>&1 &
java -jar "$BASE/api_gateway/target/api_gateway-0.0.1-SNAPSHOT.jar" > /tmp/gateway.log 2>&1 &
java -jar "$BASE/matchmaking_service/target/matchmaking_service-0.0.1-SNAPSHOT.jar" > /tmp/matchmaking.log 2>&1 &

echo "      Waiting for services (30s)..."
sleep 30
for svc in "Gateway:8080" "Auth:8081" "Matchmaking:8085"; do
  name=${svc%%:*}; port=${svc##*:}
  status=$(curl -s --max-time 3 http://localhost:$port/actuator/health | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null || echo "DOWN")
  echo "      $name: $status"
done

echo "[4/4] Starting Tank server (Windows)..."
python3 -c "
import subprocess
exe = '/mnt/d/Unity/TankOnline/SE315.Q21/Tank/out/build/x64-Release/server_tank/Release/server_tank.exe'
cwd = '/mnt/d/Unity/TankOnline/SE315.Q21/Tank/out/build/x64-Release/server_tank/Release'
p = subprocess.Popen([exe], cwd=cwd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
print('      Tank server PID:', p.pid)
"

echo ""
WIFI_IP=$(powershell.exe -Command "Get-NetIPAddress -AddressFamily IPv4 | Where-Object { \$_.InterfaceAlias -like 'Local Area Connection*' } | Select-Object InterfaceAlias, IPAddress | Format-Table -AutoSize" 2>/dev/null)
echo "All services started. WiFi endpoint: ${WIFI_IP}:8080"
