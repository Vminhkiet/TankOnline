# Hướng dẫn sử dụng Worst-Case Live Benchmark

## Bước 1 — Build bench_wc_live.exe

```bash
# Từ WSL2 hoặc Developer Command Prompt
cd Tank
cmake --build out/build/x64-Release --target bench_wc_live
```

Hoặc mở Visual Studio → Build → bench_wc_live

## Bước 2 — Chạy agent (WSL2)

```bash
cd /mnt/c/Users/ADMIN/Desktop/New\ folder\ \(5\)/SE315.Q21
python3 Tank/bench_wc_live_agent.py --players=10
```

Agent tự:
- Tìm bench_wc_live.exe
- Khởi động exe với `--players=10`
- Expose metrics tại http://localhost:9103/metrics/prometheus

## Bước 3 — Kiểm tra

- **Control UI**: http://localhost:9103/
- **Prometheus metrics**: http://localhost:9103/metrics/prometheus
- **JSON status**: http://localhost:9103/status

## Bước 4 — Import Grafana Dashboard

1. Grafana → Dashboards → Import
2. Upload file: `monitoring/grafana_wc_live_dashboard.json`
3. Chọn datasource Prometheus → Import

## Bước 5 — Đổi số player

Có 2 cách:
1. **Qua UI**: Mở http://localhost:9103/ → Nhập số player → Nhấn "Áp dụng"
2. **Qua API**: `curl -X POST http://localhost:9103/config -H "Content-Type: application/json" -d '{"players":5}'`

## Metrics có trên Grafana

| Metric | Mô tả |
|--------|-------|
| `wc_total_p99_us` | P99 tick time (µs) — **dùng tính GPC** |
| `wc_gpc` | GPC = floor(16667 / P99) |
| `wc_budget_pct` | % budget tick 60Hz |
| `wc_physics_p99_us` | Physics phase P99 (bottleneck) |
| `wc_bullet_p99_us` | Bullet phase P99 |
| `wc_snap_p99_us` | Snap phase P99 |
| `wc_players` | Số player hiện tại |
| `wc_steady_bullets` | Số đạn steady-state |

## Log format

bench_wc_live.exe xuất log mỗi 600 ticks (~10s):

```
[WC_Live] players=10 warmup=done steady_bullets=590
[WC_Phase] players=10 window=1 bullet_avg=188us physics_avg=3285us ...
[WC_Final] players=10 window=1 ... total_p99=4728us gpc=3 budget_pct=28.4 ...
```

Agent parse `[WC_Final]` để cập nhật Prometheus metrics.
