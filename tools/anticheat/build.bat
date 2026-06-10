@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" > nul 2>&1
cd /d "D:\Unity\TankOnline\SE315.Q21\tools\anticheat"
cl /EHsc /W3 /std:c++17 /nologo anticheat.cpp /Fe:anticheat.exe /link ntdll.lib
if errorlevel 1 ( echo BUILD FAILED & pause ) else ( echo BUILD OK - anticheat.exe san sang )
