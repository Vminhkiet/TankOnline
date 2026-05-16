@echo off
setlocal enabledelayedexpansion
title Tank Legends - Microservices Manager

cd /d "%~dp0"

:: =========================
:: MODE CONFIG
:: =========================
set MODE=DEV

:MENU
cls
echo ==================================================
echo        TANK LEGENDS MICROSERVICES MANAGER
echo ==================================================
echo Current Mode: %MODE%
echo --------------------------------------------------
echo 1. Toggle DEV / PROD mode
echo 2. Start ALL Services
echo 3. Start Discovery ONLY
echo 4. Start Auth ONLY
echo 5. Start Shop ONLY
echo 6. Start Matchmaking ONLY
echo 7. Start History ONLY
echo 8. Start API Gateway ONLY
echo 9. Stop ALL Services
echo 0. Exit
echo ==================================================
set /p choice="Select option: "

if "%choice%"=="1" goto TOGGLE_MODE
if "%choice%"=="2" goto START_ALL
if "%choice%"=="3" goto START_DISCOVERY
if "%choice%"=="4" goto START_AUTH
if "%choice%"=="5" goto START_SHOP
if "%choice%"=="6" goto START_MATCHMAKING
if "%choice%"=="7" goto START_HISTORY
if "%choice%"=="8" goto START_GATEWAY
if "%choice%"=="9" goto STOP_ALL
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
:: START ALL (ORDER IMPORTANT)
:: =========================
:START_ALL

echo [1/6] Starting Discovery Service...
call :START_SERVICE discovery_service "Discovery Service"
timeout /t 15 /nobreak >nul

echo [2/6] Starting Auth Service...
call :START_SERVICE auth_service "Auth Service"
timeout /t 5 /nobreak >nul

echo [3/6] Starting Shop Service...
call :START_SERVICE shop "Shop Service"
timeout /t 5 /nobreak >nul

echo [4/6] Starting Matchmaking Service...
call :START_SERVICE matchmaking_service "Matchmaking Service"
timeout /t 5 /nobreak >nul

echo [5/6] Starting History Service...
call :START_SERVICE history_service "History Service"
timeout /t 5 /nobreak >nul

echo Waiting for services to register with Eureka...
timeout /t 10 /nobreak >nul

echo [6/6] Starting API Gateway (LAST)...
call :START_SERVICE api_gateway "API Gateway"

echo.
echo All services started successfully.
pause
goto MENU

:: =========================
:: SERVICE LAUNCHER
:: =========================
:START_SERVICE
set "SERVICE_DIR=%~1"
set "WINDOW_TITLE=%~2"

if "%MODE%"=="DEV" (
    start "%WINDOW_TITLE%" cmd /k "cd /d "%~dp0java-meta-services\%SERVICE_DIR%" && call mvnw.cmd spring-boot:run || pause"
) else (
    for %%f in ("%~dp0java-meta-services\%SERVICE_DIR%\target\*.jar") do (
        start "%WINDOW_TITLE%" cmd /k "cd /d "%~dp0java-meta-services\%SERVICE_DIR%" && java -jar "%%f""
    )
)

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
taskkill /F /FI "WINDOWTITLE eq API Gateway*" >nul 2>&1

echo All services stopped.
timeout /t 2 /nobreak >nul
goto MENU

:: =========================
:: EXIT
:: =========================
:END
exit