@echo off
:: Rebuild + chay benchmark do overhead task-switch (5 players, 3 matches/tick)

if "%LAUNCHED%"=="" (
    set "LAUNCHED=1"
    cmd /k "%~f0" %*
    exit /b
)

setlocal EnableDelayedExpansion
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

set "PLAYERS=5"
set "MATCHES=3"
if not "%~1"=="" set "PLAYERS=%~1"
if not "%~2"=="" set "MATCHES=%~2"

echo.
echo  ============================================================
echo   Worst-Case OVERHEAD Benchmark
echo   Players=%PLAYERS%  Matches/tick=%MATCHES%
echo  ============================================================
echo.
echo  Overhead per switch = (wall(K) - K x single) / (K-1)
echo.

:: --- Phan build ---
set "EXE_DIR=%ROOT%\Tank\out\build\x64-Release\bench_worst_case"
set "EXE=%EXE_DIR%\bench_wc_live.exe"
set "VCXPROJ_SRC=%EXE_DIR%\bench_worst_case.vcxproj"
set "VCXPROJ_DST=%EXE_DIR%\bench_wc_live.vcxproj"
set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
set "STAMP1=%EXE_DIR%\CMakeFiles\generate.stamp"
set "STAMP2=%EXE_DIR%\CMakeFiles\generate.stamp.depend"

echo [BUILD] Tao bench_wc_live.vcxproj...
set "PS_SRC=%VCXPROJ_SRC%"
set "PS_DST=%VCXPROJ_DST%"
powershell -NoProfile -Command "(Get-Content $env:PS_SRC) -replace 'bench_worst_case_spread','bench_wc_live' -replace '<ProjectName>bench_worst_case','<ProjectName>bench_wc_live' -replace 'bench_worst_case\.dir','bench_wc_live.dir' | Set-Content $env:PS_DST"

echo [BUILD] Touch stamp files...
powershell -NoProfile -Command "$now=Get-Date; @($env:STAMP1,$env:STAMP2) | ForEach-Object { if(Test-Path $_){ (Get-Item $_).LastWriteTime=$now } else { New-Item -ItemType File $_ -Force | Out-Null } }"

echo [BUILD] MSBuild bench_wc_live...
set "PS_VCXPROJ=%VCXPROJ_DST%"
set "PS_MSBUILD=%MSBUILD%"
powershell -NoProfile -Command "& $env:PS_MSBUILD $env:PS_VCXPROJ /p:Configuration=Release /p:Platform=x64 /p:BuildProjectReferences=false /m /v:m"
if errorlevel 1 (
    echo [LOI] Build that bai!
    goto :end
)

:: Copy exe
set "BUILT_EXE=%EXE_DIR%\Release\bench_worst_case.exe"
powershell -NoProfile -Command "Copy-Item $env:BUILT_EXE $env:EXE -Force"
echo [BUILD] Done: %EXE%
echo.

:: --- Phan chay ---
echo [INFO] Ghi config: players=%PLAYERS% matches=%MATCHES%
echo {"players": %PLAYERS%, "matches": %MATCHES%} > "%ROOT%\bench_wc_config.json"

:: Start Docker
docker start prometheus >nul 2>&1
docker start grafana    >nul 2>&1

:: Kill process cu
for /f "tokens=5" %%p in ('netstat -aon 2^>nul ^| findstr ":9103 "') do taskkill /F /PID %%p >nul 2>&1
taskkill /F /IM bench_wc_live.exe /T >nul 2>&1
timeout /t 2 /nobreak >nul

:: Start agent
set "AGENT=%ROOT%\Tank\bench_wc_live_agent.py"
start "WC-Overhead-Agent" cmd /k "cd /d "%ROOT%" && python "%AGENT%" --players=%PLAYERS% --matches=%MATCHES%"

echo [INFO] Cho agent khoi dong...
timeout /t 10 /nobreak >nul

echo [INFO] Mo Grafana...
start "" "http://localhost:3000/d/wc-live-bench/worst-case-live-benchmark"

echo.
echo  Doi ~20s warmup roi scroll xuong cuoi Grafana
echo  de thay panel "Overhead / Switch"
echo.

:end
echo  Nhan phim bat ky de dong...
pause >nul
endlocal
