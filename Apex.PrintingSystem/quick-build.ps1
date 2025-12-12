# Quick Build Fix Script
# This script fixes the duplicate App class issue and builds the project

Write-Host "=== Quick Build Fix ===" -ForegroundColor Cyan

# Clean and rebuild
Write-Host "`n[1/3] Cleaning build cache..." -ForegroundColor Yellow
Remove-Item -Path "Apex.UI\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "Apex.UI\bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "Apex.UI\*_wpftmp.csproj" -Force -ErrorAction SilentlyContinue
Write-Host "Done" -ForegroundColor Green

Write-Host "`n[2/3] Restoring packages..." -ForegroundColor Yellow
dotnet restore Apex.UI\Apex.UI.csproj --no-cache
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "`n[3/3] Building project..." -ForegroundColor Yellow
dotnet build Apex.UI\Apex.UI.csproj --configuration Release --no-incremental
if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host "`nRun the application:" -ForegroundColor Cyan
    Write-Host "dotnet run --project Apex.UI\Apex.UI.csproj --configuration Release" -ForegroundColor White
}
else {
    Write-Host "`n❌ BUILD FAILED!" -ForegroundColor Red
    exit 1
}
