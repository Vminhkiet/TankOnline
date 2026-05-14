# Java Meta Services — Hướng dẫn chạy và test

## Kiến trúc tổng quan

```
Client
  │
  ▼
API Gateway (:8080)
  ├── validate JWT token (trừ /api/auth/**)
  ├── inject X-User-Id, X-User-Roles vào header
  └── route đến service tương ứng
        ├── auth-service   (:8081) — Eureka lb://auth-service
        ├── shop-service   (:8088) — direct http://localhost:8088
        └── matchmaking    (:8085) — Eureka lb://matchmaking-service
```

**Bảo vệ theo tầng:**
1. **API Gateway** — từ chối request không có JWT hợp lệ (trả `401`)
2. **GatewayFilter** (shop & matchmaking) — từ chối request không qua gateway (trả `403`)
3. **GatewayHeaderAuthFilter** (shop & matchmaking) — set Spring Security context từ `X-User-Id` header

---

## Yêu cầu môi trường

| Thứ | Phiên bản |
|---|---|
| Java | 17 |
| Maven | 3.x |
| Docker | 20+ |

---

## Khởi động nhanh

### Bước 1 — Start infrastructure (database + redis)

```bash
cd /home/minhk/project/BE_CNGOL

# Dùng docker run vì docker-compose v1 có bug với Docker mới
docker run -d --name postgres_container \
  -e POSTGRES_DB=auth_service_db \
  -e POSTGRES_USER=auth_user \
  -e POSTGRES_PASSWORD=userpassword \
  -p 5432:5432 postgres:16.0

docker run -d --name redis \
  --command redis-server --requirepass 123456 \
  -p 6379:6379 redis:alpine

# MySQL dùng port 3307 (tránh conflict với MySQL local)
docker run -d --name mysql_container \
  -e MYSQL_DATABASE=shoptank_db \
  -e MYSQL_ROOT_PASSWORD="Ts171201@" \
  -p 3307:3306 mysql:8.0
```

> Đợi ~15s cho MySQL khởi động xong

### Bước 2 — Build tất cả services

```bash
export MVN=/tmp/apache-maven-3.9.6/bin/mvn   # hoặc path Maven của bạn
BASE=/home/minhk/project/BE_CNGOL/java-meta-services

for svc in discovery_service auth_service shop matchmaking_service api_gateway; do
  echo "Building $svc..."
  cd $BASE/$svc && $MVN package -DskipTests -q && echo "  [OK] $svc"
done
```

### Bước 3 — Start services theo đúng thứ tự

```bash
BASE=/home/minhk/project/BE_CNGOL/java-meta-services

# 1. Eureka (phải start trước)
nohup java -jar $BASE/discovery_service/target/*.jar > /tmp/log_discovery.txt 2>&1 &
sleep 10

# 2. Auth service
nohup java -jar $BASE/auth_service/target/auth-service-*.jar > /tmp/log_auth.txt 2>&1 &

# 3. Shop service
nohup java -jar $BASE/shop/target/shop_service-*.jar > /tmp/log_shop.txt 2>&1 &

# 4. Matchmaking service
nohup java -jar $BASE/matchmaking_service/target/matchmaking_service-*.jar > /tmp/log_matchmaking.txt 2>&1 &

# Đợi auth + shop up (khoảng 15-20s)
sleep 20

# 5. API Gateway (start cuối cùng)
nohup java -jar $BASE/api_gateway/target/api_gateway-*.jar > /tmp/log_gateway.txt 2>&1 &

# Đợi gateway up (khoảng 30s rồi test)
sleep 30
```

### Bước 4 — Cleanup (sau khi test xong)

```bash
# Dừng Java services
kill $(pgrep -f "SNAPSHOT.jar") 2>/dev/null

# Xóa Docker containers
docker stop mysql_container postgres_container redis
docker rm mysql_container postgres_container redis
```

---

## Test bằng Postman

### Import collection

1. Mở Postman
2. **Import** → chọn file `postman_collection.json` trong thư mục này
3. Collection đã có sẵn biến `{{base_url}} = http://localhost:8080`

### Thứ tự test

| # | Request | Mô tả | Expected |
|---|---|---|---|
| 1 | **Login** | Đăng nhập → tự lưu `{{token}}` | 200 + JWT |
| 2 | Login sai password | Test reject | 4xx |
| 3 | Refresh Token | Dùng `{{refreshToken}}` lấy token mới | 200 |
| 4 | Logout | Xóa session | 200 |
| 5 | Shop — có token | Lấy items | 200 |
| 6 | Shop — không token | Phải bị chặn | 401 |
| 7 | Shop — token giả | Phải bị chặn | 401 |
| 8 | Shop — bypass gateway | Gọi thẳng :8088 | 403 |

> **Lưu ý:** Chạy request **Login** trước. Token được tự động lưu vào biến `{{token}}` qua Postman script.

---

## Test bằng curl

```bash
# Bước 1: Login
RESPONSE=$(curl -s -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}')

TOKEN=$(echo $RESPONSE | python3 -c "import sys,json; print(json.load(sys.stdin)['jwt'])")
REFRESH=$(echo $RESPONSE | python3 -c "import sys,json; print(json.load(sys.stdin)['refreshToken'])")

echo "Token: $TOKEN"

# Bước 2: Gọi API có bảo vệ
curl -s http://localhost:8080/api/shop/items \
  -H "Authorization: Bearer $TOKEN"

# Bước 3: Test chặn không có token (phải 401)
curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/api/shop/items

# Bước 4: Test bypass gateway (phải 403)
curl -s -o /dev/null -w "%{http_code}" http://localhost:8088/api/shop/items \
  -H "Authorization: Bearer $TOKEN"

# Bước 5: Refresh token
curl -s -X POST http://localhost:8080/api/auth/refresh \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d "{\"refreshToken\":\"$REFRESH\"}"

# Bước 6: Logout
curl -s -X POST http://localhost:8080/api/auth/logout \
  -H "Authorization: Bearer $TOKEN"
```

---

## Danh sách tất cả Endpoints

### Auth Service (`/api/auth/**` — public)

| Method | Path | Auth? | Mô tả |
|---|---|---|---|
| POST | `/api/auth/login` | Không | Đăng nhập |
| POST | `/api/auth/logout` | Cần token | Đăng xuất |
| POST | `/api/auth/refresh` | Cần token | Lấy token mới |

### User Service (`/api/user/**` — public)

| Method | Path | Mô tả |
|---|---|---|
| GET | `/api/user/users` | Lấy danh sách user |

### Shop Service (`/api/shop/**` — cần token)

| Method | Path | Mô tả |
|---|---|---|
| GET | `/api/shop/items` | Lấy tất cả item |
| GET | `/api/shop/items/category/{category}` | Lọc theo category |
| GET | `/api/shop/items/{id}` | Chi tiết item |
| POST | `/api/shop/purchase` | Mua item |
| POST | `/api/shop/admin/items` | Thêm item (admin) |
| PUT | `/api/shop/admin/items/{id}` | Cập nhật item (admin) |
| DELETE | `/api/shop/admin/items/{id}` | Xóa item (admin) |

---

## Kết quả test tự động (8/8 PASS)

```
[PASS] T1: Login = 200
[PASS] T2: Sai password = 4xx
[PASS] T3: Shop không token = 401
[PASS] T4: Shop token hợp lệ = 200
[PASS] T5: Bypass gateway = 403
[PASS] T6: Token giả = 401
[PASS] T7: Refresh token = 200
[PASS] T8: Logout = 200
```

---

## Luồng JWT

```
1. Client:  POST /api/auth/login  →  nhận accessToken + refreshToken
2. Client:  GET /api/shop/items
            Authorization: Bearer <accessToken>
3. Gateway: verify JWT signature
            → thêm X-User-Id: 1
            → thêm X-User-Roles: ROLE_ADMIN
            → thêm X-Gateway-Origin: MySecretKey123
4. Shop:    GatewayFilter kiểm tra X-Gateway-Origin ✓
            GatewayHeaderAuthFilter set Spring Security context
            → trả dữ liệu
```

---

## Troubleshooting

| Lỗi | Nguyên nhân | Cách fix |
|---|---|---|
| `401 Unauthorized` | Thiếu/sai/hết hạn token | Login lại |
| `403 Forbidden` | Gọi trực tiếp shop:8088 | Dùng gateway port 8080 |
| `503 Service Unavailable` | Service chưa đăng ký Eureka | Đợi thêm 30s hoặc check log |
| Redis `NOAUTH` | Spring Boot 3 + Redis 7 protocol issue | Đã fix với RESP2 + `spring.data.redis.*` |
| `Already logged in` | Redis còn session cũ | `docker exec redis redis-cli -a 123456 FLUSHALL` |
