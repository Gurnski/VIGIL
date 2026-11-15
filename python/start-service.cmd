@echo off
rem Starts the VIGIL analysis service and keeps it running. Double-click it, or run it from a
rem terminal with -Network so other devices can follow a presentation. See start-service.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-service.ps1" %*
if errorlevel 1 pause
