using Apex.Core.Interfaces;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class LogMaintenanceService
    {
        private readonly ILoggerService _logger;
        private readonly string _logDirectory;

        public LogMaintenanceService(ILoggerService logger)
        {
            _logger = logger;
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        }

        public async Task PerformMaintenanceAsync(int retentionDays = 90)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(_logDirectory)) return;

                    var cutoff = DateTime.Now.AddDays(-retentionDays);
                    var files = Directory.GetFiles(_logDirectory, "*.log");

                    foreach (var file in files)
                    {
                        var fi = new FileInfo(file);
                        if (fi.CreationTime < cutoff)
                        {
                            fi.Delete();
                            _logger.Log(LogLevel.Info, $"Deleted old log file: {fi.Name}", "LogMaintenanceService", "PerformMaintenanceAsync");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, "Log maintenance failed", "LogMaintenanceService", "PerformMaintenanceAsync", ex);
                }
            });
        }
    }
}
