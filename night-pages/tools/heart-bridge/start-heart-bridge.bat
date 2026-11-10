@echo off
rem Heart-rate bridge for Night Pages. Double-click to read a Bluetooth heart-rate device.
rem For a made-up pulse instead:   start-heart-bridge.bat --simulate
rem For Pulsoid (Apple / Galaxy / Wear OS watch):   start-heart-bridge.bat --pulsoid YOUR_TOKEN
cd /d "%~dp0"
python -c "import bleak, websockets" 2>nul || python -m pip install bleak websockets
python heart_bridge.py %*
pause
