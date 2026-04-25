# Build EXE for Testing
$ErrorActionPreference = "Stop"

$projectPath = "D:\Apex\system\Apex.PrintingSystem\Apex.UI\Apex.UI.csproj"
$publishDir = "D:\Apex\system\Apex.PrintingSystem\Publish"

Write-Host "Building Release version..." -ForegroundColor Cyan

# Clean publish directory
if (Test-Path $publishDir) {
    Write-Host "Cleaning publish directory..." -ForegroundColor Yellow
    Remove-Item $publishDir -Recurse -Force
}

# Publish
Write-Host "Publishing application..." -ForegroundColor Yellow
dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir `
    --verbosity minimal

if ($LASTEXITCODE -eq 0) {
    $exePath = Join-Path $publishDir "Apex.UI.exe"
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        Write-Host "`n✅ Build successful!" -ForegroundColor Green
        Write-Host "EXE Location: $exePath" -ForegroundColor Cyan
        Write-Host "File Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Cyan
        Write-Host "Last Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Cyan
    } else {
        Write-Host "`n❌ EXE file not found in publish directory" -ForegroundColor Red
    }
} else {
    Write-Host "`n❌ Build failed!" -ForegroundColor Red
}

