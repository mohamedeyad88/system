# Build Script for Apex Printing System MSI
$ErrorActionPreference = "Stop"

# Paths
$projectPath = "..\Apex.UI\Apex.UI.csproj"
$publishDir = "..\..\Publish"
$wixObjDir = "obj"
$msiName = "ApexPrintingSystemSetup.msi"

# 1. Clean and Publish Application
Write-Host "Publishing Application..."
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $publishDir

# 2. Harvest Files using Heat
Write-Host "Harvesting Files..."
# Check if WiX is in PATH, otherwise warn
if (-not (Get-Command "heat" -ErrorAction SilentlyContinue)) {
    Write-Warning "WiX Toolset (heat.exe) not found in PATH. Please install WiX Toolset v3.11+."
    exit 1
}

heat dir $publishDir -dr INSTALLFOLDER -cg PublishedComponents -gg -g1 -sf -srd -var "var.PublishDir" -out "PublishedFiles.wxs"

# 3. Compile (Candle)
Write-Host "Compiling WiX Source..."
if (-not (Test-Path $wixObjDir)) { New-Item -ItemType Directory -Path $wixObjDir }

candle "Product.wxs" "PublishedFiles.wxs" -dPublishDir="$publishDir" -arch x64 -out "$wixObjDir\"

# 4. Link (Light)
Write-Host "Linking MSI..."
light "$wixObjDir\Product.wxs" "$wixObjDir\PublishedFiles.wxs" -out $msiName -b $publishDir -ext WixUIExtension

Write-Host "MSI Generated Successfully: $msiName"
