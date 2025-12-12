# Apex Dependency Checker
# Validates runtime environment and dependencies

Write-Host "=== APEX DEPENDENCY CHECKER ===" -ForegroundColor Cyan
Write-Host ""

$ErrorCount = 0
$WarningCount = 0

# Check .NET 8 Runtime
Write-Host "Checking .NET 8 Runtime..." -ForegroundColor Yellow
try {
    $dotnetInfo = & dotnet --list-runtimes 2>&1
    $hasNet8 = $dotnetInfo | Select-String "Microsoft.WindowsDesktop.App 8\."
    
    if ($hasNet8) {
        Write-Host "✓ .NET 8 Desktop Runtime found" -ForegroundColor Green
        $hasNet8 | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    } else {
        Write-Host "✗ .NET 8 Desktop Runtime NOT FOUND" -ForegroundColor Red
        Write-Host "  Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
        $ErrorCount++
    }
} catch {
    Write-Host "✗ dotnet command not found - .NET is not installed" -ForegroundColor Red
    $ErrorCount++
}

Write-Host ""

# Check bin folder
Write-Host "Checking application binaries..." -ForegroundColor Yellow
$binPath = Join-Path $PSScriptRoot "Apex.UI\bin\Debug\net8.0-windows"

if (Test-Path $binPath) {
    Write-Host "✓ Bin folder found: $binPath" -ForegroundColor Green
    
    # Check main executable
    $exePath = Join-Path $binPath "Apex.UI.exe"
    if (Test-Path $exePath) {
        Write-Host "✓ Apex.UI.exe found" -ForegroundColor Green
        $fileInfo = Get-Item $exePath
        Write-Host "  Size: $($fileInfo.Length) bytes" -ForegroundColor Gray
        Write-Host "  Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
    } else {
        Write-Host "✗ Apex.UI.exe NOT FOUND" -ForegroundColor Red
        Write-Host "  Run: dotnet build Apex.UI\Apex.UI.csproj" -ForegroundColor Yellow
        $ErrorCount++
    }
    
    Write-Host ""
    
    # Check required DLLs
    Write-Host "Checking dependencies..." -ForegroundColor Yellow
    $requiredDlls = @(
        "Apex.UI.dll",
        "Apex.Core.dll",
        "Apex.Services.dll"
    )
    
    foreach ($dll in $requiredDlls) {
        $dllPath = Join-Path $binPath $dll
        if (Test-Path $dllPath) {
            Write-Host "  ✓ $dll" -ForegroundColor Green
        } else {
            Write-Host "  ✗ $dll MISSING" -ForegroundColor Red
            $ErrorCount++
        }
    }
    
    Write-Host ""
    
    # Check for runtime config
    $runtimeConfig = Join-Path $binPath "Apex.UI.runtimeconfig.json"
    if (Test-Path $runtimeConfig) {
        Write-Host "✓ Runtime configuration found" -ForegroundColor Green
    } else {
        Write-Host "⚠ Runtime configuration missing" -ForegroundColor Yellow
        $WarningCount++
    }
    
} else {
    Write-Host "✗ Bin folder not found: $binPath" -ForegroundColor Red
    Write-Host "  Build the project first!" -ForegroundColor Yellow
    $ErrorCount++
}

Write-Host ""

# Check for Unicode path issues
Write-Host "Checking for path issues..." -ForegroundColor Yellow
$currentPath = $PSScriptRoot
$hasUnicode = $currentPath -match '[^\x00-\x7F]'

if ($hasUnicode) {
    Write-Host "⚠ WARNING: Path contains non-ASCII characters" -ForegroundColor Yellow
    Write-Host "  Path: $currentPath" -ForegroundColor Gray
    Write-Host "  This may cause issues with some .NET components" -ForegroundColor Yellow
    Write-Host "  Consider copying to: C:\Temp\Apex for testing" -ForegroundColor Yellow
    $WarningCount++
} else {
    Write-Host "✓ Path contains only ASCII characters" -ForegroundColor Green
}

Write-Host ""

# Check OneDrive status
Write-Host "Checking OneDrive..." -ForegroundColor Yellow
if ($currentPath -match "OneDrive") {
    Write-Host "⚠ Project is in OneDrive folder" -ForegroundColor Yellow
    Write-Host "  This may cause file locking issues" -ForegroundColor Yellow
    Write-Host "  If you experience problems, copy to a local folder" -ForegroundColor Yellow
    $WarningCount++
} else {
    Write-Host "✓ Project is not in OneDrive" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
if ($ErrorCount -eq 0 -and $WarningCount -eq 0) {
    Write-Host "✓ All checks passed!" -ForegroundColor Green
    Write-Host "  You can run: .\Apex.UI\bin\Debug\net8.0-windows\Apex.UI.exe" -ForegroundColor Yellow
} else {
    if ($ErrorCount -gt 0) {
        Write-Host "✗ $ErrorCount error(s) found - FIX REQUIRED" -ForegroundColor Red
    }
    if ($WarningCount -gt 0) {
        Write-Host "⚠ $WarningCount warning(s) found" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
