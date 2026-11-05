@echo off
rem Moodscape Mirror for Windows. Double-click to start; close this window to stop.
rem Same painter as start-windows.bat, but the QR code opens the Mirror.
call "%~dp0start-windows.bat" --qr mirror %*
