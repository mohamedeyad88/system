using Apex.Core.Enums;
using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace Apex.Services
{
    public class JobDistributionService : IJobDistributionService
    {
        private readonly IRepository<PrintJob> _jobRepository;
        private readonly IPrinterService _printerService;
        private readonly IRoutingRulesEngine _routingRulesEngine;
        private readonly ILoadBalancer _loadBalancer;
        private readonly PrinterMonitoringService _monitoringService;

        public JobDistributionService(
            IRepository<PrintJob> jobRepository,
            IPrinterService printerService,
            IRoutingRulesEngine routingRulesEngine,
            ILoadBalancer loadBalancer,
            PrinterMonitoringService monitoringService)
        {
            _jobRepository = jobRepository;
            _printerService = printerService;
            _routingRulesEngine = routingRulesEngine;
            _loadBalancer = loadBalancer;
            _monitoringService = monitoringService;
        }

        public async Task DistributeJobAsync(PrintJob job)
        {
            if (job.Id == 0)
            {
                await _jobRepository.AddAsync(job);
            }

            // 1. Evaluate Routing Rules
            var rule = await _routingRulesEngine.EvaluateRulesAsync(job);
            string? targetPrinter = null;

            if (rule != null)
            {
                if (!string.IsNullOrEmpty(rule.TargetPrinter))
                {
                    targetPrinter = rule.TargetPrinter;
                }
                else if (!string.IsNullOrEmpty(rule.TargetPool))
                {
                    // 2. Load Balancing for Pool
                    var allPrinters = await _printerService.GetAllPrintersAsync();
                    var poolPrinters = allPrinters.Where(p => p.PoolName == rule.TargetPool).ToList();

                    if (poolPrinters.Any())
                    {
                        // Map to PrinterInfo with real-time status
                        var candidates = poolPrinters.Select(p =>
                        {
                            var info = new PrinterInfo
                            {
                                Name = p.Name,
                                PoolName = p.PoolName ?? "",
                                IsOnline = true, // Default
                                QueueLength = 0
                            };

                            var status = _monitoringService.GetCurrentStatus(p.Name);
                            if (status != null)
                            {
                                info.IsOnline = !status.IsOffline;
                                info.QueueLength = status.QueueLength;
                                info.HasPaperJam = status.Condition == PrinterCondition.PaperJam;
                                info.IsOutOfPaper = status.Condition == PrinterCondition.OutOfPaper;
                                info.IsTonerLow = status.Condition is PrinterCondition.LowToner
                                                                   or PrinterCondition.OutOfToner;
                            }
                            return info;
                        });

                        var bestPrinter = _loadBalancer.SelectPrinterForJob(job, candidates);
                        if (bestPrinter != null)
                        {
                            targetPrinter = bestPrinter.Name;
                        }
                    }
                }
            }

            // Fallback: If no rule or no printer found, pick first active online printer
            if (string.IsNullOrEmpty(targetPrinter))
            {
                var allPrinters = await _printerService.GetAllPrintersAsync();
                var fallback = allPrinters.FirstOrDefault(p => p.IsActive); // Could be improved to check online status
                if (fallback != null) targetPrinter = fallback.Name;
            }

            if (!string.IsNullOrEmpty(targetPrinter))
            {
                job.TargetPrinterName = targetPrinter;
                job.Status = PrintJobStatus.Pending; // Ready for processing
                await _jobRepository.UpdateAsync(job);

                // Trigger processing
                await ProcessJobAsync(job.Id);
            }
            else
            {
                job.Status = PrintJobStatus.Error;
                job.ErrorMessage = "No suitable printer found.";
                await _jobRepository.UpdateAsync(job);
            }
        }

        private const int MaxRetryAttempts = 3;
        private static readonly TimeSpan[] RetryDelays = {
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30)
        };

        public async Task ProcessJobAsync(int jobId)
        {
            var job = await _jobRepository.GetByIdAsync(jobId);
            if (job == null || string.IsNullOrEmpty(job.TargetPrinterName)) return;

            job.Status = PrintJobStatus.Printing;
            job.StartedAtUtc = DateTime.UtcNow;
            await _jobRepository.UpdateAsync(job);

            bool success = false;
            string? lastError = null;

            while (job.Attempts < MaxRetryAttempts && !success)
            {
                job.Attempts++;

                try
                {
                    // Use the overload that accepts the full job with settings
                    success = await _printerService.PrintFileAsync(job.TargetPrinterName, job.FilePath, job);

                    if (!success)
                    {
                        lastError = "Printing failed.";
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    success = false;
                }

                if (!success && job.Attempts < MaxRetryAttempts)
                {
                    // Wait before retry (exponential backoff)
                    var delay = RetryDelays[Math.Min(job.Attempts - 1, RetryDelays.Length - 1)];
                    await Task.Delay(delay);
                }
            }

            if (success)
            {
                job.Status = PrintJobStatus.Completed;
                job.FinishedAtUtc = DateTime.UtcNow;
                job.ErrorMessage = null;
            }
            else
            {
                job.Status = PrintJobStatus.Error;
                job.ErrorMessage = $"Failed after {job.Attempts} attempts. Last error: {lastError}";
            }

            await _jobRepository.UpdateAsync(job);
        }

        /// <summary>
        /// Retries a specific failed job, resetting its attempt count.
        /// </summary>
        public async Task RetryFailedJobAsync(int jobId)
        {
            var job = await _jobRepository.GetByIdAsync(jobId);
            if (job == null || job.Status != PrintJobStatus.Error) return;

            job.Status = PrintJobStatus.Pending;
            job.Attempts = 0;
            job.ErrorMessage = null;
            await _jobRepository.UpdateAsync(job);

            await DistributeJobAsync(job);
        }

        /// <summary>
        /// Retries all failed jobs.
        /// </summary>
        public async Task RetryAllFailedJobsAsync()
        {
            var allJobs = await _jobRepository.GetAllAsync();
            var failedJobs = allJobs.Where(j => j.Status == PrintJobStatus.Error);

            foreach (var job in failedJobs)
            {
                await RetryFailedJobAsync(job.Id);
            }
        }

        public PrintJob CreatePrintJob(string filePath, int copies, JobPriority priority = JobPriority.Normal)
        {
            return new PrintJob
            {
                FilePath = filePath,
                OriginalFileName = System.IO.Path.GetFileName(filePath),
                TotalCopies = copies,
                Priority = priority,
                Status = PrintJobStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Gets the next pending job ordered by priority (highest first), then by creation time.
        /// </summary>
        public async Task<PrintJob?> GetNextPendingJobAsync()
        {
            var allJobs = await _jobRepository.GetAllAsync();
            return allJobs
                .Where(j => j.Status == PrintJobStatus.Pending && !string.IsNullOrEmpty(j.TargetPrinterName))
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.CreatedAtUtc)
                .FirstOrDefault();
        }

        /// <summary>
        /// Gets all pending jobs ordered by priority.
        /// </summary>
        public async Task<IEnumerable<PrintJob>> GetPendingJobsOrderedByPriorityAsync()
        {
            var allJobs = await _jobRepository.GetAllAsync();
            return allJobs
                .Where(j => j.Status == PrintJobStatus.Pending)
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.CreatedAtUtc);
        }

        public async Task UpdateJobStatus(int jobId, JobAssignmentStatus status)
        {
            // Legacy support
            var job = await _jobRepository.GetByIdAsync(jobId);
            if (job != null)
            {
                if (status == JobAssignmentStatus.Completed) job.Status = PrintJobStatus.Completed;
                else if (status == JobAssignmentStatus.Printing) job.Status = PrintJobStatus.Printing;
                else if (status == JobAssignmentStatus.Error) job.Status = PrintJobStatus.Error;
                await _jobRepository.UpdateAsync(job);
            }
        }

        public void AssignJobsToPrinters() { }
        public void DistributeJobs() { }

        /// <summary>
        /// Cancels a specific pending or printing job.
        /// </summary>
        public async Task<bool> CancelJobAsync(int jobId)
        {
            var job = await _jobRepository.GetByIdAsync(jobId);
            if (job == null) return false;

            // Can only cancel pending or printing jobs
            if (job.Status != PrintJobStatus.Pending && job.Status != PrintJobStatus.Printing)
                return false;

            job.Status = PrintJobStatus.Cancelled;
            job.FinishedAtUtc = DateTime.UtcNow;
            job.ErrorMessage = "Job cancelled by user.";
            await _jobRepository.UpdateAsync(job);

            return true;
        }

        /// <summary>
        /// Cancels all pending jobs.
        /// </summary>
        public async Task<int> CancelAllPendingJobsAsync()
        {
            var allJobs = await _jobRepository.GetAllAsync();
            var pendingJobs = allJobs.Where(j => j.Status == PrintJobStatus.Pending).ToList();

            foreach (var job in pendingJobs)
            {
                job.Status = PrintJobStatus.Cancelled;
                job.FinishedAtUtc = DateTime.UtcNow;
                job.ErrorMessage = "Job cancelled (bulk cancellation).";
                await _jobRepository.UpdateAsync(job);
            }

            return pendingJobs.Count;
        }

        /// <summary>
        /// Gets the count of jobs in each status.
        /// </summary>
        public async Task<Dictionary<PrintJobStatus, int>> GetJobStatusCountsAsync()
        {
            var allJobs = await _jobRepository.GetAllAsync();
            return allJobs
                .GroupBy(j => j.Status)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Updates the progress of a printing job.
        /// </summary>
        public async Task UpdateJobProgressAsync(int jobId, int currentPage, int totalPages)
        {
            var job = await _jobRepository.GetByIdAsync(jobId);
            if (job == null) return;

            job.CurrentPage = currentPage;
            job.TotalPages = totalPages;
            await _jobRepository.UpdateAsync(job);
        }

        /// <summary>
        /// Gets the progress of a specific job.
        /// </summary>
        public async Task<PrintJobProgress?> GetJobProgressAsync(int jobId)
        {
            var job = await _jobRepository.GetByIdAsync(jobId);
            if (job == null) return null;

            var progress = new PrintJobProgress
            {
                JobId = job.Id,
                CurrentPage = job.CurrentPage,
                TotalPages = job.TotalPages,
                StartedAt = job.StartedAtUtc,
                StatusMessage = job.Status.ToString(),
                IsPaused = job.Status == PrintJobStatus.Paused
            };

            // Calculate pages per second if job has started
            if (job.StartedAtUtc.HasValue && job.CurrentPage > 0)
            {
                var elapsed = DateTime.UtcNow - job.StartedAtUtc.Value;
                progress.PagesPerSecond = job.CurrentPage / Math.Max(elapsed.TotalSeconds, 1);

                // Estimate remaining time
                if (progress.PagesPerSecond > 0 && job.TotalPages > job.CurrentPage)
                {
                    var remainingPages = job.TotalPages - job.CurrentPage;
                    progress.EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingPages / progress.PagesPerSecond);
                }
            }

            return progress;
        }

        /// <summary>
        /// Gets progress for all currently printing jobs.
        /// </summary>
        public async Task<IEnumerable<PrintJobProgress>> GetAllPrintingJobsProgressAsync()
        {
            var allJobs = await _jobRepository.GetAllAsync();
            var printingJobs = allJobs.Where(j => j.Status == PrintJobStatus.Printing);

            var progressList = new List<PrintJobProgress>();
            foreach (var job in printingJobs)
            {
                var progress = await GetJobProgressAsync(job.Id);
                if (progress != null) progressList.Add(progress);
            }

            return progressList;
        }

        #region Job Scheduling

        /// <summary>
        /// Schedules a job to start at a specific time.
        /// </summary>
        public async Task<bool> ScheduleJobAsync(int jobId, DateTime scheduledStartUtc)
        {
            var job = await _jobRepository.GetByIdAsync(jobId);
            if (job == null || job.Status != PrintJobStatus.Pending) return false;

            job.ScheduledStartUtc = scheduledStartUtc;
            await _jobRepository.UpdateAsync(job);
            return true;
        }

        /// <summary>
        /// Creates a new job scheduled for a specific time.
        /// </summary>
        public PrintJob CreateScheduledJob(string filePath, int copies, DateTime scheduledStartUtc, JobPriority priority = JobPriority.Normal)
        {
            return new PrintJob
            {
                FilePath = filePath,
                OriginalFileName = System.IO.Path.GetFileName(filePath),
                TotalCopies = copies,
                Priority = priority,
                ScheduledStartUtc = scheduledStartUtc,
                Status = PrintJobStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Gets all scheduled jobs that are due to run.
        /// </summary>
        public async Task<IEnumerable<PrintJob>> GetDueScheduledJobsAsync()
        {
            var allJobs = await _jobRepository.GetAllAsync();
            return allJobs
                .Where(j => j.Status == PrintJobStatus.Pending && j.IsReadyToProcess)
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.ScheduledStartUtc ?? j.CreatedAtUtc);
        }

        /// <summary>
        /// Gets all jobs scheduled for the future.
        /// </summary>
        public async Task<IEnumerable<PrintJob>> GetUpcomingScheduledJobsAsync()
        {
            var allJobs = await _jobRepository.GetAllAsync();
            return allJobs
                .Where(j => j.Status == PrintJobStatus.Pending && j.IsScheduled)
                .OrderBy(j => j.ScheduledStartUtc);
        }

        /// <summary>
        /// Removes the schedule from a job, making it run immediately.
        /// </summary>
        public async Task<bool> UnscheduleJobAsync(int jobId)
        {
            var job = await _jobRepository.GetByIdAsync(jobId);
            if (job == null) return false;

            job.ScheduledStartUtc = null;
            await _jobRepository.UpdateAsync(job);
            return true;
        }

        #endregion

        #region Job Duplication

        /// <summary>
        /// Duplicates an existing job with a new ID and optional modifications.
        /// </summary>
        public async Task<PrintJob?> DuplicateJobAsync(int sourceJobId, int? newCopies = null, JobPriority? newPriority = null)
        {
            var sourceJob = await _jobRepository.GetByIdAsync(sourceJobId);
            if (sourceJob == null) return null;

            var duplicatedJob = new PrintJob
            {
                JobGuid = Guid.NewGuid().ToString(),
                FilePath = sourceJob.FilePath,
                OriginalFileName = sourceJob.OriginalFileName,
                TargetPrinterName = sourceJob.TargetPrinterName,
                TargetPoolName = sourceJob.TargetPoolName,
                Mode = sourceJob.Mode,
                TotalCopies = newCopies ?? sourceJob.TotalCopies,
                Priority = newPriority ?? sourceJob.Priority,
                OptionsJson = sourceJob.OptionsJson,
                Pages = sourceJob.Pages,
                Status = PrintJobStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow,
                Attempts = 0
            };

            await _jobRepository.AddAsync(duplicatedJob);
            return duplicatedJob;
        }

        /// <summary>
        /// Duplicates a job multiple times.
        /// </summary>
        public async Task<IEnumerable<PrintJob>> DuplicateJobMultipleAsync(int sourceJobId, int count)
        {
            var duplicates = new List<PrintJob>();
            for (int i = 0; i < count; i++)
            {
                var duplicate = await DuplicateJobAsync(sourceJobId);
                if (duplicate != null) duplicates.Add(duplicate);
            }
            return duplicates;
        }

        /// <summary>
        /// Creates a copy of a completed or failed job to retry it.
        /// </summary>
        public async Task<PrintJob?> ResubmitJobAsync(int sourceJobId)
        {
            var sourceJob = await _jobRepository.GetByIdAsync(sourceJobId);
            if (sourceJob == null) return null;

            // Only allow resubmitting completed, failed, or cancelled jobs
            if (sourceJob.Status != PrintJobStatus.Completed &&
                sourceJob.Status != PrintJobStatus.Failed &&
                sourceJob.Status != PrintJobStatus.Error &&
                sourceJob.Status != PrintJobStatus.Cancelled)
            {
                return null;
            }

            return await DuplicateJobAsync(sourceJobId);
        }

        #endregion
    }
}
