@echo off
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
    echo Run 01_Setup_Face.bat first.
    pause
    exit /b 1
)
set "ALICE_CAMERA_INDEX=%~1"
if not defined ALICE_CAMERA_INDEX set "ALICE_CAMERA_INDEX=0"
".venv\Scripts\python.exe" run_expression.py --camera "%ALICE_CAMERA_INDEX%"
if errorlevel 1 pause
