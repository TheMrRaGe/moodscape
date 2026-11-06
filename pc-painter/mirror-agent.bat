@echo off
rem Moodscape Mirror for Windows: starts the Mirror painter; close this window to stop it.
rem The desktop icon from setup-mirror.bat runs this. It also lets Claude (through this folder, via the Claude
rem desktop app) start, stop or restart the painter and send you its QR code. See mirror_agent.py.
rem   mirror-agent.bat open       also open the live dashboard (it shows the QR code) once the painter is up
rem   mirror-agent.bat autostart  also start the Mirror whenever you log in (setup-mirror.bat does this too)
cd /d "%~dp0"
title Moodscape Mirror
set OPEN=
if /i "%~1"=="open" set OPEN=--open
if /i "%~1"=="autostart" (
  powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Startup')+'\Moodscape Mirror.lnk'); $s.TargetPath='%~f0'; $s.WorkingDirectory='%~dp0'; $s.WindowStyle=7; $s.Save()"
  echo The Mirror will now start, minimized, whenever you log in. To undo: setup-mirror.bat remove
)
call "%~dp0env-windows.bat" || (pause & exit /b 1)
python mirror_agent.py %OPEN%
if errorlevel 1 pause
