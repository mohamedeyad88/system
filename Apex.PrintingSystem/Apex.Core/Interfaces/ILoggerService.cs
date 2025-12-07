using System;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public interface ILoggerService
    {
        void Log(LogLevel level, string message, string? component = null, string? method = null, Exception? ex = null, object? data = null);
        void LogPerformance(string operation, TimeSpan duration, long memoryUsedBytes);
        void LogPrintJob(string printerName, string jobName, string status, string details);
        Task CleanOldLogsAsync(int daysToKeep = 30);
        string GetTodayLogPath();
    }
}
