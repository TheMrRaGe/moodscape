@echo off
rem One-time setup of the Moodscape Mirror on this PC. Double-click it once:
rem   - puts a "Moodscape Mirror" icon on your desktop (double-click it to start the Mirror and see the QR code)
rem   - starts the Mirror, minimized, whenever you log in
rem   - lets phones on your home Wi-Fi reach the painter directly (Windows asks for permission once), so the
rem     Android app finds this laptop by itself, with no QR code
rem   setup-mirror.bat noautostart    the same, without starting at login
rem   setup-mirror.bat remove         undo all of it (your people and captures are kept)
cd /d "%~dp0"
call "%~dp0env-windows.bat" || (pause & exit /b 1)
if /i "%~1"=="remove" (
  python setup_windows.py --remove
  pause & exit /b 0
)
if /i "%~1"=="noautostart" (python setup_windows.py --no-autostart) else (python setup_windows.py)
if errorlevel 1 (pause & exit /b 1)
echo.
echo Starting the Mirror now. The dashboard opens in your browser when it's ready; scan its QR code with the app.
start "Moodscape Mirror" /min "%~dp0mirror-agent.bat" open
timeout /t 8
