@echo off
title ApexWheel - ViGEmBus Driver Installer
echo =================================================================
echo    🏎️ Installing ViGEmBus Virtual Controller Driver
echo    (Enables Xbox 360 controller emulation for F1 25 and PC games)
echo =================================================================
echo.
echo Launching official signed driver installer...
start "" "%~dp0drivers\ViGEmBus.exe"
echo.
echo Please complete the on-screen installer if prompted.
echo Once finished, you can run start_server.bat!
pause
