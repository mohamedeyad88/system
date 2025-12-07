# Build and Package Script for Apex Printing System (GUI Installer)
$ErrorActionPreference = "Stop"

$solutionDir = "c:\Users\moham\OneDrive\سطح المكتب\Apex\system\Apex.PrintingSystem"
$uiProject = "$solutionDir\Apex.UI\Apex.UI.csproj"
$setupProject = "$solutionDir\Apex.Setup\Apex.Setup.csproj"
$publishDir = "$solutionDir\Publish"
$setupOutputDir = "$solutionDir\SetupOutput"
$payloadZip = "$solutionDir\Apex.Setup\publish.zip"

Write-Host "=========================================="
Write-Host "   Apex Printing System - GUI Installer Build"
Write-Host "=========================================="

# 1. Publish Apex.UI (Multi-File, Self-Contained)
Write-Host "1. Publishing Apex.UI (Multi-File)..."
if (Test-Path $publishDir) { Remove-Item -Path $publishDir -Recurse -Force }

& "C:\Program Files\dotnet\dotnet.exe" publish $uiProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -o $publishDir

# Verify Publish Output
$files = Get-ChildItem -Path $publishDir -Recurse
$fileCount = $files.Count
Write-Host "   -> Published $fileCount files to $publishDir"

if ($fileCount -lt 20) {
    Write-Error "Publish failed to produce enough files. Expected > 20, got $fileCount."
}

# 2. Zip the Publish Directory
Write-Host "2. Creating Payload (publish.zip)..."
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $payloadZip)

Write-Host "   -> Payload created: $payloadZip"

# 3. Build GUI Installer (Apex.Setup)
Write-Host "3. Building GUI Installer..."
if (Test-Path $setupOutputDir) { Remove-Item $setupOutputDir -Recurse -Force }

& "C:\Program Files\dotnet\dotnet.exe" publish $setupProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $setupOutputDir

# Rename to final name
$finalExe = "$setupOutputDir\ApexPrintingSystemSetup.exe"
Rename-Item "$setupOutputDir\Apex.Setup.exe" "ApexPrintingSystemSetup.exe" -Force

Write-Host "=========================================="
Write-Host "Build Complete!"
Write-Host "Installer: $finalExe"
Write-Host "=========================================="
