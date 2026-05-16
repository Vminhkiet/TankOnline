@echo off
echo ==================================================
echo       STARTING DEDICATED TANK SERVER (C++)
echo ==================================================
cd /d "%~dp0out\build\x64-Debug-vcpkg\server_tank\Debug"

echo Running server_tank.exe...
server_tank.exe

echo.
echo Server closed or crashed.
pause
