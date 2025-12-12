# WPF Build Cache Cleanup Script
# Run this script to fix the "already contains a definition" error

Write-Host "=== Cleaning WPF Build Cache ===" -ForegroundColor Cyan

# Step 1: Clean the solution
Write-Host "`n[1/5] Cleaning solution..." -ForegroundColor Yellow
dotnet clean Apex.PrintingSystem.sln --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Clean failed, continuing anyway..." -ForegroundColor Yellow
}

# Step 2: Delete ALL temporary wpftmp files
Write-Host "`n[2/5] Removing temporary wpftmp files..." -ForegroundColor Yellow
Get-ChildItem -Path . -Filter "*_wpftmp.csproj" -Recurse | Remove-Item -Force
Write-Host "Removed wpftmp files" -ForegroundColor Green

# Step 3: Delete obj and bin folders for Apex.UI
Write-Host "`n[3/5] Removing obj and bin folders..." -ForegroundColor Yellow
Remove-Item -Path "Apex.UI\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "Apex.UI\bin" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Removed build artifacts" -ForegroundColor Green

# Step 4: Restore dependencies
Write-Host "`n[4/5] Restoring dependencies..." -ForegroundColor Yellow
dotnet restore Apex.UI\Apex.UI.csproj
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Restore failed!" -ForegroundColor Red
    exit 1
}

# Step 5: Build with clean cache
Write-Host "`n[5/5] Building Apex.UI..." -ForegroundColor Yellow
dotnet build Apex.UI\Apex.UI.csproj --configuration Release --no-incremental
if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host "`nYou can now run the application with:" -ForegroundColor Cyan
    Write-Host "dotnet run --project Apex.UI\Apex.UI.csproj" -ForegroundColor White
} else {
    Write-Host "`n❌ BUILD FAILED!" -ForegroundColor Red
    Write-Host "Check the error messages above for details." -ForegroundColor Yellow
    exit 1
}
