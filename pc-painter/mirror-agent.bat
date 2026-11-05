@echo off
rem Moodscape Mirror agent for Windows. Double-click to start the Mirror painter; close this window to stop it.
rem It also lets Claude (through this folder, via the Claude desktop app) start, stop or restart the painter
rem and send you its QR code. See mirror_agent.py.
rem   mirror-agent.bat autostart    also start the agent automatically when you log in to Windows
cd /d "%~dp0"
if /i "%~1"=="autostart" (
  powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Startup')+'\Moodscape Mirror agent.lnk'); $s.TargetPath='%~f0'; $s.WorkingDirectory='%~dp0'; $s.WindowStyle=7; $s.Save()"
  echo The Mirror agent will now start, minimized, whenever you log in. To undo, delete "Moodscape Mirror agent" from shell:startup.
)
where py >nul 2>nul && (set PY=py -3) || (set PY=python)
if not exist .venv (
  echo Setting up Python environment, first run only...
  %PY% -m venv .venv || (echo Python 3.10 or newer is needed: https://www.python.org/downloads/ & pause & exit /b 1)
)
call .venv\Scripts\activate.bat
echo Installing painter libraries, first run takes a minute...
python -m pip install -q --disable-pip-version-check numpy pillow onnxruntime-directml aiohttp qrcode
python mirror_agent.py
pause
