@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0"
if errorlevel 1 exit /b 1

echo Generating OnlineRepo.json...
set "pythonCommand="
py -3 --version >nul 2>&1
if not errorlevel 1 set "pythonCommand=py -3"
if not defined pythonCommand (
    python --version >nul 2>&1
    if not errorlevel 1 set "pythonCommand=python"
)
if not defined pythonCommand (
    echo Python 3 was not found. Install Python or add it to PATH.
    popd
    if /i not "%~1"=="--no-pause" pause
    exit /b 1
)
if /i "%~1"=="--no-pause" (
    %pythonCommand% -X utf8 "%~dp0utils\parser.py" "%CD%"
) else (
    %pythonCommand% -X utf8 "%~dp0utils\parser.py" "%CD%" --interactive
)
set "result=%errorlevel%"
if not "%result%"=="0" (
    echo Update failed. The existing OnlineRepo.json was preserved.
    popd
    if /i not "%~1"=="--no-pause" pause
    exit /b %result%
)

echo Update complete.
popd
if /i not "%~1"=="--no-pause" pause
exit /b 0
