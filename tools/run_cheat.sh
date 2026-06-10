#!/bin/bash
# Build và launch tank_hp_hack.exe từ WSL2

TOOLS_WIN="D:\\Unity\\TankOnline\\game\\SE315.Q21\\tools"
TOOLS_WSL="/mnt/d/Unity/TankOnline/game/SE315.Q21/tools"

# ── Tạo temp .bat rồi chạy ───────────────────────────────────────────────────
# Kill process cu neu dang chay (tranh LNK1104 file lock)
echo "[0/2] Killing old instance..."
# Chay bat de kill voi quyen admin (tranh Access Denied voi process RunAs)
cat > "$TOOLS_WSL/_kill.bat" << 'KILLEOF'
@echo off
taskkill /F /IM tank_hp_hack.exe 2>nul
exit /b 0
KILLEOF
powershell.exe -Command "Start-Process cmd -ArgumentList '/c ${TOOLS_WIN}\\_kill.bat' -Verb RunAs -Wait -WindowStyle Hidden" 2>/dev/null || true
rm -f "$TOOLS_WSL/_kill.bat"
sleep 2

echo "[1/2] Building tank_hp_hack.exe..."

cat > "$TOOLS_WSL/_build_tmp.bat" << 'EOF'
@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" > nul 2>&1
cd /d "D:\Unity\TankOnline\game\SE315.Q21\tools"
cl /EHsc /W3 /std:c++17 /nologo tank_hp_hack.cpp /Fe:tank_hp_hack.exe /link user32.lib gdi32.lib
EOF

cmd.exe /c "D:\\Unity\\TankOnline\\game\\SE315.Q21\\tools\\_build_tmp.bat" 2>&1 \
    | grep -Ev "^$|Copyright|Microsoft"

rm -f "$TOOLS_WSL/_build_tmp.bat" "$TOOLS_WSL/tank_hp_hack.obj" 2>/dev/null

if [ ! -f "$TOOLS_WSL/tank_hp_hack.exe" ]; then
    echo "[-] Build FAILED"
    exit 1
fi

echo "[+] Build OK → tank_hp_hack.exe"

# ── Launch trên Windows với quyền Admin ──────────────────────────────────────
echo "[2/2] Launching as Administrator..."
powershell.exe -Command "Start-Process -FilePath '${TOOLS_WIN}\\tank_hp_hack.exe' -Verb RunAs" 2>/dev/null
echo "[+] Tool dang chay tren Windows."
