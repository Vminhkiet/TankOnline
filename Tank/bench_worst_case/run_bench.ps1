# run_bench.ps1
# ─────────────────────────────────────────────────────────────────────────────
# Build + chạy benchmark worst-case GPC trên Windows.
# Chạy từ PowerShell với quyền Admin để SetProcessAffinityMask hoạt động đúng.
#
# Cách dùng:
#   cd "c:\Users\ADMIN\Desktop\New folder (5)\SE315.Q21\Tank\bench_worst_case"
#   .\run_bench.ps1
#
# Kết quả:
#   bench_worst_case.log  ← Python agent tails file này để push Prometheus

$ErrorActionPreference = "Stop"

# ── Paths ─────────────────────────────────────────────────────────────────────
$TANK_ROOT  = "$PSScriptRoot\.."
$BUILD_DIR  = "$TANK_ROOT\out\build\x64-Release"
$EXE        = "$BUILD_DIR\bench_worst_case\bench_worst_case.exe"
$MSBUILD    = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
$VCPKG      = "$TANK_ROOT\vcpkg_installed\x64-windows"

# ── Step 1: CMake configure (chỉ cần lần đầu) ─────────────────────────────────
if (-not (Test-Path "$BUILD_DIR\CMakeCache.txt")) {
    Write-Host "[run_bench] Configuring CMake ..." -ForegroundColor Cyan
    & cmake -S $TANK_ROOT -B $BUILD_DIR `
        -G "Visual Studio 17 2022" -A x64 `
        "-DCMAKE_TOOLCHAIN_FILE=$TANK_ROOT\vcpkg_installed\x64-windows\share\vcpkg\vcpkg.cmake" `
        "-DVCPKG_TARGET_TRIPLET=x64-windows" | Out-Null
}

# ── Step 2: Build bench_worst_case ────────────────────────────────────────────
Write-Host "[run_bench] Building bench_worst_case (Release x64) ..." -ForegroundColor Cyan
& cmake --build $BUILD_DIR --target bench_worst_case --config Release -j 4
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# ── Step 3: Chạy benchmark ────────────────────────────────────────────────────
$OUT_DIR = "$BUILD_DIR\bench_worst_case\Release"
if (-not (Test-Path $OUT_DIR)) {
    $OUT_DIR = "$BUILD_DIR\bench_worst_case"
}

if (-not (Test-Path $EXE)) {
    # Try Release sub-dir
    $EXE = "$OUT_DIR\bench_worst_case.exe"
}

if (-not (Test-Path $EXE)) {
    Write-Error "Không tìm thấy bench_worst_case.exe tại $EXE"
    exit 1
}

Write-Host ""
Write-Host "[run_bench] ================================================" -ForegroundColor Yellow
Write-Host "[run_bench] Chạy benchmark (WARMUP 10s + MEASURE 100s = ~110s)" -ForegroundColor Yellow
Write-Host "[run_bench] Log → $OUT_DIR\bench_worst_case.log" -ForegroundColor Yellow
Write-Host "[run_bench] ================================================" -ForegroundColor Yellow
Write-Host ""

Push-Location $OUT_DIR
try {
    & $EXE
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "[run_bench] Done." -ForegroundColor Green
Write-Host "[run_bench] Log file: $OUT_DIR\bench_worst_case.log" -ForegroundColor Green
Write-Host ""
Write-Host "Tiếp theo: chạy Python agent trên WSL2 để push metrics lên Prometheus/Grafana"
Write-Host "  python3 ~/project/SE315.Q21/Tank/bench_metrics_agent.py \"
Write-Host "    --log /mnt/d/Unity/TankOnline/Tank/out/build/x64-Release/bench_worst_case/bench_worst_case.log"
