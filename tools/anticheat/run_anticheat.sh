#!/bin/bash
# Build và launch anticheat.exe (cần chạy với quyền Administrator để scan handles)

AC_WIN="D:\\Unity\\TankOnline\\game\\SE315.Q21\\tools\\anticheat"
AC_WSL="/mnt/d/Unity/TankOnline/game/SE315.Q21/tools/anticheat"

echo "[0/2] Killing old instance..."
powershell.exe -Command "Start-Process cmd -ArgumentList '/c taskkill /F /IM anticheat.exe' -Verb RunAs -Wait -WindowStyle Hidden" 2>/dev/null || true
sleep 2

echo "[1/2] Building anticheat.exe..."
# Dung printf de dam bao CRLF line endings (Windows batch can CRLF)
printf '@echo off\r\n' > "$AC_WSL/_build_tmp.bat"
printf 'call "C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\VC\\Auxiliary\\Build\\vcvars64.bat" > nul 2>&1\r\n' >> "$AC_WSL/_build_tmp.bat"
printf 'cd /d "D:\\Unity\\TankOnline\\game\\SE315.Q21\\tools\\anticheat"\r\n' >> "$AC_WSL/_build_tmp.bat"
printf 'cl /EHsc /W3 /std:c++17 /nologo anticheat.cpp /Fe:anticheat.exe /link ntdll.lib winhttp.lib\r\n' >> "$AC_WSL/_build_tmp.bat"

cmd.exe /c "D:\\Unity\\TankOnline\\game\\SE315.Q21\\tools\\anticheat\\_build_tmp.bat" 2>&1 \
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
