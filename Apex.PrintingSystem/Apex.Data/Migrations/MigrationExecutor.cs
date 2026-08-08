using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Apex.Data.Migrations
{
    public class MigrationExecutor
    {
        private readonly ApexDbContext _context;
        private readonly SchemaVersionRepository _versionRepo;
        private const string LogFile = "migration.log";

        public MigrationExecutor(ApexDbContext context, SchemaVersionRepository versionRepo)
        {
            _context = context;
            _versionRepo = versionRepo;
        }

        public async Task ExecuteAsync(List<MigrationStep> steps)
        {
            if (steps.Count == 0) return;

            var currentVersion = await _versionRepo.GetLatestVersionAsync();
            var nextVersion = currentVersion + 1;

            Log($"[{DateTime.Now}] Starting Migration v{nextVersion} with {steps.Count} steps.");

            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    foreach (var step in steps)
                    {
                        Log($"[{DateTime.Now}] Executing: {step.Description}");
                        Log($"SQL: {step.Sql}");

                        await _context.Database.ExecuteSqlRawAsync(step.Sql);
                    }

                    await _versionRepo.LogMigrationAsync(nextVersion, $"Auto-migration with {steps.Count} steps", true);
                    await transaction.CommitAsync();

                    Log($"[{DateTime.Now}] Migration v{nextVersion} completed successfully.");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Log($"[{DateTime.Now}] Migration v{nextVersion} FAILED: {ex.Message}");
                    await _versionRepo.LogMigrationAsync(nextVersion, "Failed migration", false, ex.Message);
                    throw;
                }
            }
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile, message + Environment.NewLine);
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }
}
