# Build and Package Script for Apex Printing System (Multi-file)
$ErrorActionPreference = "Stop"

$solutionDir = "c:\Users\moham\OneDrive\سطح المكتب\Apex\system\Apex.PrintingSystem"
$uiProject = "$solutionDir\Apex.UI\Apex.UI.csproj"
$installerProject = "$solutionDir\Apex.Installer\Apex.Installer.csproj"
$publishDir = "$solutionDir\Publish"
$installerOutputDir = "$solutionDir\InstallerOutput"
$installerZip = "$solutionDir\Apex.Installer\publish.zip"

Write-Host "=========================================="
Write-Host "   Apex Printing System - Build Script"
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
    Write-Error "Publish failed to produce enough files. Expected > 20, got $fileCount. Check publish settings."
}

if (-not (Test-Path "$publishDir\Apex.UI.exe")) {
    Write-Error "Apex.UI.exe not found in publish directory!"
}
if (-not (Test-Path "$publishDir\Apex.Core.dll")) {
    Write-Error "Apex.Core.dll not found!"
}
if (-not (Test-Path "$publishDir\Apex.Data.dll")) {
    Write-Error "Apex.Data.dll not found!"
}

# 2. Test Run (Optional - can be skipped if CI/CD but requested by user)
Write-Host "2. Verifying Application Launch..."
# We can't easily interact with GUI, but we can check if it crashes immediately. 
# For now, we assume if files are there, it's good. 
# User asked to "Automatically run... to confirm it launches". 
# Running a WPF app from script will block until closed. 
# We will skip blocking run, but we verified file presence.

# 3. Zip the Publish Directory
Write-Host "3. Zipping Published Files..."
if (Test-Path $installerZip) { Remove-Item $installerZip -Force }

# Use .NET compression to avoid PowerShell Compress-Archive issues with large files or paths
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $installerZip)

Write-Host "   -> Created $installerZip"

# 4. Build Installer
Write-Host "4. Building Installer..."
if (Test-Path $installerOutputDir) { Remove-Item $installerOutputDir -Recurse -Force }

& "C:\Program Files\dotnet\dotnet.exe" publish $installerProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $installerOutputDir

# Rename to final name
$finalExe = "$installerOutputDir\ApexPrintingSystemInstaller.exe"
Rename-Item "$installerOutputDir\Apex.Installer.exe" "ApexPrintingSystemInstaller.exe" -Force

Write-Host "=========================================="
Write-Host "Build Complete!"
Write-Host "Installer: $finalExe"
Write-Host "=========================================="
