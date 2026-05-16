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
echo 3. Start Auth Service ONLY
echo 4. Start Matchmaking Service ONLY
echo 5. Start Shop Service ONLY
echo 6. Start Monitoring Service ONLY
echo 7. Start API Gateway ONLY
echo 8. Exit
echo ==================================================
set /p choice="Select an option (1-8): "

if "%choice%"=="1" goto START_ALL
if "%choice%"=="2" goto START_DISCOVERY
if "%choice%"=="3" goto START_AUTH
if "%choice%"=="4" goto START_MATCHMAKING
if "%choice%"=="5" goto START_SHOP
if "%choice%"=="6" goto START_MONITORING
if "%choice%"=="7" goto START_GATEWAY
if "%choice%"=="8" goto END

goto MENU

:START_ALL
echo [1/6] Starting Discovery Service (Eureka)...
start "Discovery Service" cmd /k "cd /d %~dp0java-meta-services\discovery_service && call .\mvnw.cmd spring-boot:run || pause"

echo Waiting 15 seconds for Eureka to initialize...
timeout /t 15 /nobreak >nul

echo [2/6] Starting Auth Service...
start "Auth Service" cmd /k "cd /d %~dp0java-meta-services\auth_service && call .\mvnw.cmd spring-boot:run || pause"

echo [3/6] Starting Matchmaking Service...
start "Matchmaking Service" cmd /k "cd /d %~dp0java-meta-services\matchmaking_service && call .\mvnw.cmd spring-boot:run || pause"

echo [4/6] Starting Shop Service...
start "Shop Service" cmd /k "cd /d %~dp0java-meta-services\shop && call .\mvnw.cmd spring-boot:run || pause"

echo [5/6] Starting Monitoring Service...
start "Monitoring Service" cmd /k "cd /d %~dp0java-meta-services\monitoring_service && call .\mvnw.cmd spring-boot:run || pause"

echo Waiting 15 seconds for services to register with Eureka...
timeout /t 15 /nobreak >nul

echo [6/6] Starting API Gateway...
start "API Gateway" cmd /k "cd /d %~dp0java-meta-services\api_gateway && call .\mvnw.cmd spring-boot:run || pause"

goto MENU

:START_DISCOVERY
start "Discovery Service" cmd /k "cd /d %~dp0java-meta-services\discovery_service && call .\mvnw.cmd spring-boot:run || pause"
goto MENU

:START_AUTH
start "Auth Service" cmd /k "cd /d %~dp0java-meta-services\auth_service && call .\mvnw.cmd spring-boot:run || pause"
goto MENU

:START_MATCHMAKING
start "Matchmaking Service" cmd /k "cd /d %~dp0java-meta-services\matchmaking_service && call .\mvnw.cmd spring-boot:run || pause"
goto MENU

:START_SHOP
start "Shop Service" cmd /k "cd /d %~dp0java-meta-services\shop && call .\mvnw.cmd spring-boot:run || pause"
goto MENU

:START_MONITORING
start "Monitoring Service" cmd /k "cd /d %~dp0java-meta-services\monitoring_service && call .\mvnw.cmd spring-boot:run || pause"
goto MENU

:START_GATEWAY
start "API Gateway" cmd /k "cd /d %~dp0java-meta-services\api_gateway && call .\mvnw.cmd spring-boot:run || pause"
goto MENU

:END
exit
