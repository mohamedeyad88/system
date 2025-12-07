using Apex.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Maintenance
{
    public class TempFileCleanupService : IHostedService, IDisposable
    {
        private readonly ILoggerService _logger;
        private Timer? _timer;
        private readonly string _tempPath;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);
        private readonly TimeSpan _fileAgeLimit = TimeSpan.FromHours(24); // Keep files for 24h for safety

        public TempFileCleanupService(ILoggerService logger)
        {
            _logger = logger;
            _tempPath = Path.Combine(Path.GetTempPath(), "ApexConversion");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Log(LogLevel.Info, "Temp File Cleanup Service starting.", "TempFileCleanupService", "StartAsync");
            
            // Run cleanup immediately on startup, then periodically
            _timer = new Timer(DoCleanup, null, TimeSpan.Zero, _cleanupInterval);
            
            return Task.CompletedTask;
        }

        private void DoCleanup(object? state)
        {
            try
            {
                if (!Directory.Exists(_tempPath)) return;

                var cutoffTime = DateTime.Now - _fileAgeLimit;
                var files = Directory.GetFiles(_tempPath);
                int deletedCount = 0;

                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.CreationTime < cutoffTime)
                        {
                            fi.Delete();
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        // File might be in use, ignore
                        _logger.Log(LogLevel.Info, $"Could not delete temp file {file}: {ex.Message}", "TempFileCleanupService", "DoCleanup");
                    }
                }

                if (deletedCount > 0)
                {
                    _logger.Log(LogLevel.Info, $"Cleaned up {deletedCount} old temp files.", "TempFileCleanupService", "DoCleanup");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "Error during temp cleanup", "TempFileCleanupService", "DoCleanup", ex);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
