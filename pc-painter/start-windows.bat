@echo off
rem Moodscape PC painter for Windows. Double-click to start; close this window to stop.
cd /d "%~dp0"
where py >nul 2>nul && (set PY=py -3) || (set PY=python)
if not exist .venv (
  echo Setting up Python environment, first run only...
  %PY% -m venv .venv || (echo Python 3.10 or newer is needed: https://www.python.org/downloads/ & pause & exit /b 1)
)
call .venv\Scripts\activate.bat
echo Installing painter libraries, first run takes a minute...
python -m pip install -q --disable-pip-version-check numpy pillow onnxruntime-directml aiohttp qrcode
python painter_server.py %*
pause
