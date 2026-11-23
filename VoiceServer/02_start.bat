@echo off
chcp 65001 >nul
set "PYTHONUTF8=1"
cd /d "%~dp0"
if not exist .venv\Scripts\python.exe goto missing
.venv\Scripts\python.exe server.py --model gpt-transcribe --port 8765
pause
exit /b
:missing
echo Run 01_setup.bat first.
pause
