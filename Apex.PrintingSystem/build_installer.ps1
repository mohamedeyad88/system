# Build and Package Script for Apex Printing System
$ErrorActionPreference = "Stop"

$solutionDir = "c:\Users\moham\OneDrive\سطح المكتب\Apex\system\Apex.PrintingSystem"
$uiProject = "$solutionDir\Apex.UI\Apex.UI.csproj"
$installerProject = "$solutionDir\Apex.Installer\Apex.Installer.csproj"
$publishDir = "$solutionDir\Publish"
$installerOutputDir = "$solutionDir\InstallerOutput"

# 1. Publish Apex.UI (Single File)
Write-Host "1. Publishing Apex.UI..."
& "C:\Program Files\dotnet\dotnet.exe" publish $uiProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

# 2. Prepare Installer Resources
Write-Host "2. Preparing Installer Resources..."
# Copy the published EXE to the Installer project directory so it can be embedded
Copy-Item "$publishDir\Apex.UI.exe" "$solutionDir\Apex.Installer\Apex.UI.exe" -Force
Copy-Item "$solutionDir\schema.sql" "$solutionDir\Apex.Installer\schema.sql" -Force

# 3. Build Installer
Write-Host "3. Building Installer..."
# We need to ensure the .csproj includes the embedded resources. 
# Since we can't easily edit .csproj with PS safely every time, we assume it's set up or we add it dynamically?
# Better to set it up once in the .csproj file.

& "C:\Program Files\dotnet\dotnet.exe" publish $installerProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $installerOutputDir

# Rename to final name
Rename-Item "$installerOutputDir\Apex.Installer.exe" "$installerOutputDir\ApexPrintingSystemInstaller.exe" -Force

Write-Host "=========================================="
Write-Host "Build Complete!"
Write-Host "Installer: $installerOutputDir\ApexPrintingSystemInstaller.exe"
Write-Host "=========================================="
