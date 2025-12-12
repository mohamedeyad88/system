# Apex WPF Application - Deployment Guide

## ⚠️ Known Issues and Solutions

### Silent Startup Failure

If the application builds successfully but doesn't start (no window, no error):

1. **Check for error logs on your Desktop**
   - Look for `ApexStartup_ERROR.log` or `ApexStartup_YYYYMMDD_HHMMSS.log`
   - These contain detailed diagnostic information

2. **Run the dependency checker**
   ```powershell
   .\DependencyChecker.ps1
   ```

3. **Launch with diagnostics**
   ```cmd
   .\LaunchWithDiagnostics.bat
   ```

### Unicode Path Issues

**Problem**: Arabic folder names (`سطح المكتب`) can cause assembly loading failures

**Solutions**:
- **Option 1 (Recommended)**: Copy the entire `bin\Debug\net8.0-windows` folder to `C:\Temp\ApexTest` and run from there
- **Option 2**: Move your entire project to a path with only ASCII characters (e.g., `C:\Projects\Apex`)

### OneDrive Sync Issues

**Problem**: OneDrive may lock DLL files or cause file access issues

**Solutions**:
- Right-click the project folder → "Always keep on this device"
- Pause OneDrive synchronization during development
- Copy the project to a non-OneDrive location

## Required Runtime

### .NET 8 Desktop Runtime

**Check if installed**:
```powershell
dotnet --list-runtimes
```

Look for: `Microsoft.WindowsDesktop.App 8.x.x`

**Download if missing**:
- https://dotnet.microsoft.com/download/dotnet/8.0
- Choose: ".NET Desktop Runtime 8.0.x"

## Building the Application

### Debug Build
```powershell
cd "C:\Users\moham\OneDrive\سطح المكتب\Apex\system"
dotnet clean Apex.UI\Apex.UI.csproj
dotnet build Apex.UI\Apex.UI.csproj -c Debug
```

### Release Build
```powershell
dotnet build Apex.UI\Apex.UI.csproj -c Release
```

## Running the Application

### From Visual Studio
- Press F5 or click "Start"
- Diagnostic logs will still be created on Desktop

### From Command Line

**Option 1: Direct execution**
```powershell
cd Apex.UI\bin\Debug\net8.0-windows
.\Apex.UI.exe
```

**Option 2: With diagnostics**
```cmd
LaunchWithDiagnostics.bat
```

## Required Files in bin Folder

The following files must be present in the output folder:

### Core Application
- `Apex.UI.exe` - Main executable
- `Apex.UI.dll` - Application assembly
- `Apex.UI.runtimeconfig.json` - Runtime configuration

### Dependencies
- `Apex.Core.dll`
- `Apex.Services.dll`

### .NET Runtime Files
- Various `System.*.dll` files (automatically copied by .NET)
- `PresentationCore.dll`, `PresentationFramework.dll` (WPF assemblies)

## Troubleshooting Steps

### 1. Validate Environment
```powershell
.\DependencyChecker.ps1
```

Expected output:
- ✓ .NET 8 Desktop Runtime found
- ✓ Apex.UI.exe found
- ✓ All DLL dependencies found

### 2. Check Build Output
```powershell
dotnet build Apex.UI\Apex.UI.csproj --verbosity detailed
```

Look for:
- "Build succeeded" message
- No error lines
- Output path confirmation

### 3. Test in Clean Environment

Copy to C:\Temp:
```powershell
Copy-Item "Apex.UI\bin\Debug\net8.0-windows" -Destination "C:\Temp\ApexTest" -Recurse
cd C:\Temp\ApexTest
.\Apex.UI.exe
```

This eliminates Unicode path and OneDrive issues.

### 4. Enable Console Output (for debugging)

Temporarily change `Apex.UI.csproj`:
```xml
<OutputType>Exe</OutputType>  <!-- Instead of WinExe -->
```

Rebuild and run from PowerShell to see console output.

### 5. Check Windows Event Viewer

If the application crashes silently:
1. Open Event Viewer
2. Navigate to: Windows Logs → Application
3. Look for errors from "Application Error" source
4. Check the faulting module name

## Deployment Checklist

### For Development Machine
- [x] .NET 8 SDK installed
- [x] Project builds without errors
- [ ] Diagnostic logs confirm successful startup
- [ ] Application window appears

### For End-User Machine
- [ ] .NET 8 Desktop Runtime installed
- [ ] Application folder contains all required DLLs
- [ ] User has read/write permissions on application folder
- [ ] Application folder is in a simple path (no Unicode characters)
- [ ] Application folder is NOT in OneDrive (or OneDrive is paused)

## Common Error Messages

### "Could not load file or assembly..."
- **Cause**: Missing DLL dependency
- **Fix**: Rebuild project, ensure all dependencies copy to output

### "The application to execute does not exist"
- **Cause**: .NET 8 runtime not installed
- **Fix**: Install .NET 8 Desktop Runtime

### No error, no window (silent failure)
- **Cause**: Unicode path, XAML parsing error, missing dependency
- **Fix**: Check Desktop error logs, run DependencyChecker.ps1

### "Access denied" or file locking errors
- **Cause**: OneDrive sync, antivirus, or file permissions
- **Fix**: Pause OneDrive, exclude from antivirus, run as administrator

## Getting Diagnostic Information

All diagnostic information is automatically logged to your Desktop:

1. **ApexStartup_ERROR.log** - Created only if startup fails
2. **ApexStartup_YYYYMMDD_HHMMSS.log** - Detailed startup log (always created)
3. **ApexStartup_YYYYMMDD_HHMMSS_SUCCESS.log** - Marker file if startup succeeds

These logs contain:
- Environment information
- Loaded assemblies
- Exception details
- Validation results

## Support

If the application still fails to start after following these steps:

1. Locate the log files on your Desktop
2. Check the implementation_plan.md in the `.gemini` folder
3. Review the exact error message in the logs
4. Search for the error message online
5. Check if it's a known .NET or WPF issue
