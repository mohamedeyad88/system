using Serilog;
using System;

namespace Apex.Services.Logging
{
    /// <summary>
    /// Production-grade logging for print operations.
    /// CRITICAL: This logger works in Release builds (unlike Debug.WriteLine).
    /// </summary>
    public static class PrintLogger
    {
        private static readonly ILogger _logger;
        
        static PrintLogger()
        {
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    "logs/apex-printing-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
                
            _logger.Information("=== Apex Printing System Logger Initialized ===");
        }
        
        public static void Info(string message, params object[] args)
        {
            _logger.Information(message, args);
        }
        
        public static void Warning(string message, params object[] args)
        {
            _logger.Warning(message, args);
        }
        
        public static void Error(Exception ex, string message, params object[] args)
        {
            _logger.Error(ex, message, args);
        }
        
        public static void Debug(string message, params object[] args)
        {
            _logger.Debug(message, args);
        }
        
        /// <summary>
        /// Log critical Win32 print API failure with full details.
        /// </summary>
        public static void Win32Error(int errorCode, string apiName, string printerName, string details = "")
        {
            var errorMessage = GetWin32ErrorMessage(errorCode);
            _logger.Error(
                "WIN32 PRINT API FAILED: {ApiName} returned error {ErrorCode} ({ErrorMessage}). Printer: '{Printer}'. Details: {Details}",
                apiName, errorCode, errorMessage, printerName, details);
        }
        
        private static string GetWin32ErrorMessage(int errorCode)
        {
            return errorCode switch
            {
                1801 => "Invalid printer name or printer not found (ERROR_INVALID_PRINTER_NAME)",
                5 => "Access denied - insufficient permissions (ERROR_ACCESS_DENIED)",
                1722 => "RPC server unavailable - printer offline or network issue (ERROR_RPC_S_SERVER_UNAVAILABLE)",
                2 => "File not found (ERROR_FILE_NOT_FOUND)",
                1814 => "Printer driver not installed (ERROR_UNKNOWN_PRINTER_DRIVER)",
                1804 => "Invalid datatype (ERROR_INVALID_DATATYPE)",
                3 => "Path not found (ERROR_PATH_NOT_FOUND)",
                1117 => "Spooler service stopped (ERROR_SERVICE_REQUEST_TIMEOUT)",
                _ => $"Unknown Win32 error code {errorCode}"
            };
        }
    }
}
