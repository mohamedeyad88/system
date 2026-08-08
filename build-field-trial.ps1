#Requires -Version 5.1
<#
.SYNOPSIS
  بناء نسخة تجريب ميداني — Apex Print OS

.DESCRIPTION
  يبني ويحزّم ApexPrintOS.exe + ApexLicenseTool.exe في مجلد إصدار نظيف.
  يُحدِّث رقم الإصدار تلقائياً من معامل -Version.

.PARAMETER Version
  رقم الإصدار (مثلاً "2.0.0"). افتراضي: "2.0.0"

.PARAMETER Out
  مجلد الإخراج الجذري. افتراضي: "D:\Apex\publish"

.EXAMPLE
  .\build-field-trial.ps1 -Version "2.0.1" -Out "D:\Apex\publish"
#>

param(
    [string]$Version = "2.0.0",
    [string]$Out     = "D:\Apex\publish"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot  = "$PSScriptRoot\Apex.PrintingSystem"
$UIProj       = "$ProjectRoot\Apex.UI\Apex.UI.csproj"
$AdminProj    = "$ProjectRoot\Apex.AdminTool\Apex.AdminTool.csproj"
$BuildLabel   = "v$Version"
$ReleaseDir   = "$Out\$BuildLabel"
$AppDir       = "$ReleaseDir\app"
$AdminDir     = "$ReleaseDir\admin-tool"
$BuildDate    = (Get-Date).ToString("yyyy-MM-dd HH:mm")

# ── Git commit hash ────────────────────────────────────────────────────────────
$Commit = "unknown"
try {
    $Commit = (git -C $ProjectRoot rev-parse --short HEAD 2>$null).Trim()
} catch {}

$InformationalVersion = "$Version-beta.1+$Commit"

Write-Host ""
Write-Host "══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Apex Print OS — Field Trial Build" -ForegroundColor Cyan
Write-Host "  Version  : $BuildLabel"
Write-Host "  Commit   : $Commit"
Write-Host "  Out Dir  : $ReleaseDir"
Write-Host "══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ── Clean output ───────────────────────────────────────────────────────────────
if (Test-Path $ReleaseDir) {
    Write-Host "→ Cleaning previous release folder..." -ForegroundColor Yellow
    Remove-Item $ReleaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $AppDir   -Force | Out-Null
New-Item -ItemType Directory -Path $AdminDir -Force | Out-Null

# ── Update version in .csproj (in-place patch) ─────────────────────────────────
Write-Host "→ Stamping version $Version into Apex.UI.csproj..." -ForegroundColor Yellow
$csproj = Get-Content $UIProj -Raw
$csproj = $csproj -replace '<Version>[^<]+</Version>',             "<Version>$Version</Version>"
$csproj = $csproj -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
$csproj = $csproj -replace '<FileVersion>[^<]+</FileVersion>',     "<FileVersion>$Version.0</FileVersion>"
$csproj = $csproj -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$InformationalVersion</InformationalVersion>"
Set-Content $UIProj $csproj -Encoding UTF8

# ── Build & Publish Apex.UI ────────────────────────────────────────────────────
Write-Host "→ Publishing Apex.UI..." -ForegroundColor Yellow
$buildLog = "$ReleaseDir\build-app.log"

dotnet publish $UIProj `
    -c Release `
    --no-restore `
    --output $AppDir `
    /p:DebugType=none `
    /p:DebugSymbols=false `
    2>&1 | Tee-Object -FilePath $buildLog

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Apex.UI publish FAILED — see $buildLog" -ForegroundColor Red
    exit 1
}

# ── Build & Publish AdminTool ──────────────────────────────────────────────────
Write-Host "→ Publishing AdminTool..." -ForegroundColor Yellow
$adminLog = "$ReleaseDir\build-admin.log"

dotnet publish $AdminProj `
    -c Release `
    --no-restore `
    --output $AdminDir `
    /p:DebugType=none `
    /p:DebugSymbols=false `
    2>&1 | Tee-Object -FilePath $adminLog

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ AdminTool publish FAILED — see $adminLog" -ForegroundColor Red
    exit 1
}

# ── Rename main exe ────────────────────────────────────────────────────────────
$srcExe  = "$AppDir\Apex.UI.exe"
$destExe = "$AppDir\ApexPrintOS.exe"
if (Test-Path $srcExe) {
    Copy-Item  $srcExe  $destExe -Force
    Remove-Item $srcExe -Force
}

# ── Remove PDB files ───────────────────────────────────────────────────────────
Get-ChildItem $AppDir,   $AdminDir -Filter "*.pdb" -Recurse | Remove-Item -Force
Get-ChildItem $AppDir,   $AdminDir -Filter "*.xml" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

# ── File sizes ─────────────────────────────────────────────────────────────────
$appExe   = Get-Item "$AppDir\ApexPrintOS.exe"    -ErrorAction SilentlyContinue
$adminExe = Get-Item "$AdminDir\ApexLicenseTool.exe" -ErrorAction SilentlyContinue

$appMB    = if ($appExe)   { [math]::Round($appExe.Length   / 1MB, 1) } else { "?" }
$adminMB  = if ($adminExe) { [math]::Round($adminExe.Length / 1MB, 1) } else { "?" }

# ── Write build-info.txt ───────────────────────────────────────────────────────
$info = @"
Apex Print OS - Field Trial Build
==================================
Version   : $BuildLabel
Built     : $BuildDate
Commit    : $Commit
Platform  : win-x64 / .NET 8 / SelfContained

FILES
------
app\ApexPrintOS.exe             ($appMB MB)  Main application
admin-tool\ApexLicenseTool.exe  ($adminMB MB)  License issuing tool (admin only)

TRIAL PERIOD
------------
Duration  : 7 days from first launch
Storage   : AppData + ProgramData + Registry (triple anti-tamper)
Protection: HMAC-SHA256 + Device ID binding + Clock rollback detection

REQUIREMENTS
------------
Windows 10 x64 or newer
No .NET Runtime needed (self-contained)
No installation required
"@
Set-Content "$ReleaseDir\build-info.txt" $info -Encoding UTF8

# ── Summary ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "══════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  BUILD COMPLETE" -ForegroundColor Green
Write-Host ""
Write-Host "  ApexPrintOS.exe         $appMB MB"    -ForegroundColor White
Write-Host "  ApexLicenseTool.exe     $adminMB MB"  -ForegroundColor White
Write-Host "  Output: $ReleaseDir"                  -ForegroundColor White
Write-Host "══════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""

# Open the output folder
Start-Process "explorer.exe" $ReleaseDir
