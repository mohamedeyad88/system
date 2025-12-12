using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Apex.UI;

/// <summary>
/// Comprehensive startup diagnostics with file-based logging
/// </summary>
public static class StartupDiagnostics
{
    private static readonly string LogPath;
    private static readonly string ErrorLogPath;
    private static readonly StringBuilder LogBuffer = new();
    private static bool _initialized = false;

    static StartupDiagnostics()
    {
        // Log to Desktop (safe location outside OneDrive issues)
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            LogPath = Path.Combine(desktop, $"ApexStartup_{timestamp}.log");
            ErrorLogPath = Path.Combine(desktop, "ApexStartup_ERROR.log");
        }
        catch
        {
            // Fallback to temp if desktop fails
            LogPath = Path.Combine(Path.GetTempPath(), $"ApexStartup_{DateTime.Now:yyyyMMddHHmmss}.log");
            ErrorLogPath = Path.Combine(Path.GetTempPath(), "ApexStartup_ERROR.log");
        }
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            Log("=== APEX PRINTING SYSTEM - STARTUP DIAGNOSTICS ===");
            Log($"Log Path: {LogPath}");
            Log($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Log($"Working Directory: {Environment.CurrentDirectory}");
            Log($"Command Line: {Environment.CommandLine}");
            Log("");

            LogEnvironmentInfo();
            LogAssemblyInfo();
            
            FlushLog();
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(ErrorLogPath, $"Logging initialization failed: {ex}");
            }
            catch
            {
                // Complete failure
            }
        }
    }

    public static void LogEnvironmentInfo()
    {
        Log("--- ENVIRONMENT INFO ---");
        Log($"OS Version: {Environment.OSVersion}");
        Log($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        Log($"64-bit Process: {Environment.Is64BitProcess}");
        Log($".NET Version: {Environment.Version}");
        Log($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Log($"Machine Name: {Environment.MachineName}");
        Log($"User Name: {Environment.UserName}");
        Log("");
    }

    public static void LogAssemblyInfo()
    {
        Log("--- ASSEMBLY INFO ---");
        try
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                Log($"Entry Assembly: {entryAssembly.FullName}");
                Log($"Location: {entryAssembly.Location}");
                
                if (ContainsNonAscii(entryAssembly.Location))
                {
                    Log("⚠️ WARNING: Assembly path contains non-ASCII characters!");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR getting assembly info: {ex.Message}");
        }
        Log("");
    }

    public static void Log(string message)
    {
        LogBuffer.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    public static void LogException(string context, Exception ex)
    {
        Log($"!!! EXCEPTION IN {context} !!!");
        Log($"Type: {ex.GetType().FullName}");
        Log($"Message: {ex.Message}");
        Log($"Stack Trace:");
        Log(ex.StackTrace ?? "(no stack trace)");
        
        if (ex.InnerException != null)
        {
            Log("");
            Log("--- INNER EXCEPTION ---");
            LogException("Inner", ex.InnerException);
        }
        
        FlushLog();
        CreateErrorMarker(ex);
    }

    public static void LogSuccess(string message)
    {
        Log($"✓ {message}");
        FlushLog();
    }

    public static void LogWarning(string message)
    {
        Log($"⚠ {message}");
        FlushLog();
    }

    public static void LogError(string message)
    {
        Log($"✗ {message}");
        FlushLog();
    }

    public static void FlushLog()
    {
        try
        {
            File.AppendAllText(LogPath, LogBuffer.ToString(), Encoding.UTF8);
            LogBuffer.Clear();
        }
        catch
        {
            // Can't log the log failure
        }
    }

    private static bool ContainsNonAscii(string text)
    {
        foreach (char c in text)
        {
            if (c > 127)
                return true;
        }
        return false;
    }

    public static void CreateSuccessMarker()
    {
        try
        {
            var successPath = LogPath.Replace(".log", "_SUCCESS.txt");
            File.WriteAllText(successPath, $"Application started successfully at {DateTime.Now}");
        }
        catch
        {
            // Ignore
        }
    }

    public static void CreateErrorMarker(Exception ex)
    {
        try
        {
            var errorContent = new StringBuilder();
            errorContent.AppendLine("=== APEX FAILED TO START ===");
            errorContent.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            errorContent.AppendLine("");
            errorContent.AppendLine($"Error: {ex.Message}");
            errorContent.AppendLine("");
            errorContent.AppendLine("Exception Type: " + ex.GetType().FullName);
            errorContent.AppendLine("");
            errorContent.AppendLine("Stack Trace:");
            errorContent.AppendLine(ex.StackTrace);
            
            if (ex.InnerException != null)
            {
                errorContent.AppendLine("");
                errorContent.AppendLine("=== INNER EXCEPTION ===");
                errorContent.AppendLine($"Error: {ex.InnerException.Message}");
                errorContent.AppendLine("Type: " + ex.InnerException.GetType().FullName);
                errorContent.AppendLine("Stack:");
                errorContent.AppendLine(ex.InnerException.StackTrace);
            }
            
            errorContent.AppendLine("");
            errorContent.AppendLine($"Detailed log: {LogPath}");
            
            File.WriteAllText(ErrorLogPath, errorContent.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Even error marker failed
        }
    }
}
