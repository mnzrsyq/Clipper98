@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Release-Clipper98.ps1" %*
exit /b %ERRORLEVEL%
