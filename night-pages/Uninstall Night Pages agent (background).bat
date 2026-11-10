@echo off
rem Removes the background Night Pages agent (the scheduled task). The project and its runs are left alone.
title Uninstall Night Pages agent (background)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\unity-agent\install_background.ps1" -Remove
pause
