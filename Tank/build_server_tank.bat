@echo off
set "SOURCE_DIR=%~dp0"
if "%SOURCE_DIR:~-1%"=="\" set "SOURCE_DIR=%SOURCE_DIR:~0,-1%"

echo ==================================================
echo       BUILD TANK SERVER (C++)
echo ==================================================
echo.
echo Select build option:
echo [1] Build Son (Visual Studio 2022)
echo [2] Build Lam (Visual Studio 18 Insiders)
echo ==================================================
set /p choice="Enter your choice (1 or 2): "

if "%choice%"=="2" goto BUILD_LAM
if "%choice%"=="1" goto BUILD_SON
echo Invalid choice, defaulting to Build Son...
echo.
goto BUILD_SON

:BUILD_SON
echo ==================================================
echo Running Build Son (VS 2022)...
echo ==================================================
set VCPKG_ROOT=C:\vcpkg
set CMAKE_EXE=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe
set "BUILD_DIR=%SOURCE_DIR%\out\build\x64-Release"

:: ---- 1. Kiem tra / cai vcpkg ----
if not exist "%VCPKG_ROOT%\vcpkg.exe" (
    echo [1/4] vcpkg chua co, dang clone ve C:\vcpkg ...
    git clone https://github.com/microsoft/vcpkg.git %VCPKG_ROOT%
    if errorlevel 1 ( echo Clone vcpkg THAT BAI & pause & exit /b 1 )
    echo [2/4] Bootstrapping vcpkg...
    call %VCPKG_ROOT%\bootstrap-vcpkg.bat -disableMetrics
    if errorlevel 1 ( echo Bootstrap THAT BAI & pause & exit /b 1 )
) else (
    echo [1/4] vcpkg da co tai %VCPKG_ROOT%
    echo [2/4] Bo qua bootstrap
)

:: ---- 3. CMake Configure ----
echo [3/4] Dang configure CMake...
"%CMAKE_EXE%" -S "%SOURCE_DIR%" -B "%BUILD_DIR%" ^
    -G "Visual Studio 17 2022" -A x64 ^
    -DCMAKE_TOOLCHAIN_FILE="%VCPKG_ROOT%\scripts\buildsystems\vcpkg.cmake" ^
    -DVCPKG_TARGET_TRIPLET=x64-windows

if errorlevel 1 ( echo CMake configure THAT BAI & pause & exit /b 1 )

:: ---- 4. CMake Build ----
echo [4/4] Dang build server_tank...
"%CMAKE_EXE%" --build "%BUILD_DIR%" --config Release --target server_tank

if errorlevel 1 ( echo Build THAT BAI & pause & exit /b 1 )

echo.
echo ==================================================
echo   BUILD SON THANH CONG!
echo   EXE: %BUILD_DIR%\Release\server_tank.exe
echo ==================================================
goto END

:BUILD_LAM
echo ==================================================
echo Running Build Lam (VS 18 Insiders)...
echo ==================================================
set "BUILD_DIR=%SOURCE_DIR%\out\build\x64-Release"
set "CMAKE_EXE=C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
set "TOOLCHAIN_FILE=C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\vcpkg\scripts\buildsystems\vcpkg.cmake"

echo [1/2] Dang configure CMake...
"%CMAKE_EXE%" -S "%SOURCE_DIR%" -B "%BUILD_DIR%" ^
    -G "Visual Studio 17 2022" -A x64 ^
    -DCMAKE_TOOLCHAIN_FILE="%TOOLCHAIN_FILE%" ^
    -DVCPKG_TARGET_TRIPLET=x64-windows

if errorlevel 1 ( echo CMake configure THAT BAI & pause & exit /b 1 )

echo [2/2] Dang build server_tank...
"%CMAKE_EXE%" --build "%BUILD_DIR%" --config Release --target server_tank

if errorlevel 1 ( echo Build THAT BAI & pause & exit /b 1 )

echo.
echo ==================================================
echo   BUILD LAM THANH CONG!
echo   EXE: %BUILD_DIR%\Release\server_tank.exe
echo ==================================================
goto END

:END
pause
