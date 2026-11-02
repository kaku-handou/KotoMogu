@echo off
setlocal
rem This script is intended for the KotoMogu LeapBridge Orion 4.1 prototype.
rem Use a relative source/build path to avoid Windows trailing-backslash quoting issues.
cd /d "%~dp0"
if errorlevel 1 goto failed

set "LEAP_SDK_ROOT=%USERPROFILE%\Desktop\LeapDeveloperKit_4.1.0+52211_win\LeapSDK"
if not exist "%LEAP_SDK_ROOT%\include\LeapC.h" (
    echo ERROR: LeapSDK not found at: %LEAP_SDK_ROOT%
    echo Edit LEAP_SDK_ROOT above if your SDK is in another folder.
    goto failed
)

cmake -S . -B build -G "Visual Studio 17 2022" -A x64 "-DLEAP_SDK_ROOT=%LEAP_SDK_ROOT%" -DCMAKE_POLICY_VERSION_MINIMUM=3.5
if errorlevel 1 goto failed

cmake --build build --config Release --target LeapBridge
if errorlevel 1 goto failed

echo.
echo SUCCESS: build\Release\LeapBridge.exe
pause
exit /b 0

:failed
echo.
echo Build failed. Please screenshot the error output.
pause
exit /b 1
