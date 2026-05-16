@echo off
echo ==================================================
echo       STARTING DEDICATED TANK SERVER (C++)
echo ==================================================

set EXE=%~dp0Tank\out\build\x64-Release\server_tank\Release\server_tank.exe

if not exist "%EXE%" (
    echo.
    echo [LOI] Chua tim thay server_tank.exe
    echo Hay chay build_server_tank.bat truoc de build server.
    echo.
    pause
    exit /b 1
)

cd /d "%~dp0Tank\out\build\x64-Release\server_tank\Release"
echo Running server_tank.exe...
set KAFKA_BROKERS=localhost:9092
server_tank.exe

echo.
echo Server closed or crashed.
pause
