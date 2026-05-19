@echo off
setlocal enabledelayedexpansion
title Tank Legends - Microservices Manager

cd /d "%~dp0"

:: =========================
:: MODE CONFIG
:: =========================
set MODE=DEV
set LAN_MODE=OFF
set LAN_IP=

:MENU
cls
echo ==================================================
echo        TANK LEGENDS MICROSERVICES MANAGER
echo ==================================================
echo Current Mode: %MODE%
if "%LAN_MODE%"=="ON" (
    echo LAN Mode:     ON  [IP: %LAN_IP%]
) else (
    echo LAN Mode:     OFF [localhost only]
)
echo --------------------------------------------------
echo  1. Toggle DEV / PROD mode
echo  2. Toggle LAN mode (auto-detect IP)
echo  3. Start ALL Services
echo  4. Start Discovery ONLY
echo  5. Start Auth ONLY
echo  6. Start Shop ONLY
echo  7. Start Matchmaking ONLY
echo  8. Start History ONLY
echo  9. Start API Gateway ONLY
echo 10. Start Profile ONLY
echo 11. Stop ALL Services
echo  0. Exit
echo ==================================================
set /p choice="Select option: "

if "%choice%"=="1" goto TOGGLE_MODE
if "%choice%"=="2" goto TOGGLE_LAN
if "%choice%"=="3" goto START_ALL
if "%choice%"=="4" goto START_DISCOVERY
if "%choice%"=="5" goto START_AUTH
if "%choice%"=="6" goto START_SHOP
if "%choice%"=="7" goto START_MATCHMAKING
if "%choice%"=="8" goto START_HISTORY
if "%choice%"=="9" goto START_GATEWAY
if "%choice%"=="10" goto START_PROFILE
if "%choice%"=="11" goto STOP_ALL
if "%choice%"=="0" goto END

goto MENU

:: =========================
:: MODE TOGGLE
:: =========================
:TOGGLE_MODE
if "%MODE%"=="DEV" (
    set MODE=PROD
) else (
    set MODE=DEV
)
goto MENU

:: =========================
:: LAN TOGGLE (auto-detect IP)
:: =========================
:TOGGLE_LAN
if "%LAN_MODE%"=="ON" (
    set LAN_MODE=OFF
    set LAN_IP=
    echo.
    echo LAN mode disabled. Services will use localhost.
) else (
    set LAN_MODE=ON
    set LAN_IP=
    for /f "usebackq tokens=*" %%a in (`powershell -NoProfile -Command "(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notlike '*Loopback*' -and $_.InterfaceAlias -notlike '*vEthernet*' -and $_.InterfaceAlias -notlike '*VirtualBox*' -and $_.InterfaceAlias -notlike '*VMware*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object -First 1).IPAddress"`) do (
        set LAN_IP=%%a
    )
    if "!LAN_IP!"=="" (
        echo [ERROR] Could not detect LAN IP.
        set LAN_MODE=OFF
    ) else (
        echo.
        echo LAN mode enabled. Detected IP: !LAN_IP!
        echo   - API Gateway will bind 0.0.0.0:8080
        echo   - Matchmaking will return server host: !LAN_IP!
    )
)
echo.
pause
goto MENU

:: =========================
:: START ALL (ORDER IMPORTANT)
:: =========================
:START_ALL

echo [1/7] Starting Discovery Service...
call :START_SERVICE discovery_service "Discovery Service"
timeout /t 15 /nobreak >nul

echo [2/7] Starting Auth Service...
call :START_SERVICE auth_service "Auth Service"
timeout /t 5 /nobreak >nul

echo [3/7] Starting Shop Service...
call :START_SERVICE shop "Shop Service"
timeout /t 5 /nobreak >nul

echo [4/7] Starting Matchmaking Service...
call :START_SERVICE matchmaking_service "Matchmaking Service"
timeout /t 5 /nobreak >nul

echo [5/7] Starting History Service...
call :START_SERVICE history_service "History Service"
timeout /t 5 /nobreak >nul

echo [6/7] Starting Profile Service...
call :START_SERVICE profile_service "Profile Service"
timeout /t 5 /nobreak >nul

echo Waiting for services to register with Eureka...
timeout /t 10 /nobreak >nul

echo [7/7] Starting API Gateway (LAST)...
call :START_SERVICE api_gateway "API Gateway"

echo.
if "%LAN_MODE%"=="ON" (
    echo All services started in LAN mode.
    echo Phone can connect to: http://%LAN_IP%:8080
) else (
    echo All services started successfully.
)
pause
goto MENU

:: =========================
:: SERVICE LAUNCHER
:: =========================
:START_SERVICE
set "SERVICE_DIR=%~1"
set "WINDOW_TITLE=%~2"

:: Write a temp launcher script for this service
set "LAUNCHER=%TEMP%\tank_launch_%SERVICE_DIR%.cmd"
(
    echo @echo off
    echo cd /d "%~dp0java-meta-services\%SERVICE_DIR%"
    if "%LAN_MODE%"=="ON" (
        echo set GAME_SERVER_HOST=%LAN_IP%
        echo set SERVER_ADDRESS=0.0.0.0
    )
    if "%MODE%"=="DEV" (
        echo mvn spring-boot:run
    ) else (
        echo for %%%%f in ^(target\*.jar^) do java -jar %%%%f
    )
    echo pause
) > "%LAUNCHER%"

start "%WINDOW_TITLE%" cmd /k "call "%LAUNCHER%""

goto :eof

:: =========================
:: INDIVIDUAL STARTS
:: =========================
:START_DISCOVERY
call :START_SERVICE discovery_service "Discovery Service"
goto MENU

:START_AUTH
call :START_SERVICE auth_service "Auth Service"
goto MENU

:START_SHOP
call :START_SERVICE shop "Shop Service"
goto MENU

:START_MATCHMAKING
call :START_SERVICE matchmaking_service "Matchmaking Service"
goto MENU

:START_HISTORY
call :START_SERVICE history_service "History Service"
goto MENU

:START_GATEWAY
call :START_SERVICE api_gateway "API Gateway"
goto MENU

:START_PROFILE
call :START_SERVICE profile_service "Profile Service"
goto MENU

:: =========================
:: STOP ALL
:: =========================
:STOP_ALL
echo Stopping all Java services...

taskkill /F /FI "WINDOWTITLE eq Discovery Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq Auth Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq Shop Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq Matchmaking Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq History Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq Profile Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq API Gateway*" >nul 2>&1

echo All services stopped.
timeout /t 2 /nobreak >nul
goto MENU

:: =========================
:: EXIT
:: =========================
:END
exit