using System;
using System.IO;

namespace Apex.Services
{
    /// <summary>
    /// Creates timestamped backups of the Apex SQLite database to a user-defined folder.
    /// </summary>
    public class BackupService
    {
        // ── Singleton (stateless — safe to share) ─────────────────────────────
        public static readonly BackupService Instance = new();
        private BackupService() { }

        // ── DB source path ────────────────────────────────────────────────────
        private static readonly string DbFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ApexPrintingSystem", "Database");

        private static readonly string DbFile = Path.Combine(DbFolder, "apex.db");

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Copies <c>apex.db</c> to <paramref name="backupDirectory"/> with a
        /// timestamp suffix.  Returns a <see cref="BackupResult"/> describing the
        /// outcome.
        /// </summary>
        public BackupResult RunBackup(string backupDirectory)
        {
            if (string.IsNullOrWhiteSpace(backupDirectory))
                return BackupResult.Fail("لم يتم تحديد مسار النسخ الاحتياطي.\nافتح الإعدادات واختر مجلدًا أولاً.");

            try
            {
                // Ensure destination folder exists
                Directory.CreateDirectory(backupDirectory);

                if (!File.Exists(DbFile))
                    return BackupResult.Fail($"ملف قاعدة البيانات غير موجود:\n{DbFile}");

                // Timestamped file name: apex_backup_2026-05-10_14-30-00.db
                var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var destName = $"apex_backup_{stamp}.db";
                var destPath = Path.Combine(backupDirectory, destName);

                File.Copy(DbFile, destPath, overwrite: false);

                // Also persist a "last backup" marker file for quick verification
                File.WriteAllText(
                    Path.Combine(backupDirectory, "last_backup.txt"),
                    $"Backup created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nFile: {destName}\nSize: {new FileInfo(destPath).Length / 1024.0:F1} KB");

                return BackupResult.Ok(destPath);
            }
            catch (Exception ex)
            {
                return BackupResult.Fail($"فشل النسخ الاحتياطي:\n{ex.Message}");
            }
        }

        /// <summary>Returns the size in KB of the live database file, or -1 if missing.</summary>
        public double GetDatabaseSizeKb()
        {
            try { return File.Exists(DbFile) ? new FileInfo(DbFile).Length / 1024.0 : -1; }
            catch { return -1; }
        }
    }

    public sealed class BackupResult
    {
        public bool Success { get; private init; }
        public string Message { get; private init; } = "";
        public string? DestPath { get; private init; }

        public static BackupResult Ok(string destPath) => new()
        {
            Success = true,
            Message = $"✅ تم إنشاء النسخة الاحتياطية بنجاح:\n{Path.GetFileName(destPath)}",
            DestPath = destPath
        };

        public static BackupResult Fail(string reason) => new()
        {
            Success = false,
            Message = reason
        };
    }
}
