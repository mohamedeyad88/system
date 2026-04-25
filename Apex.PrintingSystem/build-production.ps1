# ============================================================================
# PrintOS Production Build Script
# Build & Release Engineer - Professional Production Build
# ============================================================================
# This script creates a production-ready single-file EXE
# Requirements: .NET 8 SDK (no Visual Studio needed)
# ============================================================================

$ErrorActionPreference = "Stop"

# Configuration
$SolutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectPath = Join-Path $SolutionDir "Apex.UI\Apex.UI.csproj"
$OutputDir = Join-Path $SolutionDir "PrintOS_Test_Build"
$PublishDir = Join-Path $SolutionDir "bin\Release\Publish"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   PrintOS Production Build" -ForegroundColor Cyan
Write-Host "   Professional Release Build" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean previous builds
Write-Host "[1/6] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
    Write-Host "   -> Cleaned output directory" -ForegroundColor Green
}
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
    Write-Host "   -> Cleaned publish directory" -ForegroundColor Green
}

# Step 2: Restore dependencies
Write-Host "[2/6] Restoring NuGet packages..." -ForegroundColor Yellow
$restoreResult = dotnet restore $ProjectPath -r win-x64 --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "   -> ERROR: Restore failed!" -ForegroundColor Red
    exit 1
}
Write-Host "   -> Dependencies restored" -ForegroundColor Green

# Step 3: Build Release
Write-Host "[3/6] Building Release configuration..." -ForegroundColor Yellow
$buildOutput = dotnet build $ProjectPath -c Release -r win-x64 --no-restore --verbosity minimal 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "   -> ERROR: Build failed!" -ForegroundColor Red
    Write-Host $buildOutput -ForegroundColor Red
    exit 1
}
Write-Host "   -> Build successful" -ForegroundColor Green

# Step 4: Publish as Single File
Write-Host "[4/6] Publishing as Single File EXE..." -ForegroundColor Yellow
Write-Host "   -> Configuration: Release" -ForegroundColor Gray
Write-Host "   -> Runtime: win-x64" -ForegroundColor Gray
Write-Host "   -> Self-contained: true" -ForegroundColor Gray
Write-Host "   -> Single File: true" -ForegroundColor Gray
Write-Host "   -> ReadyToRun: true" -ForegroundColor Gray
Write-Host "   -> Compression: enabled" -ForegroundColor Gray
Write-Host "   -> Trimming: disabled (WPF compatibility)" -ForegroundColor Gray

$publishResult = dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:PublishTrimmed=false `
    -o $PublishDir `
    --verbosity minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host "   -> ERROR: Publish failed!" -ForegroundColor Red
    exit 1
}

# Step 5: Verify output
Write-Host "[5/6] Verifying output..." -ForegroundColor Yellow
$exePath = Join-Path $PublishDir "Apex.UI.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "   -> ERROR: EXE not found at $exePath" -ForegroundColor Red
    exit 1
}

$exeSize = (Get-Item $exePath).Length / 1MB
Write-Host "   -> EXE found: $exePath" -ForegroundColor Green
Write-Host "   -> Size: $([math]::Round($exeSize, 2)) MB" -ForegroundColor Green

# Step 6: Prepare delivery folder
Write-Host "[6/6] Preparing delivery folder..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $OutputDir "Logs") -Force | Out-Null

# Copy EXE and rename
$finalExe = Join-Path $OutputDir "PrintOS.exe"
Copy-Item $exePath $finalExe -Force
Write-Host "   -> Copied EXE to: $finalExe" -ForegroundColor Green

# Copy schema.sql if exists
$schemaPath = Join-Path $SolutionDir "schema.sql"
if (Test-Path $schemaPath) {
    Copy-Item $schemaPath (Join-Path $OutputDir "schema.sql") -Force
    Write-Host "   -> Copied schema.sql" -ForegroundColor Green
}

# Create README.txt
$readmePath = Join-Path $OutputDir "README.txt"
$readmeContent = @"
==========================================
PrintOS - Production Release
==========================================

VERSION: Production Build
BUILD DATE: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
TARGET: Windows 10/11 (x64)
DEPLOYMENT: Self-contained Single File

==========================================
QUICK START
==========================================

1. Double-click PrintOS.exe to launch
2. No installation required
3. No .NET Runtime needed
4. Works on any Windows 10/11 PC

==========================================
IMPORTANT NOTES
==========================================

SMART PRINTING ENGINE:
- All printing operations go through Smart Printing Engine
- Background processing enabled by default
- No direct OS API calls
- Supports large files (GBs)
- Network printer support included

LOGS:
- Log files are saved in: Logs\
- Check logs for troubleshooting
- Logs rotate automatically

PERFORMANCE:
- First launch may take 2-3 seconds (extraction)
- Subsequent launches are instant
- Background services start automatically
- No console window (GUI only)

SYSTEM REQUIREMENTS:
- Windows 10 (version 1809+) or Windows 11
- x64 architecture
- 4GB RAM minimum
- Network access (for network printers)

==========================================
TROUBLESHOOTING
==========================================

If the application doesn't start:
1. Check Windows Event Viewer
2. Review Logs\ folder
3. Ensure Windows is up to date
4. Try running as Administrator

If printing fails:
1. Verify printer is online
2. Check network connection (for network printers)
3. Review Logs\ for error details
4. Ensure printer drivers are installed

==========================================
SUPPORT
==========================================

For production issues:
- Check Logs\ folder first
- Review error messages in application
- Contact system administrator

==========================================
"@

Set-Content -Path $readmePath -Value $readmeContent -Encoding UTF8
Write-Host "   -> Created README.txt" -ForegroundColor Green

# Final summary
Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   BUILD COMPLETE!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output Location: $OutputDir" -ForegroundColor White
Write-Host "Executable: PrintOS.exe" -ForegroundColor White
Write-Host "Size: $([math]::Round($exeSize, 2)) MB" -ForegroundColor White
Write-Host ""
Write-Host "Ready for production testing!" -ForegroundColor Green
Write-Host ""
