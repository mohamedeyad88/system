# Test Build and Run Script
# Tests if the app runs without resources

Write-Host "=== Testing Without Resources ===" -ForegroundColor Cyan

Write-Host "`n[1/3] Cleaning..." -ForegroundColor Yellow
Remove-Item -Path "Apex.UI\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "Apex.UI\*_wpftmp.csproj" -Force -ErrorAction SilentlyContinue

Write-Host "`n[2/3] Building..." -ForegroundColor Yellow
dotnet build Apex.UI\Apex.UI.csproj --configuration Release --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n[3/3] Running (watch for crash)..." -ForegroundColor Yellow
Write-Host "If app starts successfully, resources are NOT the problem." -ForegroundColor Cyan
Write-Host "If app still crashes, the problem is elsewhere.`n" -ForegroundColor Cyan

dotnet run --project Apex.UI\Apex.UI.csproj --configuration Release --no-build
