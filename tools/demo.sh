#!/bin/bash
# demo.sh — Demo AntiCheat
# Usage:
#   ./demo.sh           — demo 2 giai đoạn (cheat hoạt động → bị phát hiện)
#   ./demo.sh nocheat   — chỉ chạy cheat không có anticheat
#   ./demo.sh ac        — chỉ chạy anticheat phát hiện cheat
#   ./demo.sh stop      — dừng tất cả

CHEAT_EXE="D:\\Unity\\TankOnline\\SE315.Q21\\tools\\tank_hp_hack.exe"
AC_EXE="D:\\Unity\\TankOnline\\SE315.Q21\\tools\\anticheat\\anticheat.exe"
PROJECT_DIR="/home/minhk/project/SE315.Q21"

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
CYAN='\033[0;36m'; BOLD='\033[1m'; NC='\033[0m'

info()    { echo -e "${CYAN}[*]${NC} $1"; }
success() { echo -e "${GREEN}[+]${NC} $1"; }
warn()    { echo -e "${YELLOW}[!]${NC} $1"; }
error()   { echo -e "${RED}[-]${NC} $1"; }
title()   { echo -e "\n${BOLD}$1${NC}\n"; }

MODE=${1:-"full"}

# ── Helpers ──────────────────────────────────────────────────────────────────

kill_cheat() {
    powershell.exe -NoProfile -Command \
        "Stop-Process -Name tank_hp_hack -Force -ErrorAction SilentlyContinue" 2>/dev/null
}

kill_ac() {
    powershell.exe -NoProfile -Command \
        "Stop-Process -Name anticheat -Force -ErrorAction SilentlyContinue" 2>/dev/null
}

start_cheat() {
    kill_cheat
    sleep 1
    powershell.exe -NoProfile -Command \
        "Start-Process '${CHEAT_EXE}' -WindowStyle Normal" 2>/dev/null
}

start_ac() {
    kill_ac
    sleep 1
    powershell.exe -NoProfile -Command \
        "Start-Process '${AC_EXE}' -Verb RunAs -WindowStyle Normal" 2>/dev/null
}

check_backend() {
    if curl -s http://localhost:8761/actuator/health 2>/dev/null | grep -q "UP"; then
        success "Backend đang chạy."
    else
        warn "Backend chưa UP. Khởi động..."
        cd "$PROJECT_DIR" && bash start.sh
        sleep 30
    fi
}

# ── Modes ─────────────────────────────────────────────────────────────────────

stop_all() {
    info "Dừng cheat tool và anticheat..."
    kill_cheat
    kill_ac
    success "Đã dừng tất cả."
    exit 0
}

demo_nocheat() {
    echo -e "${RED}╔════════════════════════════════════════╗${NC}"
    echo -e "${RED}║  GIAI ĐOẠN 1: CHEAT (KHÔNG ANTICHEAT) ║${NC}"
    echo -e "${RED}╚════════════════════════════════════════╝${NC}"

    echo ""
    echo -e "  Kết quả mong đợi:"
    echo -e "  ${GREEN}→${NC} Cheat tool đọc được memory game"
    echo -e "  ${GREEN}→${NC} Overlay hiện vị trí + HP tất cả người chơi"
    echo ""
    read -p "  [Enter] chạy cheat tool..."
    start_cheat
    success "Cheat tool đang chạy."
}

demo_ac() {
    echo -e "${GREEN}╔══════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║  GIAI ĐOẠN 2: ANTICHEAT PHÁT HIỆN CHEAT     ║${NC}"
    echo -e "${GREEN}╚══════════════════════════════════════════════╝${NC}"

    echo ""
    echo -e "  Kết quả mong đợi:"
    echo -e "  ${CYAN}→${NC} Cửa sổ AntiCheat hiện:"
    echo -e "  ${CYAN}     [!!] CHEAT DETECTED${NC}"
    echo -e "  ${CYAN}          PID     : <pid của tank_hp_hack>${NC}"
    echo -e "  ${CYAN}          Process : tank_hp_hack.exe${NC}"
    echo -e "  ${CYAN}          Reason  : Matches known cheat name${NC}"
    echo ""
    read -p "  [Enter] khởi động AntiCheat (cần UAC → bấm Yes)..."
    start_ac
    success "AntiCheat đang chạy — quan sát cửa sổ anticheat.exe phát hiện cheat."
}

demo_full() {
    echo ""
    echo -e "${BOLD}╔══════════════════════════════════════════════╗${NC}"
    echo -e "${BOLD}║   SE315.Q21 — AntiCheat Kernel-Mode Demo     ║${NC}"
    echo -e "${BOLD}╚══════════════════════════════════════════════╝${NC}"
    echo ""

    check_backend

    echo ""
    echo -e "${YELLOW}Demo 2 giai đoạn:${NC}"
    echo "  1. Cheat tool hoạt động — đọc được vị trí + HP người chơi"
    echo "  2. Bật AntiCheat → Phát hiện cheat ngay lập tức"
    echo ""
    read -p "  [Enter] bắt đầu demo..."

    # ── Giai đoạn 1 ──────────────────────────────────────────────────────────
    echo ""
    demo_nocheat

    echo ""
    echo -e "  ${YELLOW}Quan sát overlay — cheat đang đọc được vị trí + HP người chơi.${NC}"
    echo ""
    read -p "  [Enter] tiếp tục bật AntiCheat để phát hiện cheat..."

    # ── Giai đoạn 2 ──────────────────────────────────────────────────────────
    echo ""
    demo_ac

    echo ""
    echo -e "${GREEN}╔════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║              DEMO HOÀN THÀNH           ║${NC}"
    echo -e "${GREEN}╚════════════════════════════════════════╝${NC}"
    echo ""
    echo "  Dừng tất cả:  bash $(basename $0) stop"
    echo ""
}

# ── Main ─────────────────────────────────────────────────────────────────────
case "$MODE" in
    full|"")  demo_full    ;;
    nocheat)  check_backend; demo_nocheat ;;
    ac)       check_backend; demo_ac      ;;
    stop)     stop_all     ;;
    *)
        echo "Usage: $0 [full|nocheat|ac|stop]"
        echo "  full    — demo 2 giai đoạn: cheat hoạt động → bị chặn (default)"
        echo "  nocheat — chỉ giai đoạn 1: cheat không bị chặn"
        echo "  ac      — chỉ giai đoạn 2: cheat bị chặn"
        echo "  stop    — dừng tất cả"
        ;;
esac
