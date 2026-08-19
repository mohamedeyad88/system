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

        /// <summary>
        /// Where the print log is written. Always the same absolute path, whatever the
        /// process working directory happens to be.
        /// </summary>
        public static string LogFolder { get; }

        static PrintLogger()
        {
            // An ABSOLUTE path under LocalAppData.
            //
            // This used to be the relative "logs/apex-printing-.log", which resolves
            // against the working directory — under Program Files that is not writable
            // by a standard user, and Serilog drops file-sink errors silently. The
            // result was an installed copy that produced no log at all, exactly when a
            // failed print run most needed evidence. LocalAppData is always writable
            // and matches where AppDiagnostics already writes.
            LogFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ApexPrintingSystem", "logs");

            try { System.IO.Directory.CreateDirectory(LogFolder); } catch { /* falls back below */ }

            var config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

            try
            {
                config = config.WriteTo.File(
                    System.IO.Path.Combine(LogFolder, "apex-printing-.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
            }
            catch
            {
                // Console-only rather than no logger at all: losing the file sink must
                // not take the whole print pipeline down.
            }

            _logger = config.CreateLogger();

            _logger.Information("=== Apex Printing System Logger Initialized ===");
            _logger.Information("Log folder: {Folder}", LogFolder);
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

        /// <summary>
        /// Records a failure that has no exception behind it — a call that reported
        /// false, a result that came back empty.
        /// </summary>
        public static void Error(string message, params object[] args)
        {
            _logger.Error(message, args);
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
