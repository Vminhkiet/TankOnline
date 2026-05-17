---
name: reference-grafana-reading
description: How to read Grafana Dashboard 1 (C++ Game Server) panels and derive conclusions from metrics
metadata: 
  node_type: memory
  type: reference
  originSessionId: fb982b58-5659-4296-bc23-8d36087f8194
---

# Cách đọc Grafana Dashboard 1 — C++ Game Server

## Prometheus metrics endpoint
`http://172.25.203.168:9100/metrics/prometheus`

Key metrics:
- `tank_tick_duration_us_avg` — tick avg (µs)
- `tank_tick_duration_us_p99` — tick p99 worst 1% (µs)
- `tank_tick_duration_us_max` — spike max (µs)
- `tank_tick_budget_us` — budget = 16667µs (60 Hz)
- `tank_tick_overruns_total` — counter, tăng mỗi khi tick > budget
- `tank_active_matches` — số match đang chạy
- `tank_threadpool_queue_depth` — pending jobs trong pool (> 0 = bottleneck)
- `tank_process_memory_mb` — RSS memory MB
- `tank_kafka_events_consumed_total` — match.create đã nhận
- `tank_kafka_events_produced_total` — match.result đã gửi

---

## Panel 1 — "The Budget Line"

PromQL:
- `tank_tick_duration_us_avg` → đường xanh
- `tank_tick_duration_us_p99` → đường cam
- `vector(16667)` → đường đỏ đứt (ngân sách)

Cách đọc:
- p99 dưới đỏ mãi → HEALTHY
- p99 chạm/vượt đỏ → overrun, player thấy frame drop
- p99 cao nhưng avg thấp → spike ngắn (OS jitter), không phải game logic nặng
- Cả avg lẫn p99 đều tăng đều → game logic thực sự nặng hơn (thêm match / bullet)

---

## Panel 2 — "Stability Score / Overruns"

PromQL: `increase(tank_tick_overruns_total[$__range])`

Đọc: đếm bao nhiêu overrun trong time range đang xem.
- Xanh (<5): ổn định
- Vàng (5–10): cần chú ý
- Đỏ (>10): có vấn đề thực sự

---

## Panel 3 — "Load vs Latency"

PromQL:
- `tank_tick_duration_us_p99` → trục trái (đường cam)
- `tank_active_matches` → trục phải (cột xanh)

Cách đọc tương quan tải ↔ latency:
- Matches tăng → p99 tăng tuyến tính → scaling bình thường
- Matches tăng → p99 tăng vọt → bottleneck (pool contention / bullet collision)
- Matches = 0 → p99 vẫn cao → OS jitter thuần, không phải game logic

---

## Luồng phân tích hoàn chỉnh

```
Chạy stress test
      ↓
"The Budget Line"
  avg tăng tuyến tính?     → capacity scaling bình thường
  p99 tăng nhanh hơn avg?  → collision/spike là vấn đề
      ↓
"Load vs Latency"
  matches nhiều = p99 cao? → xác nhận O(N_matches) scaling
      ↓
"Stability Score"
  số overrun               → quantify mức độ ảnh hưởng gameplay
      ↓
KẾT LUẬN: sweet spot = X matches trước khi p99 > 80% budget
```

---

## Metrics chưa có panel nhưng có thể kết luận thêm

| Metric | Kết luận |
|--------|----------|
| `tank_threadpool_queue_depth > 0` liên tục | 8 workers không đủ, cần tăng pool |
| `tank_tick_duration_us_max` cao bất thường | Match init spike hay game logic? |
| `tank_process_memory_mb` tăng theo match | Memory leak hay bình thường? |
| `consumed - produced` | Số match đang còn sống = consumed_total - produced_total |
