# Incremental Resource Test Script
# Tests resources one by one to find the problematic file

Write-Host "=== Testing Resources Incrementally ===" -ForegroundColor Cyan

Write-Host "`n[1/2] Rebuilding with Theme.xaml only..." -ForegroundColor Yellow
Remove-Item -Path "Apex.UI\obj" -Recurse -Force -ErrorAction SilentlyContinue
dotnet build Apex.UI\Apex.UI.csproj --configuration Release --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n[2/2] Running..." -ForegroundColor Yellow
Write-Host "`nIf app crashes: Theme.xaml is the problem" -ForegroundColor Cyan
Write-Host "If app runs: Try next resource`n" -ForegroundColor Cyan

dotnet run --project Apex.UI\Apex.UI.csproj --configuration Release --no-build
