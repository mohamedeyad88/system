# Apex Printing System - Clean Publish Script

$ErrorActionPreference = "Stop"
$solutionDir = $PSScriptRoot
$projectPath = "$solutionDir\Apex.UI\Apex.UI.csproj"
$publishDir = "$solutionDir\Publish"

Write-Host "Starting Clean Publish Process..." -ForegroundColor Cyan

# 1. Kill running instances
Write-Host "Stopping running instances..." -ForegroundColor Yellow
Get-Process "Apex.UI" -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Clean Directories
Write-Host "Cleaning directories..." -ForegroundColor Yellow
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Get-ChildItem -Path $solutionDir -Include bin, obj -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# 3. Restore
Write-Host "Restoring packages..." -ForegroundColor Yellow
dotnet restore "$solutionDir\Apex.PrintingSystem.sln"

# 4. Build & Publish
Write-Host "Publishing..." -ForegroundColor Yellow
dotnet publish $projectPath -c Release -o $publishDir -r win-x64 --self-contained false /p:DebugType=None /p:DebugSymbols=false

# 5. Create Version File
$version = @{
    Version     = "1.0.0"
    BuildDate   = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    Environment = "Production"
} | ConvertTo-Json
$version | Out-File "$publishDir\version.json"

Write-Host "Publish Complete! Output: $publishDir" -ForegroundColor Green
} else {
    Write-Host "Publish Failed!" -ForegroundColor Red
}
