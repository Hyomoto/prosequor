@echo off
setlocal
cd /d "%~dp0"

echo Running Prosequor test suite...
echo Log: "%~dp0tests.log"

dotnet test "prosequor.slnx" --nologo -v minimal > "tests.log" 2>&1
set EXIT=%ERRORLEVEL%

echo.
type "tests.log"
echo.
if %EXIT% neq 0 (
  echo Tests FAILED. See tests.log
) else (
  echo Tests PASSED. See tests.log
)

exit /b %EXIT%
