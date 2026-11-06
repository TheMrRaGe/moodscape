@echo off
rem Sets up (first time) and activates this folder's Python environment with the painter's libraries.
rem Used by setup-mirror.bat and mirror-agent.bat.
cd /d "%~dp0"
where py >nul 2>nul && (set PY=py -3) || (set PY=python)
if not exist .venv (
  echo Setting up Python environment, first run only...
  %PY% -m venv .venv || (echo Python 3.10 or newer is needed: https://www.python.org/downloads/ & exit /b 1)
)
call .venv\Scripts\activate.bat
if not exist .venv\ready-1 (
  echo Installing painter libraries, first run takes a minute...
  python -m pip install -q --disable-pip-version-check numpy pillow onnxruntime-directml aiohttp qrcode || exit /b 1
  echo ok> .venv\ready-1
)
exit /b 0
