using Apex.Core.Enums;
using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apex.Services
{
    /// <summary>
    /// Service for archiving completed/failed jobs and managing job history.
    /// </summary>
    public class JobArchiveService
    {
        private readonly IRepository<PrintJob> _jobRepository;
        private readonly IRepository<ArchivedPrintJob> _archiveRepository;

        public JobArchiveService(
            IRepository<PrintJob> jobRepository,
            IRepository<ArchivedPrintJob> archiveRepository)
        {
            _jobRepository = jobRepository;
            _archiveRepository = archiveRepository;
        }

        /// <summary>
        /// Archives a single completed job and removes it from the active queue.
        /// </summary>
        public async Task<ArchivedPrintJob?> ArchiveJobAsync(int jobId, string? notes = null)
        {
            var job = await _jobRepository.GetByIdAsync(jobId);
            if (job == null) return null;

            // Only archive terminal status jobs
            if (job.Status != PrintJobStatus.Completed &&
                job.Status != PrintJobStatus.Failed &&
                job.Status != PrintJobStatus.Error &&
                job.Status != PrintJobStatus.Cancelled)
            {
                return null;
            }

            var archived = CreateArchivedJob(job, notes);
            await _archiveRepository.AddAsync(archived);
            await _jobRepository.DeleteAsync(job);

            return archived;
        }

        /// <summary>
        /// Archives all jobs older than the specified number of days.
        /// </summary>
        public async Task<int> ArchiveOldJobsAsync(int daysOld = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
            var allJobs = await _jobRepository.GetAllAsync();
            
            var jobsToArchive = allJobs
                .Where(j => j.FinishedAtUtc.HasValue && 
                            j.FinishedAtUtc < cutoffDate &&
                            (j.Status == PrintJobStatus.Completed || 
                             j.Status == PrintJobStatus.Failed ||
                             j.Status == PrintJobStatus.Error ||
                             j.Status == PrintJobStatus.Cancelled))
                .ToList();

            foreach (var job in jobsToArchive)
            {
                await ArchiveJobAsync(job.Id, $"Auto-archived: older than {daysOld} days");
            }

            return jobsToArchive.Count;
        }

        /// <summary>
        /// Archives all completed jobs.
        /// </summary>
        public async Task<int> ArchiveCompletedJobsAsync()
        {
            var allJobs = await _jobRepository.GetAllAsync();
            var completedJobs = allJobs.Where(j => j.Status == PrintJobStatus.Completed).ToList();

            foreach (var job in completedJobs)
            {
                await ArchiveJobAsync(job.Id, "Batch archive: completed");
            }

            return completedJobs.Count;
        }

        /// <summary>
        /// Gets archived jobs with optional filtering.
        /// </summary>
        public async Task<IEnumerable<ArchivedPrintJob>> GetArchivedJobsAsync(
            DateTime? fromDate = null, 
            DateTime? toDate = null,
            string? printerName = null)
        {
            var archived = await _archiveRepository.GetAllAsync();
            
            if (fromDate.HasValue)
                archived = archived.Where(a => a.ArchivedAtUtc >= fromDate.Value);
            
            if (toDate.HasValue)
                archived = archived.Where(a => a.ArchivedAtUtc <= toDate.Value);
            
            if (!string.IsNullOrEmpty(printerName))
                archived = archived.Where(a => a.TargetPrinterName == printerName);

            return archived.OrderByDescending(a => a.ArchivedAtUtc);
        }

        /// <summary>
        /// Gets archive statistics.
        /// </summary>
        public async Task<ArchiveStatistics> GetArchiveStatisticsAsync()
        {
            var archived = await _archiveRepository.GetAllAsync();
            var list = archived.ToList();

            return new ArchiveStatistics
            {
                TotalArchived = list.Count,
                CompletedCount = list.Count(a => a.FinalStatus == "Completed"),
                FailedCount = list.Count(a => a.FinalStatus == "Failed" || a.FinalStatus == "Error"),
                CancelledCount = list.Count(a => a.FinalStatus == "Cancelled"),
                TotalPagesProcessed = list.Sum(a => a.Pages ?? 0),
                AverageDurationSeconds = list.Where(a => a.DurationSeconds.HasValue).Average(a => a.DurationSeconds) ?? 0,
                OldestArchiveDate = list.Min(a => (DateTime?)a.ArchivedAtUtc),
                NewestArchiveDate = list.Max(a => (DateTime?)a.ArchivedAtUtc)
            };
        }

        /// <summary>
        /// Permanently deletes archived jobs older than the specified days.
        /// </summary>
        public async Task<int> PurgeOldArchivesAsync(int daysOld = 365)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
            var archived = await _archiveRepository.GetAllAsync();
            var toDelete = archived.Where(a => a.ArchivedAtUtc < cutoffDate).ToList();

            foreach (var archive in toDelete)
            {
                await _archiveRepository.DeleteAsync(archive);
            }

            return toDelete.Count;
        }

        private ArchivedPrintJob CreateArchivedJob(PrintJob job, string? notes)
        {
            double? duration = null;
            if (job.StartedAtUtc.HasValue && job.FinishedAtUtc.HasValue)
            {
                duration = (job.FinishedAtUtc.Value - job.StartedAtUtc.Value).TotalSeconds;
            }

            return new ArchivedPrintJob
            {
                OriginalJobId = job.Id,
                JobGuid = job.JobGuid,
                FilePath = job.FilePath,
                OriginalFileName = job.OriginalFileName,
                TargetPrinterName = job.TargetPrinterName,
                TargetPoolName = job.TargetPoolName,
                FinalStatus = job.Status.ToString(),
                Pages = job.Pages,
                TotalCopies = job.TotalCopies,
                Attempts = job.Attempts,
                ErrorMessage = job.ErrorMessage,
                CreatedAtUtc = job.CreatedAtUtc,
                StartedAtUtc = job.StartedAtUtc,
                FinishedAtUtc = job.FinishedAtUtc,
                ArchivedAtUtc = DateTime.UtcNow,
                DurationSeconds = duration,
                ArchiveNotes = notes
            };
        }
    }

    /// <summary>
    /// Statistics about the job archive.
    /// </summary>
    public class ArchiveStatistics
    {
        public int TotalArchived { get; set; }
        public int CompletedCount { get; set; }
        public int FailedCount { get; set; }
        public int CancelledCount { get; set; }
        public int TotalPagesProcessed { get; set; }
        public double AverageDurationSeconds { get; set; }
        public DateTime? OldestArchiveDate { get; set; }
        public DateTime? NewestArchiveDate { get; set; }
    }
}
