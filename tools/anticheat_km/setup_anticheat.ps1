param([string]$LogFile = "")

$ErrorActionPreference = "Stop"

# Admin elevation — save log to D: since C: is full
$id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$pr = New-Object System.Security.Principal.WindowsPrincipal($id)
$isAdmin = $pr.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    $LOG  = "$env:USERPROFILE\ac_setup.log"
    $ps1  = $MyInvocation.MyCommand.Path
    $args2 = "-NoProfile -ExecutionPolicy Bypass -File `"$ps1`" -LogFile `"$LOG`""
    Start-Process powershell.exe -ArgumentList $args2 -Verb RunAs -Wait
    if (Test-Path $LOG) { Get-Content $LOG; Remove-Item $LOG -Force }
    exit 0
}

if ($LogFile -ne "") { Start-Transcript -Path $LogFile -Force | Out-Null }

$ROOT    = "D:\Unity\TankOnline\SE315.Q21\tools\anticheat_km"
$WDK_DIR = "D:\wdk"
$VS      = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
$WKBASE  = "C:\Program Files (x86)\Windows Kits\10"

Write-Host "[*] SE315.Q21 AntiCheat KM Setup" -ForegroundColor Cyan
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
New-Item -ItemType Directory -Force -Path $WDK_DIR    | Out-Null
New-Item -ItemType Directory -Force -Path "D:\wdk_temp" | Out-Null

# ── 1. Find or install WDK km libs ───────────────────────────────────────────
Write-Host "[*] Looking for WDK km libs..." -ForegroundColor Cyan

$KM_INC = ""; $SHARED_INC = ""; $KM_LIB_DIR = ""; $kmVer = ""

# Detect installed SDK versions from Lib dir (they have ucrt/um but no km yet)
$sdkVersions = @()
if (Test-Path "$WKBASE\Lib") {
    $sdkVersions = (Get-ChildItem "$WKBASE\Lib" -Directory -ErrorAction SilentlyContinue |
                    Sort-Object Name -Descending).Name
}
if (-not $sdkVersions) { $sdkVersions = @("10.0.26100.0","10.0.22621.0","10.0.19041.0") }

# Search bases: traditional install + admin-extracted locations
$searchBases = @(
    $WKBASE,
    "$WDK_DIR\winkits10",
    "$WDK_DIR\extracted2\Windows Kits\10",
    "$WDK_DIR\extracted\Windows Kits\10"
)

foreach ($v in $sdkVersions) {
    foreach ($base in $searchBases) {
        if (Test-Path "$base\Lib\$v\km\x64\ntoskrnl.lib") {
            $kmVer      = $v
            $KM_LIB_DIR = "$base\Lib\$v\km\x64"
            $KM_INC     = "$base\Include\$v\km"
            $SHARED_INC = "$base\Include\$v\shared"
            # Fallback shared to SDK on C: if not in extracted dir
            if (-not (Test-Path "$SHARED_INC\ntdef.h")) {
                foreach ($sv in $sdkVersions) {
                    if (Test-Path "$WKBASE\Include\$sv\shared\ntdef.h") {
                        $SHARED_INC = "$WKBASE\Include\$sv\shared"; break
                    }
                }
            }
            Write-Host "[+] Found WDK $v at $base" -ForegroundColor Green
            break
        }
    }
    if ($kmVer) { break }
}

# If not found: create junctions so WDK installer writes km files to D:\wdk
if (-not $kmVer) {
    Write-Host "[*] km libs not found. Creating D: junctions for WDK install..." -ForegroundColor Cyan

    # Pick the best SDK version to junction (prefer highest)
    $jVer = $sdkVersions[0]
    Write-Host "[*] Targeting version $jVer" -ForegroundColor Cyan

    $KM_INC_D   = "$WDK_DIR\km_include_$jVer"
    $KM_LIB_D   = "$WDK_DIR\km_lib_$jVer"
    $KM_INC_C   = "$WKBASE\Include\$jVer\km"
    $KM_LIB_C   = "$WKBASE\Lib\$jVer\km"

    New-Item -ItemType Directory -Force -Path $KM_INC_D | Out-Null
    New-Item -ItemType Directory -Force -Path $KM_LIB_D | Out-Null

    # Ensure parent dirs exist on C: so mklink /J can create the junction
    New-Item -ItemType Directory -Force -Path (Split-Path $KM_INC_C) -ErrorAction SilentlyContinue | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path $KM_LIB_C) -ErrorAction SilentlyContinue | Out-Null

    if (-not (Test-Path $KM_INC_C)) {
        $r = cmd /c "mklink /J `"$KM_INC_C`" `"$KM_INC_D`"" 2>&1
        Write-Host "[+] Junction: $KM_INC_C -> $KM_INC_D" -ForegroundColor Green
    } else {
        Write-Host "[!] $KM_INC_C already exists, skipping junction" -ForegroundColor Yellow
    }
    if (-not (Test-Path $KM_LIB_C)) {
        $r = cmd /c "mklink /J `"$KM_LIB_C`" `"$KM_LIB_D`"" 2>&1
        Write-Host "[+] Junction: $KM_LIB_C -> $KM_LIB_D" -ForegroundColor Green
    } else {
        Write-Host "[!] $KM_LIB_C already exists, skipping junction" -ForegroundColor Yellow
    }

    # Download WDK installer to D: (C: is full, can't use C:\Temp)
    $installer = "$WDK_DIR\wdksetup.exe"
    if (-not (Test-Path $installer)) {
        # Determine which WDK version to download based on SDK version
        $dlUrl = "https://go.microsoft.com/fwlink/?linkid=2272234"  # WDK 26100
        if ($jVer -like "10.0.22621*") { $dlUrl = "https://go.microsoft.com/fwlink/?linkid=2249371" }
        if ($jVer -like "10.0.19041*") { $dlUrl = "https://go.microsoft.com/fwlink/?linkid=2196230" }
        Write-Host "[*] Downloading WDK installer to D: ($dlUrl)..." -ForegroundColor Cyan
        (New-Object System.Net.WebClient).DownloadFile($dlUrl, $installer)
        Write-Host "[+] Download OK: $((Get-Item $installer).Length / 1MB -as [int]) MB" -ForegroundColor Green
    } else {
        Write-Host "[+] Installer already at $installer" -ForegroundColor Green
    }

    # Redirect TEMP to D: so installer can run even with C: full
    $origTemp = $env:TEMP; $origTmp = $env:TMP
    $env:TEMP = "D:\wdk_temp"; $env:TMP = "D:\wdk_temp"

    Write-Host "[*] Running WDK installer (km files will land in D:\wdk via junctions)..." -ForegroundColor Cyan
    $p = Start-Process $installer -ArgumentList "/quiet /norestart" -Wait -PassThru

    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        Write-Host "[!] Silent install failed (exit=$($p.ExitCode)). Opening installer UI..." -ForegroundColor Yellow
        Write-Host "    Click Next -> Install -> Finish. Files go to D: via junctions." -ForegroundColor Yellow
        $p = Start-Process $installer -Wait -PassThru
    }

    $env:TEMP = $origTemp; $env:TMP = $origTmp

    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        Write-Host "[-] WDK install failed (exit=$($p.ExitCode))" -ForegroundColor Red
        if ($LogFile -ne "") { Stop-Transcript | Out-Null }; exit 1
    }

    # Check if km libs landed in D: via junctions
    if (Test-Path "$KM_LIB_D\x64\ntoskrnl.lib") {
        $kmVer      = $jVer
        $KM_LIB_DIR = "$KM_LIB_D\x64"
        $KM_INC     = $KM_INC_D
        $SHARED_INC = "$WKBASE\Include\$jVer\shared"
        Write-Host "[+] km libs installed to D: $kmVer" -ForegroundColor Green
    } else {
        Write-Host "[-] km libs not found after install ($KM_LIB_D\x64\ntoskrnl.lib)" -ForegroundColor Red
        Write-Host "    Junctions were created but installer may not have run." -ForegroundColor Yellow
        if ($LogFile -ne "") { Stop-Transcript | Out-Null }; exit 1
    }
}

# signtool / makecert (optional - signing skipped if absent)
$signtool = ""; $makecert = ""
$stPaths = @(
    "$WKBASE\bin\$kmVer\x64\signtool.exe",
    "$WDK_DIR\winkits10\bin\$kmVer\x64\signtool.exe"
)
foreach ($p in $stPaths) { if (Test-Path $p) { $signtool = $p; $makecert = ($p -replace "signtool","makecert"); break } }

Write-Host "[+] WDK km version : $kmVer" -ForegroundColor Green
Write-Host "    KM_INC     : $KM_INC" -ForegroundColor Gray
Write-Host "    SHARED_INC : $SHARED_INC" -ForegroundColor Gray
Write-Host "    KM_LIB_DIR : $KM_LIB_DIR" -ForegroundColor Gray

# ── 2. Test-signing ───────────────────────────────────────────────────────────
Write-Host "[*] Checking test-signing..." -ForegroundColor Cyan
$bcOut = (& bcdedit /enum '{current}' 2>&1) -join " "
$needReboot = $false
if ($bcOut -match "testsigning\s+Yes") {
    Write-Host "[+] Test-signing already ON" -ForegroundColor Green
} else {
    & bcdedit /set testsigning on | Out-Null
    Write-Host "[+] Test-signing enabled" -ForegroundColor Green
    $needReboot = $true
}

# ── 3. Build ──────────────────────────────────────────────────────────────────
$sysExists    = Test-Path "$ROOT\anticheat_km.sys"
$loaderExists = Test-Path "$ROOT\loader\loader.exe"

if ($sysExists -and $loaderExists) {
    Write-Host "[+] Build skipped — anticheat_km.sys + loader.exe already exist" -ForegroundColor Green
} else {
Write-Host "[*] Building driver and loader..." -ForegroundColor Cyan

$KM_INC_Q     = "`"$KM_INC`""
$SHARED_INC_Q = "`"$SHARED_INC`""

$bat  = "@echo off`r`n"
$bat += "cd /d C:\`r`n"
$bat += "call `"$VS`" > nul 2>&1`r`n"
$bat += "cd /d `"$ROOT`"`r`n"
$bat += "cl /nologo /kernel /W3 /WX- /GS- /Gm- /Zp8 /EHsc /DNTDDI_VERSION=0x0A000000 /D_WIN32_WINNT=0x0A00 /D_AMD64_ /DAMD64 /D_WIN64 /I$KM_INC_Q /I$SHARED_INC_Q /c anticheat_km.c /Foanticheat_km.obj`r`n"
$bat += "if errorlevel 1 exit /b 1`r`n"
$bat += "link /nologo /DRIVER /SUBSYSTEM:NATIVE /ENTRY:DriverEntry /NODEFAULTLIB /MANIFEST:NO anticheat_km.obj `"$KM_LIB_DIR\ntoskrnl.lib`" `"$KM_LIB_DIR\hal.lib`" `"$KM_LIB_DIR\wdm.lib`" /OUT:anticheat_km.sys`r`n"
$bat += "if errorlevel 1 exit /b 1`r`n"
$bat += "del /f anticheat_km.obj 2>nul`r`n"
$bat += "cd loader`r`n"
$bat += "cl /nologo /EHsc /W3 /std:c++17 loader.cpp /Fe:loader.exe /link advapi32.lib`r`n"
$bat += "if errorlevel 1 exit /b 1`r`n"

$batPath = "D:\wdk_temp\_ac_build.bat"
[System.IO.File]::WriteAllText($batPath, $bat, [System.Text.Encoding]::ASCII)

$outLog = "D:\wdk_temp\_ac_out.txt"
$errLog = "D:\wdk_temp\_ac_err.txt"
$p = Start-Process cmd.exe -ArgumentList "/c `"$batPath`"" -Wait -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
foreach ($f in @($outLog, $errLog)) {
    if (Test-Path $f) {
        Get-Content $f | Where-Object { $_ -ne "" -and $_ -notmatch "Copyright|Microsoft Corporation" } | Write-Host
        Remove-Item $f -Force
    }
}
Remove-Item $batPath -Force -ErrorAction SilentlyContinue
if ($p.ExitCode -ne 0) {
    Write-Host "[-] Build failed (exit=$($p.ExitCode))" -ForegroundColor Red
    if ($LogFile -ne "") { Stop-Transcript | Out-Null }; exit 1
}
Write-Host "[+] Build OK: anticheat_km.sys + loader.exe" -ForegroundColor Green
} # end build block

# ── 4. Sign ───────────────────────────────────────────────────────────────────
Write-Host "[*] Signing driver..." -ForegroundColor Cyan
if ((Test-Path $makecert) -and (Test-Path $signtool)) {
    # Remove old SE315TestCert certs to avoid "multiple certs" error
    Get-ChildItem Cert:\LocalMachine\PrivateCertStore -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like "*SE315TestCert*" } |
        ForEach-Object { Remove-Item "Cert:\LocalMachine\PrivateCertStore\$($_.Thumbprint)" -Force -ErrorAction SilentlyContinue }

    & $makecert -n "CN=SE315TestCert" -r -pe -ss PrivateCertStore -sr LocalMachine -eku 1.3.6.1.5.5.7.3.3 "$ROOT\SE315TestCert.cer" 2>$null
    & certutil -addstore -f "TrustedPublisher" "$ROOT\SE315TestCert.cer" 2>$null | Out-Null
    & certutil -addstore -f "Root" "$ROOT\SE315TestCert.cer" 2>$null | Out-Null

    # Get thumbprint of the freshly created cert
    $cert = Get-ChildItem Cert:\LocalMachine\PrivateCertStore -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -like "*SE315TestCert*" } |
            Sort-Object NotBefore -Descending | Select-Object -First 1
    if ($cert) {
        $ErrorActionPreference = "Continue"
        & $signtool sign /sha1 $cert.Thumbprint /fd sha256 /t "http://timestamp.digicert.com" "$ROOT\anticheat_km.sys" 2>$null | Out-Null
        $ErrorActionPreference = "Stop"
        if ($LASTEXITCODE -eq 0) { Write-Host "[+] Signed OK" -ForegroundColor Green }
        else { Write-Host "[!] Sign failed (exit=$LASTEXITCODE) - OK in test-signing mode" -ForegroundColor Yellow }
    } else {
        Write-Host "[!] Cert not found after makecert, skipping sign" -ForegroundColor Yellow
    }
} else {
    Write-Host "[!] signtool not in WDK, skipping sign (test-signing mode OK)" -ForegroundColor Yellow
}

# ── 5. Load or schedule after reboot ─────────────────────────────────────────
if ($needReboot) {
    Write-Host "[!] REBOOT REQUIRED (test-signing just enabled)" -ForegroundColor Yellow
    Write-Host "[*] Creating scheduled task to auto-load after reboot..." -ForegroundColor Cyan
    $loaderPath = "$ROOT\loader\loader.exe"
    $action   = New-ScheduledTaskAction -Execute $loaderPath -Argument "load"
    $trigger  = New-ScheduledTaskTrigger -AtLogOn
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
    Register-ScheduledTask -TaskName "SE315_LoadAntiCheat" -Action $action -Trigger $trigger -Settings $settings -RunLevel Highest -Force | Out-Null
    Write-Host "[+] Scheduled task registered (runs once after next login)" -ForegroundColor Green
    Write-Host ""
    $ans = Read-Host "Reboot now? (y/n)"
    if ($ans -eq "y") { Restart-Computer -Force }
} else {
    Write-Host "[*] Loading driver into kernel..." -ForegroundColor Cyan
    $p = Start-Process "$ROOT\loader\loader.exe" -ArgumentList "load" -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -eq 0) { Write-Host "[+] Driver loaded! Open DebugView, filter [AC-KM]" -ForegroundColor Green }
    else { Write-Host "[!] Load failed (exit=$($p.ExitCode))" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "[+] Done." -ForegroundColor Green
Write-Host "    Driver : $ROOT\anticheat_km.sys"
Write-Host "    Loader : $ROOT\loader\loader.exe"
Write-Host "    Log    : DebugView filter [AC-KM]"
Write-Host "    Unload : loader\loader.exe unload"

if ($LogFile -ne "") { Stop-Transcript | Out-Null }
