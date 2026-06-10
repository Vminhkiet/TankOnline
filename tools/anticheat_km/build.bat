@echo off
setlocal
cd /d C:\

:: ── Tim WDK + kiem tra km libs ───────────────────────────────────────────────
set WDK_BASE=C:\Program Files (x86)\Windows Kits\10
set WDK_VER=
for /f "delims=" %%v in ('dir /b /ad "%WDK_BASE%\Lib" 2^>nul ^| sort /r') do (
    if not defined WDK_VER (
        if exist "%WDK_BASE%\Lib\%%v\km\x64\ntoskrnl.lib" set WDK_VER=%%v
    )
)

if not defined WDK_VER (
    echo.
    echo [ERROR] Khong tim thay WDK kernel-mode libraries.
    echo.
    echo  WDK hien tai chi co SDK ^(user-mode^). Can cai full WDK:
    echo.
    echo  1. Vao: https://learn.microsoft.com/windows-hardware/drivers/download-the-wdk
    echo  2. Chon Windows 11 WDK hoac Windows 10 WDK
    echo  3. Cai WDK KHOP voi SDK version da co:
    for /f "delims=" %%v in ('dir /b /ad "%WDK_BASE%\Lib" 2^>nul ^| sort /r') do (
        echo     SDK version tim thay: %%v
    )
    echo  4. Khi cai, tick "Install Windows Driver Kit"
    echo  5. Sau khi cai xong chay lai build.bat nay
    echo.
    echo  Hoac dung NuGet trong Visual Studio:
    echo    Tools ^> NuGet Package Manager ^> Install "Microsoft.Windows.WDK.x64"
    echo.
    pause & exit /b 1
)
echo [*] WDK version: %WDK_VER%

:: ── Setup MSVC ────────────────────────────────────────────────────────────────
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" > nul 2>&1

set KM_INC="%WDK_BASE%\Include\%WDK_VER%\km"
set SHARED_INC="%WDK_BASE%\Include\%WDK_VER%\shared"
set KM_LIB="%WDK_BASE%\Lib\%WDK_VER%\km\x64"

cd /d "D:\Unity\TankOnline\SE315.Q21\tools\anticheat_km"

:: ── [1] Build driver ──────────────────────────────────────────────────────────
echo [1/3] Compiling anticheat_km.c...
cl /nologo /kernel /W3 /WX- /GS- /Gm- /Zp8 /EHsc ^
   /DNTDDI_VERSION=0x0A000000 /D_WIN32_WINNT=0x0A00 /D_AMD64_ /DAMD64 /D_WIN64 ^
   /I%KM_INC% /I%SHARED_INC% ^
   /c anticheat_km.c /Foanticheat_km.obj
if errorlevel 1 ( echo [-] Compile FAILED & pause & exit /b 1 )

echo [2/3] Linking anticheat_km.sys...
link /nologo /DRIVER /SUBSYSTEM:NATIVE /ENTRY:GsDriverEntry ^
     /NODEFAULTLIB /MANIFEST:NO ^
     anticheat_km.obj ^
     %KM_LIB%\ntoskrnl.lib ^
     %KM_LIB%\hal.lib ^
     %KM_LIB%\wdm.lib ^
     /OUT:anticheat_km.sys
if errorlevel 1 ( echo [-] Link FAILED & pause & exit /b 1 )
echo [+] anticheat_km.sys OK

:: ── [2] Sign voi test cert ────────────────────────────────────────────────────
set SIGNTOOL="%WDK_BASE%\bin\%WDK_VER%\x64\signtool.exe"
set MAKECERT="%WDK_BASE%\bin\%WDK_VER%\x64\makecert.exe"
echo [3/3] Signing with test certificate...
%MAKECERT% -n "CN=SE315TestCert" -r -pe -ss PrivateCertStore -sr LocalMachine ^
           -eku 1.3.6.1.5.5.7.3.3 SE315TestCert.cer 2>nul
certutil -addstore -f "TrustedPublisher" SE315TestCert.cer > nul 2>&1
certutil -addstore -f "Root"             SE315TestCert.cer > nul 2>&1
%SIGNTOOL% sign /v /s PrivateCertStore /n "SE315TestCert" ^
           /t http://timestamp.digicert.com anticheat_km.sys 2>nul
if errorlevel 1 (
    echo [!] Sign that bai - driver van co the chay neu test-signing bat
) else (
    echo [+] Signed OK
)

:: ── [3] Build loader ──────────────────────────────────────────────────────────
cd loader
cl /nologo /EHsc /W3 /std:c++17 loader.cpp /Fe:loader.exe /link advapi32.lib
if errorlevel 1 ( echo [-] Loader FAILED & pause & exit /b 1 )
echo [+] loader.exe OK
cd ..
del /f anticheat_km.obj 2>nul

echo.
echo === Build complete ===
echo   anticheat_km.sys   — kernel driver (load vao kernel)
echo   loader\loader.exe  — user-mode loader (can Admin)
echo.
echo Buoc tiep theo:
echo   1. Bat test-signing mode (neu chua):
echo      bcdedit /set testsigning on   ^(reboot sau^)
echo   2. Load driver:
echo      loader\loader.exe load
echo   3. Mo DebugView (Sysinternals) de xem log [AC-KM]
echo   4. Unload:
echo      loader\loader.exe unload
echo.
pause
