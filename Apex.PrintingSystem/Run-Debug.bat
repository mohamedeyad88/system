@echo off
echo Starting Apex Printing System (Debug Mode)...
echo.

REM Kill any running instances
taskkill /IM Apex.UI.exe /F 2>nul

REM Navigate to Debug folder
cd /d "D:\Apex\system\Apex.PrintingSystem\Apex.UI\bin\Debug\net8.0-windows"

REM Check if file exists
if not exist "Apex.UI.exe" (
    echo ERROR: Apex.UI.exe not found in Debug folder!
    echo Please build the project first: dotnet build -c Debug
    pause
    exit /b 1
)

REM Run the application
echo Running from: %CD%
echo.
start "" "Apex.UI.exe"

echo Application started!
timeout /t 2
