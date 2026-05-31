# Saga Pattern — TankOnline Backend

## Tổng quan

Hệ thống áp dụng **Choreography-based Saga** (không dùng central orchestrator).  
Mỗi service tự lắng nghe Kafka events, thực hiện bước xử lý của mình, và publish event tiếp theo hoặc compensation event nếu thất bại.

**Event bus:** Apache Kafka  
**Không sử dụng:** Axon Framework, Eventuate, Temporal hay bất kỳ Saga framework nào.

---

## Saga 1 — User Registration

### Mục tiêu
Đảm bảo khi đăng ký tài khoản, cả `user` (auth_service) và `profile` (profile_service) đều được tạo thành công, hoặc không có gì được lưu.

### Flow

```
Client → auth_service.register()
           │
           ├─ Lưu User vào PostgreSQL
           └─ Publish [user.created] ──────────────────→ profile_service
                                                              │
                                                     Tạo Profile thành công
                                                              │ fail
                                                     Publish [user.profile.failed]
                                                              │
                                          auth_service ←──────┘
                                          Xóa User mồ côi (compensation)
```

### Files liên quan
| Bước | File |
|------|------|
| Publish `user.created` | `auth_service/serviceImpl/UserService.java` |
| Tạo profile / publish `user.profile.failed` | `profile_service/kafka/UserCreatedKafkaListener.java` |
| **Compensation**: xóa user mồ côi | `auth_service/kafka/UserProfileFailedKafkaListener.java` |
| Test | `auth_service/.../saga/SagaOneAuthCompensationTest.java` |
| Test | `profile_service/.../saga/SagaOneUserRegistrationTest.java` |

### ✅ Đã làm
- Publish `user.created` khi đăng ký thành công
- profile_service tạo profile khi nhận event
- profile_service publish `user.profile.failed` nếu tạo profile thất bại
- auth_service nhận `user.profile.failed` và xóa user mồ côi (compensation)
- Unit test cho cả hai bước

### ❌ Chưa làm
- **Outbox pattern**: `userRepository.save()` và `kafkaTemplate.send()` không atomic — nếu DB commit nhưng Kafka fail → event không được gửi → profile không tạo nhưng cũng không có compensation
- **Idempotency**: nếu Kafka redelivery `user.created` → profile bị tạo 2 lần
- **Dead Letter Queue (DLQ)**: nếu `user.profile.failed` bị drop → compensation không chạy

---

## Saga 2 — Item Purchase

### Mục tiêu
Đảm bảo khi mua item: coin bị trừ **và** inventory/lịch sử được lưu. Nếu DB fail sau khi đã trừ coin → hoàn trả coin.

### Flow

```
Client → shop_service.purchase()
           │
           ├─ Bước 1: Gọi profile_service HTTP /internal/coins/deduct (trừ coin)
           │          ↓ fail → throw exception (không cần compensation vì chưa trừ)
           │
           ├─ Bước 2: Lưu PlayerItem + Purchase vào MySQL (trong @Transactional)
           │          ↓ fail
           └─────────── Compensation: Gọi profile_service HTTP /internal/coins/refund
                                       (hoàn trả toàn bộ coin đã trừ)
```

### Files liên quan
| Bước | File |
|------|------|
| Toàn bộ saga | `shop_service/serviceImpl/ShopServiceImpl.java` |
| Test | `shop_service/.../saga/SagaTwoPurchaseCompensationTest.java` |

### ✅ Đã làm
- Theo dõi `totalDeducted` để biết cần hoàn trả bao nhiêu
- Compensation: hoàn trả coin khi DB save thất bại
- `@Transactional` rollback tự động cho tất cả DB writes
- Unit test kiểm tra scenario coin refund

### ❌ Chưa làm
- **Compensation của compensation**: nếu `refundCoinsToProfile()` cũng fail (network timeout, profile_service down) → coin mất vĩnh viễn, chỉ `log.error`
- **Retry mechanism**: không có retry khi HTTP call đến profile_service thất bại tạm thời
- **Distributed transaction visibility**: không có trạng thái saga (STARTED / COMPENSATING / COMPLETED) để monitor

---

## Saga 3 — Match Result Processing

### Mục tiêu
Khi một trận đấu kết thúc, lưu lịch sử (history_service) và cập nhật stats/RP (profile_service).

### Flow

```
C++ game server → Kafka [match.result]
                        │
                        ├──→ history_service: lưu MatchHistory vào PostgreSQL
                        │                    cập nhật leaderboard Redis
                        │
                        └──→ profile_service: cộng RP, cập nhật win/loss stats
```

### Files liên quan
| Bước | File |
|------|------|
| Consume `match.result` | `history_service/kafka/MatchResultKafkaListener.java` |
| Consume `match.result` | `profile_service/kafka/MatchResultKafkaListener.java` |
| Skip nếu `cheat_void` | `history_service/kafka/MatchResultKafkaListener.java` |
| Test | `profile_service/.../saga/SagaThreeMatchResultTest.java` |

### ✅ Đã làm
- history_service và profile_service đều consume `match.result` độc lập (fan-out qua consumer groups khác nhau)
- Bỏ qua lưu lịch sử nếu outcome = `cheat_void` (anticheat integration)
- Dedup check: `repo.existsByMatchIdAndPlayerId()` trước khi save

### ❌ Chưa làm
- **Không có compensation**: nếu history_service fail khi lưu → chỉ `log.error`, lịch sử mất, không retry
- **Không có compensation**: nếu profile_service fail khi cập nhật RP → RP không được cộng, không ai biết
- **Không có DLQ**: message bị drop hoàn toàn nếu xử lý fail
- **Không đồng bộ**: history có thể lưu thành công nhưng profile RP không được cập nhật → inconsistency

---

## Saga 4 — Session Cleanup (Forced Logout)

### Mục tiêu
Khi phát hiện đăng nhập trùng thiết bị hoặc tài khoản bị ban, invalidate session và dọn dẹp player khỏi tất cả service.

### Flow

```
auth_service (ban/duplicate login)
    │
    └─ Publish [user.session.invalidated]
                │
                ├──→ matchmaking_service: xóa player khỏi lobby queue
                │
                └──→ C++ game server (qua Kafka consumer): force logout player đang trong match
```

### Files liên quan
| Bước | File |
|------|------|
| Publish event | `auth_service/serviceImpl/SessionInvalidationProducer.java` |
| Dọn lobby | `matchmaking_service/kafka/SessionInvalidatedKafkaListener.java` |
| Force logout trong match | `C++ server: main.cpp` (consumer `user.session.invalidated`) |
| Test | `matchmaking_service/.../saga/SagaFourSessionCleanupTest.java` |

### ✅ Đã làm
- Publish `user.session.invalidated` khi ban hoặc duplicate login
- matchmaking_service xóa player khỏi `LobbyManager`
- C++ server nhận event và gửi `S2C_FORCE_LOGOUT` packet đến client

### ❌ Chưa làm
- **Không có compensation**: nếu `lobbyManager.removePlayer()` fail → player zombie trong queue
- **Không có DLQ**: message drop → player bị ban vẫn có thể ở trong match
- **Race condition**: player có thể vừa được match ngay trước khi event đến → match được tạo với player bị ban

---

## Tóm tắt

| Saga | Compensation | Retry | DLQ | Idempotency | Outbox |
|------|:---:|:---:|:---:|:---:|:---:|
| 1 — User Registration | ✅ | ❌ | ❌ | ❌ | ❌ |
| 2 — Purchase | ✅ (partial) | ❌ | ❌ | ❌ | ❌ |
| 3 — Match Result | ❌ | ❌ | ❌ | ✅ (dedup) | ❌ |
| 4 — Session Cleanup | ❌ | ❌ | ❌ | ❌ | ❌ |

### Rủi ro cao nhất cần xử lý
1. **Outbox pattern** cho Saga 1: DB commit + Kafka publish không atomic
2. **DLQ** cho Saga 3: lịch sử trận đấu mất silently nếu Kafka consumer fail
3. **Compensation của compensation** cho Saga 2: coin mất nếu refund cũng fail
4. **Race condition** cho Saga 4: player bị ban vẫn join được match trong window giữa ban và event propagation
