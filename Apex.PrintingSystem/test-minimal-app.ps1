# Test with Minimal Configuration
Write-Host "=== Testing with MINIMAL App.xaml ===" -ForegroundColor Cyan

# Backup original
Write-Host "`nBacking up App.xaml..." -ForegroundColor Yellow
Copy-Item "Apex.UI\App.xaml" "Apex.UI\App.xaml.BACKUP" -Force
Write-Host "Done" -ForegroundColor Green

# Use minimal version
Write-Host "`nUsing minimal App.xaml..." -ForegroundColor Yellow
Copy-Item "Apex.UI\App_MINIMAL_TEST.xaml" "Apex.UI\App.xaml" -Force
Write-Host "Done" -ForegroundColor Green

# Clean build
Write-Host "`nCleaning..." -ForegroundColor Yellow
Remove-Item -Recurse -Force Apex.UI\bin, Apex.UI\obj -ErrorAction SilentlyContinue

# Build
Write-Host "`nBuilding with minimal App.xaml..." -ForegroundColor Yellow
dotnet build Apex.UI\Apex.UI.csproj -c Release --no-incremental

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild succeeded" -ForegroundColor Green
    Write-Host "`nNow try running:" -ForegroundColor Cyan
    Write-Host ".\Apex.UI\bin\Release\net8.0-windows\Apex.UI.exe" -ForegroundColor Yellow
    Write-Host "`nIf it WORKS: The problem is in App.xaml resource dictionaries" -ForegroundColor White
    Write-Host "If it CRASHES: The problem is in App.xaml.cs code" -ForegroundColor White
}
else {
    Write-Host "`nBuild failed" -ForegroundColor Red
}

Write-Host "`nTo restore original App.xaml, run:" -ForegroundColor Yellow
Write-Host "Copy-Item Apex.UI\App.xaml.BACKUP Apex.UI\App.xaml -Force" -ForegroundColor Gray
