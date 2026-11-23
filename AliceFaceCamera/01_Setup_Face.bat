@echo off
cd /d "%~dp0"
py -3.11 -m venv .venv
if errorlevel 1 (
    echo Install Python 3.11 x64 including the Python Launcher and run this file again.
    pause
    exit /b 1
)
".venv\Scripts\python.exe" -m pip install -r requirements.txt
if errorlevel 1 (
    echo Installation failed. Check the messages above.
    pause
    exit /b 1
)
echo Ready. Run 02_Start_Face.bat.
pause
