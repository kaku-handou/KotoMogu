@echo off
setlocal
cd /d "%~dp0"
if errorlevel 1 goto failed
rem Edit only this path if your SDK moved.
set "LEAP_SDK_ROOT=C:\unity\LeapDeveloperKit_4.1.0+52211_win\LeapSDK"
if not exist "%LEAP_SDK_ROOT%\include\LeapC.h" (
  echo ERROR: LeapC.h not found. Edit LEAP_SDK_ROOT in this BAT.
  goto failed
)
where cmake >nul 2>nul
if errorlevel 1 (
  echo ERROR: Run from x64 Native Tools Command Prompt for VS 2026.
  goto failed
)
rem Separate build directory preserves the previous build and avoids cached generators.
cmake -S . -B build_gestures_vs2026 -G "Visual Studio 18 2026" -A x64 "-DLEAP_SDK_ROOT=%LEAP_SDK_ROOT%" -DCMAKE_POLICY_VERSION_MINIMUM=3.5
if errorlevel 1 goto failed
cmake --build build_gestures_vs2026 --config Release --target LeapBridge
if errorlevel 1 goto failed
echo SUCCESS: build_gestures_vs2026\Release\LeapBridge.exe
pause
exit /b 0
:failed
echo Build failed. Copy the error output for troubleshooting.
pause
exit /b 1
