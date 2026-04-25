@echo off
echo ========================================
echo Building Apex.UI.exe for Testing
echo ========================================
echo.

cd /d "%~dp0"

echo Step 1: Cleaning...
if exist "Publish" rmdir /s /q "Publish"

echo.
echo Step 2: Publishing...
dotnet publish Apex.UI\Apex.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o Publish

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo ✅ Build Successful!
    echo ========================================
    echo.
    echo EXE Location: %CD%\Publish\Apex.UI.exe
    echo.
    if exist "Publish\Apex.UI.exe" (
        echo File exists! Ready for testing.
        echo.
        echo You can now:
        echo 1. Copy Apex.UI.exe to your test machine
        echo 2. Run it and test printing
        echo.
    ) else (
        echo ❌ EXE file not found!
    )
) else (
    echo.
    echo ❌ Build Failed!
    echo Check the error messages above.
)

echo.
pause

