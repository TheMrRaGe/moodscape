@echo off
rem Night Pages Unity agent: lets Claude build and play-test the game in the background.
rem Unity and the game run with no window and at low priority. Leave this window open (minimize it); close it to stop.
title Night Pages Unity agent
cd /d "%~dp0"
set PY=
rem Prefer the Python the Moodscape painter already uses on this PC, then the py launcher, then usual install folders.
if exist "%~dp0..\pc-painter\.venv\Scripts\python.exe" set PY="%~dp0..\pc-painter\.venv\Scripts\python.exe"
if not defined PY (where py >nul 2>nul && set "PY=py -3")
if not defined PY if exist "%LOCALAPPDATA%\Programs\Python\Python311\python.exe" set PY="%LOCALAPPDATA%\Programs\Python\Python311\python.exe"
if not defined PY if exist "%LOCALAPPDATA%\Programs\Python\Python310\python.exe" set PY="%LOCALAPPDATA%\Programs\Python\Python310\python.exe"
if not defined PY if exist "%USERPROFILE%\anaconda3\python.exe" set PY="%USERPROFILE%\anaconda3\python.exe"
if not defined PY (
  echo Couldn't find Python on this PC. Install Python 3 from https://www.python.org/downloads/ and try again.
  pause
  exit /b 1
)
echo Night Pages agent starting with %PY%
echo Leave this window open ^(you can minimize it^). Close it to stop the agent.
echo.
%PY% "%~dp0tools\unity-agent\np_unity_agent.py"
echo.
echo The agent stopped. If there is an error above, send Claude a screenshot of this window.
pause
