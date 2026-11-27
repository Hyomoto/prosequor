@echo off
setlocal
cd /d "%~dp0"

echo Log: "%~dp0tests.log"
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0test.ps1"
set EXIT=%ERRORLEVEL%

exit /b %EXIT%
