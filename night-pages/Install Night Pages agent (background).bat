@echo off
rem Night Pages agent, background version: run this once. From then on the agent runs hidden whenever you're
rem logged in (no window, low priority, never takes focus), so Claude can build and test without you starting it.
title Install Night Pages agent (background)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\unity-agent\install_background.ps1"
if errorlevel 1 (
  echo.
  echo Something went wrong above. Send Claude a screenshot of this window.
  pause
  exit /b 1
)
echo.
echo Done. This window closes in a few seconds.
timeout /t 6 >nul
