using System;
using System.IO;
using System.Text;

namespace Apex.Core.Diagnostics
{
    /// <summary>
    /// Lightweight, dependency-free diagnostic sink for the kind of operational
    /// failures that were previously swallowed by empty <c>catch {}</c> blocks.
    ///
    /// Writes a single timestamped line (plus exception detail) to
    /// %LocalAppData%/ApexPrintingSystem/Logs/diagnostics.log. It is intentionally
    /// best-effort and never throws — logging a failure must never crash the caller.
    /// Use this instead of an empty catch so non-fatal errors remain diagnosable.
    /// </summary>
    public static class AppDiagnostics
    {
        private static readonly object _gate = new();

        private static string LogPath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ApexPrintingSystem", "Logs");
                return Path.Combine(dir, "diagnostics.log");
            }
        }

        /// <summary>Records a non-fatal exception with a short context label.</summary>
        public static void LogWarning(string context, Exception ex)
            => Write("WARN", context, ex?.Message ?? "", ex);

        /// <summary>Records a non-fatal message (no exception).</summary>
        public static void LogWarning(string context, string message)
            => Write("WARN", context, message, null);

        private static void Write(string level, string context, string message, Exception? ex)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                  .Append(level).Append("  ").Append(context).Append(" — ").Append(message);
                if (ex != null)
                    sb.AppendLine().Append("    ").Append(ex.GetType().Name)
                      .Append(": ").Append(ex.ToString());
                sb.AppendLine();

                lock (_gate)
                    File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Diagnostics must never throw — if even logging fails, stay silent.
            }
        }
    }
}
