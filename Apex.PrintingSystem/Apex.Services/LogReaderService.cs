using Apex.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class LogReaderService : ILogReaderService
    {
        private readonly string _logDirectory;

        public LogReaderService()
        {
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        }

        public Task<string> GetTodayLogPathAsync()
        {
            return Task.FromResult(Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.log"));
        }

        public Task<IEnumerable<string>> GetLogFilesAsync()
        {
            if (!Directory.Exists(_logDirectory))
                return Task.FromResult(Enumerable.Empty<string>());

            return Task.FromResult(Directory.GetFiles(_logDirectory, "*.log").AsEnumerable());
        }

        public async Task<string> ReadLogFileAsync(string logPath)
        {
            if (!File.Exists(logPath)) return string.Empty;

            try
            {
                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }
            catch
            {
                return "Error reading log file.";
            }
        }
    }
}
