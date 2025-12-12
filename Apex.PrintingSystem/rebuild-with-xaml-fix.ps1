# Rebuild with XAML Fix
Write-Host "=== Rebuilding with XAML Resource Fix ===" -ForegroundColor Cyan

# Step 1: Clean everything
Write-Host "`n[1/4] Cleaning build artifacts..." -ForegroundColor Yellow
Remove-Item -Path "Apex.UI\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "Apex.UI\bin" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "✓ Cleaned" -ForegroundColor Green

# Step 2: Restore
Write-Host "`n[2/4] Restoring packages..." -ForegroundColor Yellow
dotnet restore Apex.UI\Apex.UI.csproj --no-cache
if ($LASTEXITCODE -ne 0) { 
    Write-Host "✗ Restore failed!" -ForegroundColor Red
    exit 1 
}
Write-Host "✓ Restored" -ForegroundColor Green

# Step 3: Build
Write-Host "`n[3/4] Building (this will embed XAML as BAML)..." -ForegroundColor Yellow
dotnet build Apex.UI\Apex.UI.csproj -c Release --no-incremental
if ($LASTEXITCODE -ne 0) { 
    Write-Host "✗ Build failed!" -ForegroundColor Red
    exit 1 
}
Write-Host "✓ Built successfully" -ForegroundColor Green

# Step 4: Verify BAML resources
Write-Host "`n[4/4] Verifying BAML resources are embedded..." -ForegroundColor Yellow
$dllPath = "Apex.UI\bin\Release\net8.0-windows\Apex.UI.dll"
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $dllPath).Path)
$bamlResources = $assembly.GetManifestResourceNames() | Where-Object { $_ -like "*.baml" }

if ($bamlResources.Count -gt 0) {
    Write-Host "✓ Found $($bamlResources.Count) BAML resources:" -ForegroundColor Green
    $bamlResources | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
}
else {
    Write-Host "✗ WARNING: No BAML resources found!" -ForegroundColor Red
}

Write-Host "`n=== BUILD COMPLETE ===" -ForegroundColor Cyan
Write-Host "`nYou can now run:" -ForegroundColor White
Write-Host ".\Apex.UI\bin\Release\net8.0-windows\Apex.UI.exe" -ForegroundColor Yellow
