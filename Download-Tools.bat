@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Download-Tools.ps1" %*
exit /b %ERRORLEVEL%
