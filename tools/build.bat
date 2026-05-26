@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" > nul 2>&1
cl /EHsc /W3 /std:c++17 /nologo tank_hp_hack.cpp /Fe:tank_hp_hack.exe /link user32.lib gdi32.lib
if errorlevel 1 ( echo BUILD FAILED & pause ) else ( echo BUILD OK - tank_hp_hack.exe san sang )
