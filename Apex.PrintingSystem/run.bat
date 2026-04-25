@echo off
chcp 65001 >nul
echo ========================================
echo   Apex Printing System - تشغيل التطبيق
echo ========================================
echo.

cd /d "%~dp0Apex.UI"

echo [1/2] Building project...
dotnet build Apex.UI.csproj --verbosity quiet
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ❌ Build failed! Check errors above.
    pause
    exit /b 1
)

echo [2/2] Starting application...
echo.
dotnet run --project Apex.UI.csproj

pause

