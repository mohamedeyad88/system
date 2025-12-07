# Publish Script for Apex Printing System
$ErrorActionPreference = "Stop"

Write-Host "Restoring dependencies..."
& "C:\Program Files\dotnet\dotnet.exe" restore "c:\Users\moham\OneDrive\سطح المكتب\Apex\system\Apex.PrintingSystem\Apex.UI\Apex.UI.csproj"

Write-Host "Building and Publishing..."
# Publish as a single file, self-contained executable for Windows x64
& "C:\Program Files\dotnet\dotnet.exe" publish "c:\Users\moham\OneDrive\سطح المكتب\Apex\system\Apex.PrintingSystem\Apex.UI\Apex.UI.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "c:\Users\moham\OneDrive\سطح المكتب\Apex\system\Apex.PrintingSystem\Publish"

Write-Host "Publish completed successfully."
Write-Host "Executable located at: c:\Users\moham\OneDrive\سطح المكتب\Apex\system\Apex.PrintingSystem\Publish\Apex.UI.exe"
