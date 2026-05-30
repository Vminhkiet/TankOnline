@echo off
:: Giu cua so khong tu dong dong khi double-click
if "%LAUNCHED%"=="" (
    set "LAUNCHED=1"
    cmd /k "%~f0" %*
    exit /b
)

setlocal EnableDelayedExpansion
cls

echo.
echo  ============================================================
echo   Worst-Case Live Benchmark  -  Tank Online
echo  ============================================================
echo.
echo  Cach dung:
echo    run_wc_benchmark.bat               (5 players, 1 match)
echo    run_wc_benchmark.bat 10            (10 players, 1 match)
echo    run_wc_benchmark.bat 5 3           (5 players, 3 matches - do overhead)
echo    run_wc_benchmark.bat rebuild       (rebuild exe + 5 players)
echo    run_wc_benchmark.bat rebuild 5 3   (rebuild + 5 players + 3 matches)
echo.

:: ── Parse arguments hoặc hỏi input ─────────────────────────────────────────
set "PLAYERS=5"
set "MATCHES=1"
set "DO_BUILD=0"

if not "%~1"=="" goto :parse_args

:: Không có tham số → hỏi người dùng nhập
echo  Nhap so player (Enter = mac dinh 5):
set /p "PLAYERS_INPUT=  Players: "
if not "!PLAYERS_INPUT!"=="" set "PLAYERS=!PLAYERS_INPUT!"

echo.
echo  Nhap so matches/tick de do overhead (Enter = 1 = khong do overhead):
echo  (Vi du: 3 = chay 3 match tuan tu de tinh switching cost)
set /p "MATCHES_INPUT=  Matches: "
if not "!MATCHES_INPUT!"=="" set "MATCHES=!MATCHES_INPUT!"

echo.
echo  Rebuild exe? (y = co, Enter = khong):
set /p "REBUILD_INPUT=  Rebuild: "
if /i "!REBUILD_INPUT!"=="y" set "DO_BUILD=1"

echo.
goto :done_parse

:parse_args
if "%~1"=="" goto :done_parse
if /i "%~1"=="rebuild" (
    set "DO_BUILD=1"
    shift
    goto :parse_args
)
if not defined _P_SET (
    set "PLAYERS=%~1"
    set "_P_SET=1"
) else if not defined _M_SET (
    set "MATCHES=%~1"
    set "_M_SET=1"
)
shift
goto :parse_args
:done_parse

:: ── Đường dẫn ──────────────────────────────────────────────────────────────
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

set "EXE_DIR=%ROOT%\Tank\out\build\x64-Release\bench_worst_case"
set "EXE=%EXE_DIR%\bench_wc_live.exe"
set "VCXPROJ_SRC=%EXE_DIR%\bench_worst_case.vcxproj"
set "VCXPROJ_DST=%EXE_DIR%\bench_wc_live.vcxproj"
set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
set "AGENT=%ROOT%\Tank\bench_wc_live_agent.py"
set "LOG=%EXE_DIR%\Release\bench_wc_live.log"
set "STAMP1=%EXE_DIR%\CMakeFiles\generate.stamp"
set "STAMP2=%EXE_DIR%\CMakeFiles\generate.stamp.depend"

echo  Players  : %PLAYERS%
echo  Matches  : %MATCHES%
echo  Rebuild  : %DO_BUILD%
echo.

:: ── Kiểm tra Python ──────────────────────────────────────────────────────────
echo [STEP 1] Kiem tra Python...
python --version
if errorlevel 1 (
    echo [LOI] Python khong tim thay!
    goto :end
)
echo  OK.
echo.

:: ── Build exe nếu cần ────────────────────────────────────────────────────────
if not exist "%EXE%" set "DO_BUILD=1"

if "%DO_BUILD%"=="0" goto :skip_build

echo [STEP 2] Build bench_wc_live.exe...

if exist "%VCXPROJ_SRC%" goto :vcxproj_ok
echo [LOI] Khong tim thay: %VCXPROJ_SRC%
goto :end
:vcxproj_ok

:: Gen vcxproj
set "PS_SRC=%VCXPROJ_SRC%"
set "PS_DST=%VCXPROJ_DST%"
powershell -NoProfile -Command "(Get-Content $env:PS_SRC) -replace 'bench_worst_case_spread','bench_wc_live' -replace '<ProjectName>bench_worst_case','<ProjectName>bench_wc_live' -replace 'bench_worst_case\.dir','bench_wc_live.dir' | Set-Content $env:PS_DST"

:: Touch stamps
powershell -NoProfile -Command "$now=Get-Date; @($env:STAMP1,$env:STAMP2) | ForEach-Object { if(Test-Path $_){ (Get-Item $_).LastWriteTime=$now } else { New-Item -ItemType File $_ -Force | Out-Null } }"

:: Kill exe cu truoc khi build (tranh lock file)
taskkill /F /IM bench_wc_live.exe /T >nul 2>&1
timeout /t 1 /nobreak >nul

:: Build
set "PS_VCXPROJ=%VCXPROJ_DST%"
set "PS_MSBUILD=%MSBUILD%"
powershell -NoProfile -Command "& $env:PS_MSBUILD $env:PS_VCXPROJ /p:Configuration=Release /p:Platform=x64 /p:BuildProjectReferences=false /m /v:m"
if errorlevel 1 (
    echo [LOI] Build that bai!
    goto :end
)

:: Copy exe (exe da bi kill nen khong con bi lock)
set "BUILT_EXE=%EXE_DIR%\Release\bench_worst_case.exe"
powershell -NoProfile -Command "Copy-Item $env:BUILT_EXE $env:EXE -Force"
echo [OK] Build + copy xong.
echo.
goto :step3

:skip_build
echo [STEP 2] Bo qua build (dung exe co san).
echo.

:: ── Kiem tra exe ─────────────────────────────────────────────────────────────
:step3
if exist "%EXE%" goto :exe_ok
echo [LOI] Khong tim thay exe. Chay lai voi: rebuild %PLAYERS% %MATCHES%
goto :end
:exe_ok

:: ── Start Docker containers ──────────────────────────────────────────────────
echo [STEP 3] Start Docker (Prometheus + Grafana)...
docker start prometheus >nul 2>&1 && echo  Prometheus OK || echo  Prometheus: skip
docker start grafana    >nul 2>&1 && echo  Grafana OK    || echo  Grafana: skip
timeout /t 2 /nobreak >nul
echo.

:: ── Kill process cu ──────────────────────────────────────────────────────────
echo [STEP 4] Don dep process cu...
for /f "tokens=5" %%p in ('netstat -aon 2^>nul ^| findstr ":9103 "') do taskkill /F /PID %%p >nul 2>&1
taskkill /F /IM bench_wc_live.exe /T >nul 2>&1
timeout /t 1 /nobreak >nul
echo  Done.
echo.

:: ── Ghi config ───────────────────────────────────────────────────────────────
echo [STEP 5] Ghi config: players=%PLAYERS% matches=%MATCHES%
echo {"players": %PLAYERS%, "matches": %MATCHES%} > "%ROOT%\bench_wc_config.json"
echo  Done.
echo.

:: ── Start agent ──────────────────────────────────────────────────────────────
echo [STEP 6] Khoi dong agent...
start "WC-Agent [port 9103]" cmd /k "cd /d "%ROOT%" && python "%AGENT%" --players=%PLAYERS% --matches=%MATCHES%"
echo  Done.
echo.

:: ── Cho agent san sang ───────────────────────────────────────────────────────
echo [STEP 7] Cho agent + warmup (~15 giay)...
timeout /t 15 /nobreak
echo  Done.
echo.

:: ── Mo Grafana ───────────────────────────────────────────────────────────────
echo [STEP 8] Mo Grafana dashboard...
start "" "http://localhost:3000/d/wc-live-bench/worst-case-live-benchmark"
echo.

:: ── Thong bao ────────────────────────────────────────────────────────────────
echo  ============================================================
if %MATCHES% GTR 1 (
    echo   OVERHEAD MODE: %MATCHES% matches/tick
    echo   Scroll xuong cuoi Grafana de thay panel Overhead/Switch
) else (
    echo   NORMAL MODE: 1 match/tick
)
echo.
echo   Players : %PLAYERS%
echo   Matches : %MATCHES%
echo   Grafana : http://localhost:3000/d/wc-live-bench/...
echo   Control : http://localhost:9103/
echo  ============================================================
echo.

:end
echo  Nhan phim bat ky de dong...
pause >nul
endlocal
