# Final Build Script
# Builds Apex.UI after all fixes have been applied

Write-Host "=== Building Apex.UI (Final) ===" -ForegroundColor Cyan

Write-Host "`n[1/2] Cleaning..." -ForegroundColor Yellow
Remove-Item -Path "Apex.UI\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "Apex.UI\*_wpftmp.csproj" -Force -ErrorAction SilentlyContinue

Write-Host "`n[2/2] Building..." -ForegroundColor Yellow
dotnet build Apex.UI\Apex.UI.csproj --configuration Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host "`n📋 Summary of Applied Fixes:" -ForegroundColor Cyan
    Write-Host "  ✓ Registered LogViewerViewModel in DI" -ForegroundColor White
    Write-Host "  ✓ Added comprehensive exception handling" -ForegroundColor White
    Write-Host "  ✓ Added database directory creation" -ForegroundColor White
    Write-Host "  ✓ Wrapped database init in defensive try-catch" -ForegroundColor White
    Write-Host "  ✓ Fixed App_MINIMAL.xaml conflict" -ForegroundColor White
    Write-Host "  ✓ Added System.IO namespace" -ForegroundColor White
    Write-Host "`n🚀 Ready to Run:" -ForegroundColor Cyan
    Write-Host "dotnet run --project Apex.UI\Apex.UI.csproj --configuration Release" -ForegroundColor White
    Write-Host "`n📝 Check Desktop for startup logs:" -ForegroundColor Cyan
    Write-Host "  - ApexStartup_YYYYMMDD_HHMMSS_SUCCESS.txt (on success)" -ForegroundColor White
    Write-Host "  - ApexStartup_ERROR.log (on failure)" -ForegroundColor White
}
else {
    Write-Host "`n❌ BUILD FAILED!" -ForegroundColor Red
    exit 1
}
