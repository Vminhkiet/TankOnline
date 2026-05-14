#!/bin/bash
set -e

# Điều hướng đến thư mục gốc của dự án
cd "$(dirname "$0")/.."

# Tuỳ chọn xóa volume khi dừng
REMOVE_VOLUMES=false

# Hàm dọn dẹp khi Ctrl+C
cleanup() {
  echo
  echo "⚡ Caught signal. Stopping Docker Compose services..."
  docker compose -f /home/minhk/GameServer/docker-compose.yml down --volumes
  echo "✅ Docker Compose services stopped."
}


# Bắt Ctrl+C hoặc terminate
trap cleanup INT TERM

# ----------------------------------------------------------------------
# BƯỚC 1: KHỞI ĐỘNG DOCKER COMPOSE
# ----------------------------------------------------------------------
echo "🚀 Starting dependent Docker Compose services (PostgreSQL, Redis, RedisInsight)..."
docker compose up -d

# ----------------------------------------------------------------------
# BƯỚC 2: CHỜ POSTGRESQL SẴN SÀNG
# ----------------------------------------------------------------------
echo "⏳ Waiting for PostgreSQL to be ready..."
until docker exec postgres_container pg_isready -U auth_user -d auth_service_db > /dev/null 2>&1; do
  echo "  ⏳ Waiting for DB..."
  sleep 1
done
echo "✅ PostgreSQL is ready!"

# ----------------------------------------------------------------------
# BƯỚC 3: CHẠY SPRING BOOT AUTH SERVICE
# ----------------------------------------------------------------------
echo "🚀 Running Spring Boot application..."
cd java-meta-services/auth_service

echo "🔨 Building Spring Boot application..."
mvn clean package -DskipTests

echo "▶️ Starting Spring Boot application..."
java -jar target/auth-service-0.0.1-SNAPSHOT.jar
