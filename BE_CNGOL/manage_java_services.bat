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
echo 5. Start Matchmaking Service ONLY
echo 6. Exit
echo ==================================================
set /p choice="Select an option (1-6): "

if "%choice%"=="1" goto START_ALL
if "%choice%"=="2" goto START_DISCOVERY
if "%choice%"=="3" goto START_GATEWAY
if "%choice%"=="4" goto START_AUTH
if "%choice%"=="5" goto START_MATCHMAKING
if "%choice%"=="6" goto END

goto MENU

:START_ALL
echo [1/4] Starting Discovery Service (Eureka)...
start "Discovery Service" cmd /k "cd java-meta-services\discovery_service && mvnw.cmd spring-boot:run"
echo Waiting 15 seconds for Eureka to initialize...
timeout /t 15 /nobreak >nul
echo [2/4] Starting API Gateway...
start "API Gateway" cmd /k "cd java-meta-services\api_gateway && mvnw.cmd spring-boot:run"
echo [3/4] Starting Auth Service...
start "Auth Service" cmd /k "cd java-meta-services\auth_service && mvnw.cmd spring-boot:run"
echo [4/4] Starting Matchmaking Service...
start "Matchmaking Service" cmd /k "cd java-meta-services\matchmaking_service && mvnw.cmd spring-boot:run"
goto MENU

:START_DISCOVERY
start "Discovery Service" cmd /k "cd java-meta-services\discovery_service && mvnw.cmd spring-boot:run"
goto MENU

:START_GATEWAY
start "API Gateway" cmd /k "cd java-meta-services\api_gateway && mvnw.cmd spring-boot:run"
goto MENU

:START_AUTH
start "Auth Service" cmd /k "cd java-meta-services\auth_service && mvnw.cmd spring-boot:run"
goto MENU

:START_MATCHMAKING
start "Matchmaking Service" cmd /k "cd java-meta-services\matchmaking_service && mvnw.cmd spring-boot:run"
goto MENU

:END
exit
