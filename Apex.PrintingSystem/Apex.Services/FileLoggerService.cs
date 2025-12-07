using Apex.Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class FileLoggerService : ILoggerService, IDisposable
    {
        private readonly string _logDirectory;
        private readonly BlockingCollection<LogEntry> _logQueue;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processingTask;
        private const int MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

        public FileLoggerService()
        {
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            _logQueue = new BlockingCollection<LogEntry>();
            _cancellationTokenSource = new CancellationTokenSource();
            _processingTask = Task.Run(ProcessLogQueue);
        }

        public void Log(LogLevel level, string message, string? component = null, string? method = null, Exception? ex = null, object? data = null)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Component = component ?? new StackTrace().GetFrame(1)?.GetMethod()?.DeclaringType?.Name,
                Method = method ?? new StackTrace().GetFrame(1)?.GetMethod()?.Name,
                Exception = ex?.ToString(),
                Data = data,
                Suggestion = GenerateSuggestion(message, ex),
                CorrelationId = System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString()
            };

            _logQueue.Add(entry);
        }

        public void LogPerformance(string operation, TimeSpan duration, long memoryUsedBytes)
        {
            Log(LogLevel.Info, $"Performance: {operation} took {duration.TotalMilliseconds}ms. Memory Delta: {memoryUsedBytes / 1024.0 / 1024.0:F2} MB", "Performance", null, null, new { DurationMs = duration.TotalMilliseconds, MemoryBytes = memoryUsedBytes });
        }

        public void LogPrintJob(string printerName, string jobName, string status, string details)
        {
            Log(LogLevel.Info, $"PrintJob: {jobName} on {printerName} - {status}. {details}", "PrintJob", null, null, new { Printer = printerName, Job = jobName, Status = status });
        }

        public async Task CleanOldLogsAsync(int daysToKeep = 90)
        {
            // Handled by LogMaintenanceService now, but kept for compatibility
            await Task.CompletedTask;
        }

        public string GetTodayLogPath()
        {
            return Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
        }

        private async void ProcessLogQueue()
        {
            foreach (var entry in _logQueue.GetConsumingEnumerable(_cancellationTokenSource.Token))
            {
                await WriteLogEntryAsync(entry);
            }
        }

        private async Task WriteLogEntryAsync(LogEntry entry)
        {
            const int maxRetries = 3;
            int retryCount = 0;
            bool success = false;

            while (!success && retryCount < maxRetries)
            {
                try
                {
                    var logPath = GetTodayLogPath();
                    
                    // Check file size and rotate if needed
                    if (File.Exists(logPath) && new FileInfo(logPath).Length > MaxFileSizeBytes)
                    {
                        var newPath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}_{DateTime.Now:HHmmss}.log");
                        File.Move(logPath, newPath);
                    }

                    var json = JsonSerializer.Serialize(entry);
                    // Simple Obfuscation (Base64) - In production use AES
                    var encryptedJson = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
                    
                    var line = $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] [{entry.CorrelationId}] {entry.Message}";
                    if (entry.Exception != null) line += $" [Ex: {entry.Exception.GetType().Name}]";

                    await File.AppendAllTextAsync(logPath, line + Environment.NewLine);
                    success = true;
                }
                catch (IOException)
                {
                    retryCount++;
                    await Task.Delay(100 * (int)Math.Pow(2, retryCount)); // Exponential backoff
                }
                catch
                {
                    break; // Non-IO errors, abort
                }
            }
        }

        private string? GenerateSuggestion(string message, Exception? ex)
        {
            if (message.Contains("Printer did not respond") || (ex != null && ex.Message.Contains("RPC server is unavailable")))
                return "Check printer power and network connection.";
            if (message.Contains("Template file missing") || (ex is FileNotFoundException))
                return "Verify the template path in settings.";
            if (ex is OutOfMemoryException)
                return "Restart application or reduce image quality.";
            return null;
        }

        #region Log Export

        /// <summary>
        /// Exports logs to CSV format.
        /// </summary>
        public async Task<string> ExportLogsAsCsvAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var logs = await ReadLogsAsync(startDate, endDate);
            var exportPath = Path.Combine(_logDirectory, $"export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            using (var writer = new StreamWriter(exportPath))
            {
                // Header
                await writer.WriteLineAsync("Timestamp,Level,Component,Method,Message");

                // Data
                foreach (var log in logs)
                {
                    var line = $"{log.Timestamp:yyyy-MM-dd HH:mm:ss},{log.Level},{EscapeCsv(log.Component)},{EscapeCsv(log.Method)},{EscapeCsv(log.Message)}";
                    await writer.WriteLineAsync(line);
                }
            }

            return exportPath;
        }

        /// <summary>
        /// Exports logs to JSON format.
        /// </summary>
        public async Task<string> ExportLogsAsJsonAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var logs = await ReadLogsAsync(startDate, endDate);
            var exportPath = Path.Combine(_logDirectory, $"export_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(exportPath, json);

            return exportPath;
        }

        /// <summary>
        /// Exports logs to plain text format.
        /// </summary>
        public async Task<string> ExportLogsAsTextAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var logs = await ReadLogsAsync(startDate, endDate);
            var exportPath = Path.Combine(_logDirectory, $"export_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            using (var writer = new StreamWriter(exportPath))
            {
                await writer.WriteLineAsync($"=== Log Export Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                await writer.WriteLineAsync($"Total Entries: {logs.Count}");
                await writer.WriteLineAsync(new string('=', 80));
                await writer.WriteLineAsync();

                foreach (var log in logs)
                {
                    await writer.WriteLineAsync($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Level}]");
                    await writer.WriteLineAsync($"Component: {log.Component} | Method: {log.Method}");
                    await writer.WriteLineAsync($"Message: {log.Message}");
                    if (!string.IsNullOrEmpty(log.Exception))
                    {
                        await writer.WriteLineAsync($"Exception: {log.Exception}");
                    }
                    await writer.WriteLineAsync(new string('-', 80));
                }
            }

            return exportPath;
        }

        private async Task<List<LogEntry>> ReadLogsAsync(DateTime? startDate, DateTime? endDate)
        {
            var logs = new List<LogEntry>();
            var start = startDate ?? DateTime.Now.AddDays(-7);
            var end = endDate ?? DateTime.Now;

            // Read log files for date range
            for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
            {
                var logPath = Path.Combine(_logDirectory, $"{date:yyyy-MM-dd}.log");
                if (File.Exists(logPath))
                {
                    var lines = await File.ReadAllLinesAsync(logPath);
                    foreach (var line in lines)
                    {
                        // Parse log line (simplified)
                        if (TryParseLogLine(line, out var entry))
                        {
                            logs.Add(entry);
                        }
                    }
                }
            }

            return logs;
        }

        private bool TryParseLogLine(string line, out LogEntry entry)
        {
            entry = new LogEntry();
            
            try
            {
                // Simple parsing - [HH:mm:ss] [Level] [CorrelationId] Message
                var parts = line.Split(new[] { "] [" }, StringSplitOptions.None);
                if (parts.Length >= 3)
                {
                    entry.Timestamp = DateTime.Parse(parts[0].TrimStart('['));
                    entry.Level = Enum.Parse<LogLevel>(parts[1]);
                    entry.CorrelationId = parts[2];
                    entry.Message = parts.Length > 3 ? string.Join("] [", parts.Skip(3)).TrimEnd(']') : "";
                    return true;
                }
            }
            catch { }

            return false;
        }

        private string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        #endregion

        public void Dispose()
        {
            _logQueue.CompleteAdding();
            _cancellationTokenSource.Cancel();
            try { _processingTask.Wait(1000); } catch { }
            _cancellationTokenSource.Dispose();
            _logQueue.Dispose();
        }

        private class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; } = "";
            public string? Component { get; set; }
            public string? Method { get; set; }
            public string? Exception { get; set; }
            public object? Data { get; set; }
            public string? Suggestion { get; set; }
            public string? CorrelationId { get; set; }
        }
    }
}
