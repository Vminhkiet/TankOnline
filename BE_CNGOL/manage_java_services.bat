@echo off
title Tank Legends - Microservices Manager
cd /d "%~dp0"

:MENU
cls
echo ==================================================
echo       TANK LEGENDS MICROSERVICES MANAGER
echo ==================================================
echo 1. Start ALL Services (Auto-sequence)
echo 2. Start Discovery Service ONLY
echo 3. Start API Gateway ONLY
echo 4. Start Auth Service ONLY
echo 5. Start Shop Service ONLY
echo 6. Start Matchmaking Service ONLY
echo 7. Start History Service ONLY
echo 8. Stop ALL Java Services
echo 9. Exit
echo ==================================================
set /p choice="Select an option (1-9): "

if "%choice%"=="1" goto START_ALL
if "%choice%"=="2" goto START_DISCOVERY
if "%choice%"=="3" goto START_GATEWAY
if "%choice%"=="4" goto START_AUTH
if "%choice%"=="5" goto START_SHOP
if "%choice%"=="6" goto START_MATCHMAKING
if "%choice%"=="7" goto START_HISTORY
if "%choice%"=="8" goto STOP_ALL
if "%choice%"=="9" goto END

goto MENU

:START_ALL
echo [1/6] Starting Discovery Service (Eureka)...
start "Discovery Service" cmd /k "cd /d "%~dp0java-meta-services\discovery_service" && java -jar target\discovery_service-0.0.1-SNAPSHOT.jar"
echo Waiting 20 seconds for Eureka to initialize...
timeout /t 20 /nobreak >nul

echo [2/6] Starting Auth Service...
start "Auth Service" cmd /k "cd /d "%~dp0java-meta-services\auth_service" && java -jar target\auth-service-0.0.1-SNAPSHOT.jar"
timeout /t 5 /nobreak >nul

echo [3/6] Starting Shop Service...
start "Shop Service" cmd /k "cd /d "%~dp0java-meta-services\shop" && java -jar target\shop_service-0.0.1-SNAPSHOT.jar"
timeout /t 5 /nobreak >nul

echo [4/6] Starting Matchmaking Service...
start "Matchmaking Service" cmd /k "cd /d "%~dp0java-meta-services\matchmaking_service" && java -jar target\matchmaking_service-0.0.1-SNAPSHOT.jar"
timeout /t 5 /nobreak >nul

echo [5/6] Starting History Service...
start "History Service" cmd /k "cd /d "%~dp0java-meta-services\history_service" && java -jar target\history_service-0.0.1-SNAPSHOT.jar"
timeout /t 5 /nobreak >nul

echo [6/6] Starting API Gateway (last)...
start "API Gateway" cmd /k "cd /d "%~dp0java-meta-services\api_gateway" && java -jar target\api_gateway-0.0.1-SNAPSHOT.jar"

echo.
echo All services started! Check Eureka at http://localhost:8761
pause
goto MENU

:START_DISCOVERY
start "Discovery Service" cmd /k "cd /d "%~dp0java-meta-services\discovery_service" && java -jar target\discovery_service-0.0.1-SNAPSHOT.jar"
goto MENU

:START_GATEWAY
start "API Gateway" cmd /k "cd /d "%~dp0java-meta-services\api_gateway" && java -jar target\api_gateway-0.0.1-SNAPSHOT.jar"
goto MENU

:START_AUTH
start "Auth Service" cmd /k "cd /d "%~dp0java-meta-services\auth_service" && java -jar target\auth-service-0.0.1-SNAPSHOT.jar"
goto MENU

:START_SHOP
start "Shop Service" cmd /k "cd /d "%~dp0java-meta-services\shop" && java -jar target\shop_service-0.0.1-SNAPSHOT.jar"
goto MENU

:START_MATCHMAKING
start "Matchmaking Service" cmd /k "cd /d "%~dp0java-meta-services\matchmaking_service" && java -jar target\matchmaking_service-0.0.1-SNAPSHOT.jar"
goto MENU

:START_HISTORY
start "History Service" cmd /k "cd /d "%~dp0java-meta-services\history_service" && java -jar target\history_service-0.0.1-SNAPSHOT.jar"
goto MENU

:STOP_ALL
echo Stopping all Java services...
taskkill /F /FI "WINDOWTITLE eq Discovery Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq API Gateway*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq Auth Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq Shop Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq Matchmaking Service*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq History Service*" >nul 2>&1
echo All services stopped.
timeout /t 2 /nobreak >nul
goto MENU

:END
exit
