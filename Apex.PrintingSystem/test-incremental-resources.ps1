# Test Incremental Resource Loading
# This tests each resource file one by one to find which causes the crash

Write-Host "=== Incremental Resource Loading Test ===" -ForegroundColor Cyan

# Restore original first
Write-Host "`nRestoring original App.xaml..." -ForegroundColor Yellow
if (Test-Path "Apex.UI\App.xaml.BACKUP") {
    Copy-Item "Apex.UI\App.xaml.BACKUP" "Apex.UI\App.xaml" -Force
    Write-Host "Done" -ForegroundColor Green
}

# Test 1: Colors only
Write-Host "`n[Test 1] Testing with Colors.xaml only..." -ForegroundColor Cyan
Copy-Item "Apex.UI\App_TEST_COLORS.xaml" "Apex.UI\App.xaml" -Force
Remove-Item -Recurse -Force Apex.UI\bin\Release, Apex.UI\obj -ErrorAction SilentlyContinue
dotnet build Apex.UI\Apex.UI.csproj -c Release -v quiet
if ($LASTEXITCODE -eq 0) {
    Write-Host "Build OK. Running..." -ForegroundColor Yellow
    Start-Process "Apex.UI\bin\Release\net8.0-windows\Apex.UI.exe"
    Start-Sleep -Seconds 3
    $process = Get-Process "Apex.UI" -ErrorAction SilentlyContinue
    if ($process) {
        Write-Host "SUCCESS: Colors.xaml works!" -ForegroundColor Green
        Stop-Process -Name "Apex.UI" -Force -ErrorAction SilentlyContinue
    }
    else {
        Write-Host "CRASH: Colors.xaml causes crash" -ForegroundColor Red
        exit
    }
}

Write-Host "`n============================" -ForegroundColor Cyan
Write-Host "Continue adding resources one by one manually" -ForegroundColor Yellow
Write-Host "If it works, add Animations.xaml next, then Controls.xaml, etc." -ForegroundColor Yellow
