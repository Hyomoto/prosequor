@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set PYTHONIOENCODING=utf-8

if not defined VSPYTHONTK (
  echo VSPYTHONTK is not set.
  echo Point it at your VSpythonTK folder, then reopen the terminal:
  echo   setx VSPYTHONTK "D:\path\to\VSpythonTK"
  exit /b 1
)

if not exist "%VSPYTHONTK%\build.py" (
  echo build.py not found under VSPYTHONTK="%VSPYTHONTK%"
  exit /b 1
)

if defined VINTAGESTORYDATA (
  set "MODS=%VINTAGESTORYDATA%\Mods"
) else (
  set "MODS=%APPDATA%\VintagestoryData\Mods"
)

python "%VSPYTHONTK%\build.py" "%~dp0." --copy-to "%MODS%\prosequor.zip"
exit /b %ERRORLEVEL%
