#!/bin/bash
# run_km.sh  —  Entry point duy nhat cho anticheat kernel-mode
# Lam tat ca: WDK install → test-signing → build → sign → load
#
# Usage:
#   ./run_km.sh          — setup + load (full auto, skip build neu da build)
#   ./run_km.sh load     — chi load driver (khong build lai)
#   ./run_km.sh unload   — unload driver
#   ./run_km.sh status   — kiem tra trang thai

KM_WIN="D:\\Unity\\TankOnline\\SE315.Q21\\tools\\anticheat_km"
KM_WSL="/mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat_km"
ACTION=${1:-setup}

case "$ACTION" in
setup|"")
    echo "[*] Starting anticheat setup (Admin required)..."
    powershell.exe -NoProfile -ExecutionPolicy Bypass \
        -File "${KM_WIN}\\setup_anticheat.ps1" 2>&1
    ;;
load)
    echo "[*] Loading driver (Admin required)..."
    powershell.exe -NoProfile -Command \
        "Start-Process '${KM_WIN}\\loader\\loader.exe' -ArgumentList 'load' -Verb RunAs -Wait -WindowStyle Hidden" 2>/dev/null
    echo "[+] Done. Kiem tra: bash $0 status"
    ;;
unload)
    echo "[*] Unloading driver..."
    powershell.exe -NoProfile -Command \
        "Start-Process '${KM_WIN}\\loader\\loader.exe' -ArgumentList 'unload' -Verb RunAs -Wait -WindowStyle Hidden" 2>/dev/null
    echo "[+] Done."
    ;;
status)
    powershell.exe -NoProfile -Command \
        "& '${KM_WIN}\\loader\\loader.exe' status" 2>/dev/null
    ;;
*)
    echo "Usage: $0 [setup|load|unload|status]"
    ;;
esac
