@echo off
echo ==================================================
echo       STARTING DEDICATED TANK SERVER (C++)
echo ==================================================
cd /d "%~dp0Tank\out\build\x64-Release\Release"

echo Running server_tank.exe...
server_tank.exe

echo.
echo Server closed or crashed.
pause
