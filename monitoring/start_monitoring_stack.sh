#!/bin/bash
# Khởi động Prometheus + Grafana bằng Docker
# Chạy: bash monitoring/start_monitoring_stack.sh

set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=== Starting Prometheus ==="
docker rm -f prometheus 2>/dev/null || true
docker run -d --name prometheus \
  -p 9090:9090 \
  -v "$SCRIPT_DIR/prometheus.yml:/etc/prometheus/prometheus.yml:ro" \
  -v "$SCRIPT_DIR/alert_rules.yml:/etc/prometheus/alert_rules.yml:ro" \
  --add-host=host.docker.internal:host-gateway \
  prom/prometheus:latest \
  --config.file=/etc/prometheus/prometheus.yml \
  --storage.tsdb.retention.time=7d
echo "  Prometheus: http://localhost:9090"

echo "=== Starting Grafana ==="
docker rm -f grafana 2>/dev/null || true
docker run -d --name grafana \
  -p 3000:3000 \
  -e GF_SECURITY_ADMIN_PASSWORD=admin \
  -e GF_USERS_ALLOW_SIGN_UP=false \
  -e GF_AUTH_ANONYMOUS_ENABLED=true \
  -e GF_AUTH_ANONYMOUS_ORG_ROLE=Viewer \
  grafana/grafana:latest
echo "  Grafana: http://localhost:3000  (admin/admin)"

echo ""
echo "Đợi ~10s để các container khởi động..."
sleep 10

# Add Prometheus datasource to Grafana via API
curl -sf -X POST http://admin:admin@localhost:3000/api/datasources \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Prometheus",
    "type": "prometheus",
    "url": "http://host.docker.internal:9090",
    "access": "proxy",
    "isDefault": true
  }' > /dev/null && echo "  Datasource Prometheus added to Grafana" || echo "  (datasource may already exist)"

echo ""
echo "=== Stack ready ==="
echo "  Prometheus targets : http://localhost:9090/targets"
echo "  Grafana dashboard  : http://localhost:3000"
echo "  Import dashboard   : Grafana → + → Import → upload monitoring/grafana_dashboard.json"
