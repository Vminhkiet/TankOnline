@echo off
echo ==================================================
echo       STARTING DEDICATED TANK SERVER (C++)
echo ==================================================

set EXE=%~dp0out\build\x64-Release\server_tank\Release\server_tank.exe

if not exist "%EXE%" (
    echo.
    echo [LOI] Chua tim thay server_tank.exe
    echo Hay chay build_server_tank.bat truoc de build server.
    echo.
    pause
    exit /b 1
)

echo Detecting WSL2 IP...
for /f %%i in ('wsl ip -4 addr show eth0 ^| wsl grep -oP "(?<=inet )[\d.]+"') do set WSL2_IP=%%i

if "%WSL2_IP%"=="" (
    echo [WARN] Could not detect WSL2 IP, using fallback 172.23.79.122
    set WSL2_IP=172.23.79.122
)

echo Using Kafka broker: %WSL2_IP%:9092
set KAFKA_BROKERS=%WSL2_IP%:9092

cd /d "%~dp0out\build\x64-Release\server_tank\Release"
echo Running server_tank.exe...
server_tank.exe

echo.
echo Server closed or crashed.
pause
