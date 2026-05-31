# Hướng dẫn đổi IP khi kết nối WiFi mới

Hệ thống dùng 2 loại IP cần cập nhật khi đổi mạng:

| Loại | Ý nghĩa | Lệnh lấy IP |
|------|---------|-------------|
| **WSL2 IP** (`172.25.x.x`) | IP của WSL2 eth0 — dùng cho Kafka, API Gateway | `ip addr show eth0 \| grep "inet " \| awk '{print $2}' \| cut -d/ -f1` |
| **Windows WiFi IP** (`192.168.x.x`) | IP card WiFi Windows — Unity client kết nối UDP tới game server | `ipconfig \| findstr "IPv4"` (chọn card WiFi) |

> **Lưu ý:** WSL2 IP thay đổi mỗi lần reboot. Windows WiFi IP thay đổi khi đổi mạng.

---

## Danh sách file cần sửa

### Tự động sửa khi chạy `./start.sh`

Những file này **không cần sửa tay** — `start.sh` tự patch trước khi build:

| File | Biến | Giá trị tự patch |
|------|------|-----------------|
| `Tank/server_tank/src/main.cpp` (WSL2+Win) | `kafkaBrokers` | WSL2 IP tự detect |
| `tools/anticheat/anticheat.cpp` (WSL2+Win) | `AUTH_HOST` | WSL2 IP tự detect |

---

### Phải sửa tay khi đổi mạng

#### 1. WSL2 IP — thay `172.25.203.168` bằng IP mới

**`BE_CNGOL/java-meta-services/matchmaking_service/src/main/resources/application.yaml` — dòng 8**
```yaml
spring:
  kafka:
    bootstrap-servers: 172.25.203.168:9092   # ← SỬA thành WSL2 IP mới
```

**`BE_CNGOL/java-meta-services/monitoring_service/src/main/resources/application.yml` — dòng 8**
```yaml
spring:
  kafka:
    bootstrap-servers: 172.25.203.168:9092   # ← SỬA thành WSL2 IP mới
```

**`monitoring/prometheus.yml` — tất cả targets**
```yaml
- targets: ["172.25.203.168:8761"]   # ← SỬA thành WSL2 IP mới (7 chỗ)
- targets: ["172.25.203.168:8080"]
# ... (các service còn lại)
```

**`Tank Legends Management Web/src/main.jsx` — dòng 35**
```js
const API_BASE = 'http://172.25.203.168:8080';   // ← SỬA thành WSL2 IP mới
```

---

#### 2. Windows WiFi IP — thay `192.168.100.31` bằng IP mới

**`start.sh` — dòng 105**
```bash
java -Dtank.server.host=192.168.100.31 \   # ← SỬA thành Windows WiFi IP mới
```

**`BE_CNGOL/java-meta-services/matchmaking_service/src/main/resources/application.yaml` — dòng 38**
```yaml
tank:
  server:
    host: 192.168.100.31   # ← SỬA thành Windows WiFi IP mới
```

---

## Checklist nhanh khi đổi mạng

```bash
# 1. Lấy WSL2 IP mới
WSL2_IP=$(ip addr show eth0 | grep "inet " | awk '{print $2}' | cut -d/ -f1)
echo "WSL2 IP mới: $WSL2_IP"

# 2. Lấy Windows WiFi IP (chạy trong PowerShell)
# ipconfig | findstr "IPv4"
```

Sau đó sửa các file theo bảng trên, rồi:

```bash
# 3. Rebuild và restart toàn bộ
./start.sh
```

`start.sh` sẽ tự patch `main.cpp` và `anticheat.cpp` với WSL2 IP mới trước khi build.

---

## Sơ đồ kết nối

```
Unity Client (Windows)
    │
    ├─ HTTP  → 192.168.100.31:8080  (API Gateway, qua WiFi LAN)    ← Windows WiFi IP
    └─ UDP   → 192.168.100.31:8080  (C++ Game Server)               ← Windows WiFi IP

C++ Game Server (Windows)
    └─ Kafka → 172.25.203.168:9092  (Kafka trong Docker/WSL2)       ← WSL2 IP

anticheat.exe (Windows, Admin)
    └─ HTTP  → 172.25.203.168:8080  (API Gateway)                   ← WSL2 IP

Java Services (WSL2)
    └─ Kafka → 172.25.203.168:9092  (Kafka trong Docker)            ← WSL2 IP (localhost OK)

Admin Frontend (Windows browser)
    └─ HTTP  → 172.25.203.168:8080  (API Gateway)                   ← WSL2 IP
```
