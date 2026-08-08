#Requires -Version 5.1
<#
.SYNOPSIS
  Build the Apex Print OS installer (field-trial).

.DESCRIPTION
  1. Publishes Apex.UI as a self-contained single-file win-x64 app.
  2. Renames the main exe to ApexPrintOS.exe.
  3. Stages files into installer\stage\app.
  4. Compiles the setup with Inno Setup (ISCC.exe): EULA accept/decline page
     and a Control-Panel uninstall entry.

  Build-machine prerequisites:
    - .NET 8 SDK    https://dotnet.microsoft.com/download/dotnet/8.0
    - Inno Setup 6+ https://jrsoftware.org/isdl.php

.PARAMETER Version
  Package version (default 2.2.0 - matches Apex.UI.csproj).

.EXAMPLE
  .\build-installer.ps1
  .\build-installer.ps1 -Version 2.2.0
#>

param(
    [string]$Version = "2.2.0",
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root       = $PSScriptRoot
$UIProj     = Join-Path $Root "Apex.PrintingSystem\Apex.UI\Apex.UI.csproj"
$InstallDir = Join-Path $Root "installer"
$StageApp   = Join-Path $InstallDir "stage\app"
$OutputDir  = Join-Path $InstallDir "output"
$IssFile    = Join-Path $InstallDir "ApexPrintOS.iss"

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  Apex Print OS - Installer Build  (v$Version)"         -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

# -- 0. Verify prerequisites -------------------------------------------------
$hasSdk = $false
try { $null = & dotnet --list-sdks 2>$null; if ($LASTEXITCODE -eq 0) { $hasSdk = $true } } catch {}
if (-not $hasSdk) {
    throw "No .NET SDK found. Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
}

$iscc = @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    throw "Inno Setup (ISCC.exe) not found. Install it: https://jrsoftware.org/isdl.php"
}
Write-Host "-> dotnet SDK : OK"    -ForegroundColor Green
Write-Host "-> Inno Setup : $iscc" -ForegroundColor Green

# -- 1. Clean stage ----------------------------------------------------------
if (Test-Path $StageApp) { Remove-Item $StageApp -Recurse -Force }
New-Item -ItemType Directory -Path $StageApp  -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# -- 2. Publish Apex.UI (self-contained, single-file, win-x64) ---------------
Write-Host "-> Publishing Apex.UI ..." -ForegroundColor Yellow
& dotnet publish $UIProj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o $StageApp `
    /p:Version=$Version `
    /p:DebugType=none `
    /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Apex.UI publish failed." }

# -- 3. Rename main exe -> ApexPrintOS.exe -----------------------------------
$srcExe  = Join-Path $StageApp "Apex.UI.exe"
$destExe = Join-Path $StageApp "ApexPrintOS.exe"
if (Test-Path $srcExe) {
    Move-Item $srcExe $destExe -Force
} elseif (-not (Test-Path $destExe)) {
    throw "Published executable not found."
}

# -- 4. Trim symbol/doc files ------------------------------------------------
Get-ChildItem $StageApp -Include *.pdb,*.xml -Recurse -File -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

# -- 5. Compile the installer ------------------------------------------------
Write-Host "-> Compiling installer via Inno Setup ..." -ForegroundColor Yellow
& $iscc "/DMyAppVersion=$Version" "/DAppSrc=stage\app" $IssFile
if ($LASTEXITCODE -ne 0) { throw "Installer compilation (ISCC) failed." }

$setup = Join-Path $OutputDir "ApexPrintOS-Setup-$Version.exe"
Write-Host ""
Write-Host "======================================================" -ForegroundColor Green
Write-Host "  BUILD COMPLETE"           -ForegroundColor Green
Write-Host "  Installer: $setup"        -ForegroundColor White
Write-Host "======================================================" -ForegroundColor Green

if (Test-Path $setup) { Start-Process explorer.exe "/select,`"$setup`"" }
