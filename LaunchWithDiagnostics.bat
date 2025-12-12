@echo off
REM Apex Diagnostic Launcher
REM Runs Apex.UI.exe with diagnostic output enabled

echo === APEX DIAGNOSTIC LAUNCHER ===
echo.

set BIN_PATH=%~dp0Apex.UI\bin\Debug\net8.0-windows\Apex.UI.exe

if not exist "%BIN_PATH%" (
    echo ERROR: Apex.UI.exe not found at:
    echo %BIN_PATH%
    echo.
    echo Please build the project first:
    echo   dotnet build Apex.UI\Apex.UI.csproj
    echo.
    pause
    exit /b 1
)

echo Executable found: %BIN_PATH%
echo.
echo Launching with diagnostic mode...
echo ================================
echo.

REM Enable fusion logging for assembly loading diagnostics
set FUSION_LOG=1
set COMPlus_LogEnable=1
set COMPlus_LogToConsole=1

REM Run the application
"%BIN_PATH%"

set EXIT_CODE=%ERRORLEVEL%

echo.
echo ================================
echo Application exited with code: %EXIT_CODE%
echo.

if %EXIT_CODE% NEQ 0 (
    echo ERROR: Application failed to start or crashed
    echo Check your Desktop for error logs:
    echo   - ApexStartup_ERROR.log
    echo   - ApexStartup_YYYYMMDD_HHMMSS.log
) else (
    echo Application closed normally
)

echo.
echo Press any key to exit...
pause > nul
