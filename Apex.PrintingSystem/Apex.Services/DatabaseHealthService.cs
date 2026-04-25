using Apex.Core.Interfaces;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class DatabaseHealthService : IDatabaseHealthService
    {
        private readonly ILoggerService _logger;
        private readonly string _dbPath;
        private readonly string _backupFolder;

        public DatabaseHealthService(ILoggerService logger)
        {
            _logger = logger;
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var dbDirectory = Path.Combine(appDataPath, "ApexPrintingSystem", "Database");
            Directory.CreateDirectory(dbDirectory);
            _dbPath = Path.Combine(dbDirectory, "apex.db");
            _backupFolder = Path.Combine(appDataPath, "ApexPrintingSystem", "Backups");
            Directory.CreateDirectory(_backupFolder);
        }

        public async Task<bool> CheckHealthAsync()
        {
            try
            {
                if (!File.Exists(_dbPath)) return false;
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "Database health check failed", "DatabaseHealthService", "CheckDatabaseHealthAsync", ex);
                return false;
            }
        }

        public async Task RepairDatabaseAsync()
        {
            _logger.Log(LogLevel.Warning, "Attempting Database Repair...", "DatabaseHealthService", "RepairDatabaseAsync");
            var dir = Path.GetDirectoryName(_dbPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
            await Task.CompletedTask;
        }

        public async Task BackupDatabaseAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(_backupFolder))
                        Directory.CreateDirectory(_backupFolder);

                    // Weekly Backup Check
                    var lastBackup = Directory.GetFiles(_backupFolder, "apex_backup_*.db")
                                              .Select(f => new FileInfo(f))
                                              .OrderByDescending(f => f.CreationTime)
                                              .FirstOrDefault();

                    if (lastBackup != null && (DateTime.Now - lastBackup.CreationTime).TotalDays < 7)
                    {
                        return; // Backup is fresh enough
                    }

                    if (File.Exists(_dbPath))
                    {
                        var backupPath = Path.Combine(_backupFolder, $"apex_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                        File.Copy(_dbPath, backupPath, true);
                        _logger.Log(LogLevel.Info, $"Database backup created: {backupPath}", "DatabaseHealthService", "BackupDatabaseAsync");

                        // Prune old backups (keep last 5)
                        var backups = Directory.GetFiles(_backupFolder, "apex_backup_*.db")
                                               .Select(f => new FileInfo(f))
                                               .OrderByDescending(f => f.CreationTime)
                                               .ToList();

                        if (backups.Count > 5)
                        {
                            foreach (var oldBackup in backups.Skip(5))
                            {
                                oldBackup.Delete();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, "Database backup failed", "DatabaseHealthService", "BackupDatabaseAsync", ex);
                }
            });
        }
    }
}
