# Apex Printing System - PowerShell Launch Script
# تشغيل التطبيق

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Apex Printing System - تشغيل التطبيق" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$uiProjectPath = Join-Path $scriptPath "Apex.UI\Apex.UI.csproj"

if (-not (Test-Path $uiProjectPath)) {
    Write-Host "❌ Project file not found: $uiProjectPath" -ForegroundColor Red
    pause
    exit 1
}

Set-Location $scriptPath

Write-Host "[1/2] Building project..." -ForegroundColor Yellow
$buildResult = dotnet build $uiProjectPath --verbosity quiet 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "❌ Build failed!" -ForegroundColor Red
    $buildResult | Select-String -Pattern "error" | Select-Object -First 5
    pause
    exit 1
}

Write-Host "[2/2] Starting application..." -ForegroundColor Green
Write-Host ""

dotnet run --project $uiProjectPath

