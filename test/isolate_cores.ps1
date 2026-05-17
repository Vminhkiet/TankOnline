# isolate_cores.ps1
# Chạy với quyền Admin trên Windows trước khi benchmark
# powershell -ExecutionPolicy Bypass -File isolate_cores.ps1

param(
    [int]$ServerCores = 4,   # số cores dành riêng cho server (0 = dùng tất cả)
    [switch]$Restore          # -Restore để undo sau khi xong
)

$ErrorActionPreference = "SilentlyContinue"

# ─── Affinity masks ───────────────────────────────────────────────────────────
# i5-12500H: 8 logical cores (0-7)
# Server chiếm cores 4-7 (0xF0), OS/apps chiếm cores 0-3 (0x0F)
$SERVER_MASK = switch ($ServerCores) {
    2 { 0xC0 }   # cores 6-7
    4 { 0xF0 }   # cores 4-7
    8 { 0xFF }   # all cores (no isolation)
    default { 0xF0 }
}
$OTHER_MASK = 0xFF -bxor $SERVER_MASK  # inverse

if ($Restore) {
    Write-Host "[RESTORE] Resetting all processes to full affinity (0xFF)..."
    Get-Process | ForEach-Object { try { $_.ProcessorAffinity = 0xFF } catch {} }
    # Restore power plan to Balanced
    powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e | Out-Null
    # Re-enable Defender real-time
    Set-MpPreference -DisableRealtimeMonitoring $false | Out-Null
    Write-Host "[RESTORE] Done."
    return
}

Write-Host "============================================================"
Write-Host " Core Isolation Setup for server_tank benchmark"
Write-Host " Server mask : 0x$($SERVER_MASK.ToString('X2')) (cores $(($SERVER_MASK | ForEach-Object { $b = $_; (0..7 | Where-Object { $b -band (1 -shl $_) }) }) -join ','))"
Write-Host " Other  mask : 0x$($OTHER_MASK.ToString('X2'))"
Write-Host "============================================================"

# ─── Step 1: High Performance power plan (no CPU throttling) ─────────────────
Write-Host "[1/6] Setting High Performance power plan..."
powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c | Out-Null

# ─── Step 2: Disable Windows Defender real-time (prevent scan spikes) ────────
Write-Host "[2/6] Disabling Defender real-time monitoring..."
Set-MpPreference -DisableRealtimeMonitoring $true | Out-Null

# ─── Step 3: Push background processes to OTHER_MASK cores ───────────────────
Write-Host "[3/6] Pinning background processes to cores 0x$($OTHER_MASK.ToString('X2'))..."
$skip = @("Idle", "System", "Registry", "smss", "csrss", "wininit", "services",
          "lsass", "svchost", "server_tank")
$moved = 0
Get-Process | Where-Object { $_.Name -notin $skip } | ForEach-Object {
    try {
        $_.ProcessorAffinity = $OTHER_MASK
        $moved++
    } catch {}
}
Write-Host "   Moved $moved processes to cores 0x$($OTHER_MASK.ToString('X2'))"

# ─── Step 4: Find server_tank and apply isolation ────────────────────────────
Write-Host "[4/6] Pinning server_tank to cores 0x$($SERVER_MASK.ToString('X2'))..."
$srv = Get-Process -Name server_tank
if ($srv) {
    $srv.ProcessorAffinity = $SERVER_MASK
    $srv.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::RealTime
    Write-Host "   server_tank PID=$($srv.Id) → affinity=0x$($SERVER_MASK.ToString('X2')), priority=RealTime"
} else {
    Write-Host "   server_tank not running yet — start it, then re-run this script"
}

# ─── Step 5: Set current PowerShell to HIGH priority (for test script) ───────
Write-Host "[5/6] Setting this PowerShell session to High priority..."
(Get-Process -Id $PID).PriorityClass = [System.Diagnostics.ProcessPriorityClass]::High

# ─── Step 6: Disable Windows Update service temporarily ──────────────────────
Write-Host "[6/6] Stopping Windows Update service..."
Stop-Service -Name wuauserv -Force | Out-Null

Write-Host ""
Write-Host "============================================================"
Write-Host " Isolation ACTIVE. Run benchmark now."
Write-Host " After benchmark: powershell -File isolate_cores.ps1 -Restore"
Write-Host "============================================================"
