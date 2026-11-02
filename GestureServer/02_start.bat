@echo off
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" goto missing
.venv\Scripts\python.exe gesture_server.py --camera 0 --hold 0.3 --score 0.55
pause
exit /b
:missing
echo Run 01_setup.bat first.
pause
