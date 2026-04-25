# Apex Printing System - Publish Script
# نشر التطبيق

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Apex Printing System - نشر التطبيق" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$uiProjectPath = Join-Path $scriptPath "Apex.UI\Apex.UI.csproj"
$publishDir = Join-Path $scriptPath "publish"
$configuration = "Release"

if (-not (Test-Path $uiProjectPath)) {
    Write-Host "❌ Project file not found: $uiProjectPath" -ForegroundColor Red
    pause
    exit 1
}

Set-Location $scriptPath

Write-Host "[1/3] Cleaning previous publish..." -ForegroundColor Yellow
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

Write-Host "[2/3] Publishing application..." -ForegroundColor Yellow
$publishArgs = @(
    "publish",
    $uiProjectPath,
    "--configuration", $configuration,
    "--output", $publishDir,
    "--self-contained", "true",
    "--runtime", "win-x64",
    "/p:PublishSingleFile=true",
    "/p:IncludeAllContentForSelfExtract=true",
    "/p:EnableCompressionInSingleFile=true"
)

$publishResult = & dotnet $publishArgs 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "❌ Publish failed!" -ForegroundColor Red
    $publishResult | Select-String -Pattern "error" | Select-Object -First 10
    pause
    exit 1
}

Write-Host "[3/3] Creating run script..." -ForegroundColor Yellow
$runScript = @"
@echo off
cd /d "%~dp0"
start "" "Apex.UI.exe"
"@
$runScript | Out-File -FilePath (Join-Path $publishDir "run.bat") -Encoding ASCII

Write-Host ""
Write-Host "✅ Publish completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "📁 Published files location: $publishDir" -ForegroundColor Cyan
Write-Host "🚀 To run: Navigate to publish folder and double-click 'Apex.UI.exe' or 'run.bat'" -ForegroundColor Cyan
Write-Host ""
pause
