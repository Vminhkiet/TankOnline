#!/usr/bin/env bash
set -euo pipefail

echo ""
echo " ============================================================"
echo "  Worst-Case Live Benchmark  -  Tank Online"
echo " ============================================================"
echo ""
echo " Cach dung:"
echo "   ./run_wc_benchmark.sh               (5 players, 1 match)"
echo "   ./run_wc_benchmark.sh 10            (10 players, 1 match)"
echo "   ./run_wc_benchmark.sh 5 3           (5 players, 3 matches - do overhead)"
echo "   ./run_wc_benchmark.sh rebuild       (rebuild exe + 5 players)"
echo "   ./run_wc_benchmark.sh rebuild 5 3   (rebuild + 5 players + 3 matches)"
echo ""

# ── Parse arguments ────────────────────────────────────────────────────────────
PLAYERS=5
MATCHES=1
DO_BUILD=0
_P_SET=0
_M_SET=0

if [[ $# -eq 0 ]]; then
    read -rp "  Players (Enter = 5): " PLAYERS_INPUT
    [[ -n "$PLAYERS_INPUT" ]] && PLAYERS="$PLAYERS_INPUT"

    echo ""
    echo " Nhap so matches/tick de do overhead (Enter = 1 = khong do overhead):"
    read -rp "  Matches: " MATCHES_INPUT
    [[ -n "$MATCHES_INPUT" ]] && MATCHES="$MATCHES_INPUT"

    echo ""
    read -rp "  Rebuild exe? (y = co, Enter = khong): " REBUILD_INPUT
    [[ "${REBUILD_INPUT,,}" == "y" ]] && DO_BUILD=1
    echo ""
else
    for arg in "$@"; do
        if [[ "${arg,,}" == "rebuild" ]]; then
            DO_BUILD=1
        elif [[ $_P_SET -eq 0 ]]; then
            PLAYERS="$arg"; _P_SET=1
        elif [[ $_M_SET -eq 0 ]]; then
            MATCHES="$arg"; _M_SET=1
        fi
    done
fi

# ── Đường dẫn ─────────────────────────────────────────────────────────────────
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WIN_ROOT="/mnt/d/Unity/TankOnline/game/SE315.Q21"

EXE_DIR="$WIN_ROOT/Tank/out/build/x64-Release/bench_worst_case"
EXE="$EXE_DIR/Release/bench_wc_live.exe"
VCXPROJ_DST="$EXE_DIR/bench_wc_live.vcxproj"   # đã có sẵn, không cần gen
MSBUILD="/mnt/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
AGENT="$ROOT/Tank/bench_wc_live_agent.py"

echo " Players  : $PLAYERS"
echo " Matches  : $MATCHES"
echo " Rebuild  : $DO_BUILD"
echo ""

# ── STEP 1: Kiểm tra Python ────────────────────────────────────────────────────
echo "[STEP 1] Kiem tra Python..."
python3 --version || { echo "[LOI] Python khong tim thay!"; exit 1; }
echo " OK."
echo ""

# ── STEP 2: Build nếu cần ────────────────────────────────────────────────────────
[[ ! -f "$EXE" ]] && DO_BUILD=1

if [[ $DO_BUILD -eq 1 ]]; then
    echo "[STEP 2] Build bench_wc_live.exe..."

    [[ ! -f "$VCXPROJ_DST" ]] && { echo "[LOI] Khong tim thay: $VCXPROJ_DST"; exit 1; }

    # Kill Windows exe cũ
    powershell.exe -NoProfile -Command "taskkill /F /IM bench_wc_live.exe /T" 2>/dev/null || true
    sleep 1

    # Build — wslpath -w để convert Linux path → Windows path cho MSBuild
    VCXPROJ_WIN=$(wslpath -w "$VCXPROJ_DST")
    "$MSBUILD" "$VCXPROJ_WIN" \
        /p:Configuration=Release /p:Platform=x64 \
        /p:BuildProjectReferences=false /m /v:m \
        || { echo "[LOI] Build that bai!"; exit 1; }

    echo "[OK] Build xong."
    echo ""
else
    echo "[STEP 2] Bo qua build (dung exe co san)."
    echo ""
fi

# ── STEP 3: Kiểm tra exe ──────────────────────────────────────────────────────
[[ ! -f "$EXE" ]] && { echo "[LOI] Khong tim thay exe. Chay lai voi: rebuild $PLAYERS $MATCHES"; exit 1; }

# ── STEP 4: Start Docker ───────────────────────────────────────────────────────
echo "[STEP 3] Start Docker (Prometheus + Grafana)..."
docker start prometheus >/dev/null 2>&1 && echo " Prometheus OK" || echo " Prometheus: skip"
docker start grafana    >/dev/null 2>&1 && echo " Grafana OK"    || echo " Grafana: skip"
sleep 2
echo ""

# ── STEP 5: Kill process cũ ───────────────────────────────────────────────────
echo "[STEP 4] Don dep process cu..."
fuser -k 9103/tcp 2>/dev/null || true
powershell.exe -NoProfile -Command "taskkill /F /IM bench_wc_live.exe /T" 2>/dev/null || true
sleep 1
echo " Done."
echo ""

# ── STEP 6: Ghi config ────────────────────────────────────────────────────────
echo "[STEP 5] Ghi config: players=$PLAYERS matches=$MATCHES"
echo "{\"players\": $PLAYERS, \"matches\": $MATCHES}" > "$ROOT/bench_wc_config.json"
echo " Done."
echo ""

# ── STEP 7: Start agent ───────────────────────────────────────────────────────
echo "[STEP 6] Khoi dong agent..."
python3 "$AGENT" --players="$PLAYERS" --matches="$MATCHES" &
AGENT_PID=$!
echo " Agent PID: $AGENT_PID"
echo " Done."
echo ""

# ── STEP 8: Chờ warmup ────────────────────────────────────────────────────────
echo "[STEP 7] Cho agent + warmup (~15 giay)..."
sleep 15
echo " Done."
echo ""

# ── STEP 9: Mở Grafana ────────────────────────────────────────────────────────
echo "[STEP 8] Mo Grafana dashboard..."
powershell.exe -NoProfile -Command "Start-Process 'http://localhost:3000/d/wc-live-bench/worst-case-live-benchmark'" 2>/dev/null \
    || echo " -> Mo tay: http://localhost:3000/d/wc-live-bench/worst-case-live-benchmark"
echo ""

# ── Thông báo ─────────────────────────────────────────────────────────────────
echo " ============================================================"
if [[ $MATCHES -gt 1 ]]; then
    echo "  OVERHEAD MODE: $MATCHES matches/tick"
    echo "  Scroll xuong cuoi Grafana de thay panel Overhead/Switch"
else
    echo "  NORMAL MODE: 1 match/tick"
fi
echo ""
echo "  Players : $PLAYERS"
echo "  Matches : $MATCHES"
echo "  Grafana : http://localhost:3000/d/wc-live-bench/..."
echo "  Control : http://localhost:9103/"
echo " ============================================================"
echo ""
echo " Nhan Ctrl+C de dung agent (PID $AGENT_PID)..."
wait "$AGENT_PID"
