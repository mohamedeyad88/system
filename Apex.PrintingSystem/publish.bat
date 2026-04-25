@echo off
chcp 65001 >nul
echo ========================================
echo   Apex Printing System - نشر التطبيق
echo ========================================
echo.

cd /d "%~dp0"

set PUBLISH_DIR=publish
set CONFIGURATION=Release

echo [1/3] Cleaning previous publish...
if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%"
)

echo [2/3] Publishing application...
dotnet publish Apex.UI/Apex.UI.csproj ^
    --configuration %CONFIGURATION% ^
    --output "%PUBLISH_DIR%" ^
    --self-contained true ^
    --runtime win-x64 ^
    /p:PublishSingleFile=true ^
    /p:IncludeAllContentForSelfExtract=true ^
    /p:EnableCompressionInSingleFile=true

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ❌ Publish failed! Check errors above.
    pause
    exit /b 1
)

echo [3/3] Creating run script...
(
    echo @echo off
    echo cd /d "%%~dp0"
    echo start "" "Apex.UI.exe"
) > "%PUBLISH_DIR%\run.bat"

echo.
echo ✅ Publish completed successfully!
echo.
echo 📁 Published files location: %CD%\%PUBLISH_DIR%
echo 🚀 To run: Navigate to %PUBLISH_DIR% and double-click "Apex.UI.exe" or "run.bat"
echo.
pause

