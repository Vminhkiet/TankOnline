#!/bin/bash
# Build và launch anticheat.exe (cần chạy với quyền Administrator để scan handles)

AC_WIN="D:\\Unity\\TankOnline\\SE315.Q21\\tools\\anticheat"
AC_WSL="/mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat"

echo "[1/2] Building anticheat.exe..."
cat > "$AC_WSL/_build_tmp.bat" << 'EOF'
@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" > nul 2>&1
cd /d "D:\Unity\TankOnline\SE315.Q21\tools\anticheat"
cl /EHsc /W3 /std:c++17 /nologo anticheat.cpp /Fe:anticheat.exe /link ntdll.lib
EOF

cmd.exe /c "D:\\Unity\\TankOnline\\SE315.Q21\\tools\\anticheat\\_build_tmp.bat" 2>&1 \
    | grep -Ev "^$|Copyright|Microsoft"

rm -f "$AC_WSL/_build_tmp.bat" "$AC_WSL/anticheat.obj" 2>/dev/null

if [ ! -f "$AC_WSL/anticheat.exe" ]; then
    echo "[-] Build FAILED"
    exit 1
fi
echo "[+] Build OK → anticheat.exe"

echo "[2/2] Launching as Administrator..."
powershell.exe -Command "Start-Process -FilePath '${AC_WIN}\\anticheat.exe' -Verb RunAs" 2>/dev/null
echo "[+] AntiCheat dang chay tren Windows."
