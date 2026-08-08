using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing.Queue
{
    /// <summary>
    /// Centralized print queue system - THE BRAIN of the print server.
    /// 
    /// ARCHITECTURE:
    /// - Each printer has its own isolated queue (FIFO per printer)
    /// - Jobs are never sent directly - they ALWAYS go through this queue
    /// - Only ONE active job per printer at any time
    /// - Failed jobs don't block the queue - they're moved to retry/failed state
    /// - Queue persists across UI lifecycle
    /// 
    /// DESIGN PHILOSOPHY:
    /// "The application is the server, not the printers"
    /// </summary>
    public class CentralizedPrintQueue
    {
        // Per-printer queues: Key = printer name, Value = job queue for that printer
        private readonly ConcurrentDictionary<string, ConcurrentQueue<PrintJob>> _printerQueues = new();

        // All jobs indexed by ID for fast lookup
        private readonly ConcurrentDictionary<string, PrintJob> _allJobs = new();

        // Active job per printer (only one!)
        private readonly ConcurrentDictionary<string, PrintJob> _activeJobs = new();

        // Statistics
        private long _totalJobsQueued;
        private long _totalJobsCompleted;
        private long _totalJobsFailed;

        // Singleton
        private static readonly Lazy<CentralizedPrintQueue> _instance =
            new(() => new CentralizedPrintQueue());

        public static CentralizedPrintQueue Instance => _instance.Value;

        private CentralizedPrintQueue()
        {
            Debug.WriteLine("[PrintQueue] ✓ Centralized Print Queue initialized");
        }

        /// <summary>
        /// Event fired when a job state changes.
        /// </summary>
        public event EventHandler<PrintJob>? JobStateChanged;

        /// <summary>
        /// Event fired when a job's progress updates.
        /// </summary>
        public event EventHandler<PrintJob>? JobProgressChanged;

        /// <summary>
        /// Enqueues a new print job.
        /// Job immediately enters Queued state and will be processed when printer is available.
        /// </summary>
        public string EnqueueJob(PrintJob job)
        {
            if (job == null)
                throw new ArgumentNullException(nameof(job));

            if (string.IsNullOrEmpty(job.PrinterName))
                throw new ArgumentException("Job must have a printer name", nameof(job));

            // Add to global job registry
            if (!_allJobs.TryAdd(job.JobId, job))
                throw new InvalidOperationException($"Job {job.JobId} already exists in queue");

            // Get or create printer-specific queue
            var printerQueue = _printerQueues.GetOrAdd(job.PrinterName, _ => new ConcurrentQueue<PrintJob>());

            // Check dependency state; if not satisfied mark WaitingForDependency
            if (!IsDependencySatisfied(job))
            {
                job.State = PrintJobState.WaitingForDependency;
                job.StatusMessage = "Waiting for dependency...";
            }
            else
            {
                job.State = PrintJobState.Queued;
                job.StatusMessage = $"Queued (position: {printerQueue.Count + 1})";
            }

            // Enqueue the job
            printerQueue.Enqueue(job);

            Interlocked.Increment(ref _totalJobsQueued);

            Debug.WriteLine($"[PrintQueue] ✓ Job {job.JobId} ({job.JobName}) queued for printer '{job.PrinterName}' - Queue depth: {printerQueue.Count} - UseRawDocumentMode: {job.UseRawDocumentMode}");

            JobStateChanged?.Invoke(this, job);

            return job.JobId;
        }

        /// <summary>
        /// Gets the next job to execute for a specific printer.
        /// Returns null if no jobs available or printer already has an active job.
        /// </summary>
        public PrintJob? GetNextJob(string printerName)
        {
            if (!_printerQueues.TryGetValue(printerName, out var queue))
                return null;

            // Check if printer already has an active job
            if (_activeJobs.ContainsKey(printerName))
            {
                Debug.WriteLine($"[PrintQueue] Printer '{printerName}' already has an active job - skipping");
                return null;
            }

            // Iterate through queue to find a job whose dependency is satisfied
            var attempts = queue.Count;
            while (attempts-- > 0 && queue.TryDequeue(out var job))
            {
                if (IsDependencySatisfied(job))
                {
                    // Mark as active
                    _activeJobs[printerName] = job;
                    job.State = PrintJobState.Preparing;
                    job.StartedAt = DateTime.UtcNow;
                    job.StatusMessage = "Preparing...";

                    Debug.WriteLine($"[PrintQueue] ▶️ Starting job {job.JobId} on printer '{printerName}' - Remaining in queue: {queue.Count}");

                    JobStateChanged?.Invoke(this, job);

                    return job;
                }
                else
                {
                    // Dependency not ready; put it back at the tail
                    job.State = PrintJobState.WaitingForDependency;
                    job.StatusMessage = "Waiting for dependency...";
                    queue.Enqueue(job);
                }
            }

            return null;
        }

        /// <summary>
        /// Marks a job as completed and removes it from active jobs.
        /// </summary>
        public void CompleteJob(string jobId, bool success, string? errorMessage = null)
        {
            if (!_allJobs.TryGetValue(jobId, out var job))
            {
                Debug.WriteLine($"[PrintQueue] ⚠️ Attempted to complete non-existent job {jobId}");
                return;
            }

            job.CompletedAt = DateTime.UtcNow;

            if (success)
            {
                job.State = PrintJobState.Completed;
                job.StatusMessage = "Completed";
                job.Progress = 100;
                Interlocked.Increment(ref _totalJobsCompleted);

                Debug.WriteLine($"[PrintQueue] ✅ Job {jobId} completed successfully in {job.ElapsedTime?.TotalSeconds:F1}s");
            }
            else
            {
                job.State = PrintJobState.Failed;
                job.ErrorMessage = errorMessage;
                job.StatusMessage = $"Failed: {errorMessage}";
                Interlocked.Increment(ref _totalJobsFailed);

                Debug.WriteLine($"[PrintQueue] ❌ Job {jobId} failed: {errorMessage}");
            }

            // Remove from active jobs
            _activeJobs.TryRemove(job.PrinterName, out _);

            JobStateChanged?.Invoke(this, job);
        }

        /// <summary>
        /// Updates job progress (0-100).
        /// </summary>
        public void UpdateJobProgress(string jobId, int progress, string? statusMessage = null)
        {
            if (!_allJobs.TryGetValue(jobId, out var job))
                return;

            job.Progress = Math.Clamp(progress, 0, 100);

            if (statusMessage != null)
                job.StatusMessage = statusMessage;

            JobProgressChanged?.Invoke(this, job);
        }

        /// <summary>
        /// Updates job state.
        /// </summary>
        public void UpdateJobState(string jobId, PrintJobState newState, string? statusMessage = null)
        {
            if (!_allJobs.TryGetValue(jobId, out var job))
                return;

            var oldState = job.State;
            job.State = newState;

            if (statusMessage != null)
                job.StatusMessage = statusMessage;

            Debug.WriteLine($"[PrintQueue] Job {jobId} state: {oldState} → {newState}");

            JobStateChanged?.Invoke(this, job);
        }

        /// <summary>
        /// Re-queues a failed job for retry.
        /// </summary>
        public bool RetryJob(string jobId)
        {
            if (!_allJobs.TryGetValue(jobId, out var job))
                return false;

            if (!job.CanRetry)
            {
                Debug.WriteLine($"[PrintQueue] Job {jobId} cannot be retried (max retries reached or not in failed state)");
                return false;
            }

            job.RetryCount++;
            job.State = PrintJobState.Queued;
            job.StatusMessage = $"Retrying... (attempt {job.RetryCount}/{job.MaxRetries})";
            job.ErrorMessage = null;
            job.CompletedAt = null;

            // Re-add to queue
            if (_printerQueues.TryGetValue(job.PrinterName, out var queue))
            {
                queue.Enqueue(job);
                Debug.WriteLine($"[PrintQueue] ↻ Job {jobId} re-queued for retry #{job.RetryCount}");

                JobStateChanged?.Invoke(this, job);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Cancels a job (if not yet started).
        /// </summary>
        public bool CancelJob(string jobId)
        {
            if (!_allJobs.TryGetValue(jobId, out var job))
                return false;

            if (job.IsActive)
            {
                Debug.WriteLine($"[PrintQueue] Cannot cancel active job {jobId}");
                return false;
            }

            job.State = PrintJobState.Cancelled;
            job.StatusMessage = "Cancelled by user";
            job.CompletedAt = DateTime.UtcNow;

            // Remove from active if present
            _activeJobs.TryRemove(job.PrinterName, out _);

            Debug.WriteLine($"[PrintQueue] Job {jobId} cancelled");

            JobStateChanged?.Invoke(this, job);

            return true;
        }

        /// <summary>
        /// Skips a job (marks as logically completed for dependents).
        /// </summary>
        public bool SkipJob(string jobId, string? reason = null)
        {
            if (!_allJobs.TryGetValue(jobId, out var job))
                return false;

            if (job.IsActive)
            {
                Debug.WriteLine($"[PrintQueue] Cannot skip active job {jobId}");
                return false;
            }

            job.State = PrintJobState.Skipped;
            job.StatusMessage = $"Skipped{(reason != null ? $": {reason}" : string.Empty)}";
            job.CompletedAt = DateTime.UtcNow;

            // Remove from active if present
            _activeJobs.TryRemove(job.PrinterName, out _);

            Debug.WriteLine($"[PrintQueue] Job {jobId} skipped");

            JobStateChanged?.Invoke(this, job);

            return true;
        }

        private bool IsDependencySatisfied(PrintJob job)
        {
            if (string.IsNullOrEmpty(job.DependsOnJobId))
                return true;

            if (!_allJobs.TryGetValue(job.DependsOnJobId, out var parent))
                return false;

            return job.IsDependencySatisfied(parent);
        }

        /// <summary>
        /// Gets all jobs for a specific printer.
        /// </summary>
        public IEnumerable<PrintJob> GetJobsForPrinter(string printerName)
        {
            return _allJobs.Values.Where(j => j.PrinterName == printerName);
        }

        /// <summary>
        /// Gets all jobs in any state.
        /// </summary>
        public IEnumerable<PrintJob> GetAllJobs()
        {
            return _allJobs.Values;
        }

        /// <summary>
        /// Gets the active job for a printer (if any).
        /// </summary>
        public PrintJob? GetActiveJob(string printerName)
        {
            return _activeJobs.TryGetValue(printerName, out var job) ? job : null;
        }

        /// <summary>
        /// Gets queue depth for a printer.
        /// </summary>
        public int GetQueueDepth(string printerName)
        {
            return _printerQueues.TryGetValue(printerName, out var queue) ? queue.Count : 0;
        }

        /// <summary>
        /// Gets queue statistics.
        /// </summary>
        public QueueStatistics GetStatistics()
        {
            return new QueueStatistics
            {
                TotalJobsQueued = _totalJobsQueued,
                TotalJobsCompleted = _totalJobsCompleted,
                TotalJobsFailed = _totalJobsFailed,
                ActiveJobs = _activeJobs.Count,
                QueuedJobs = _allJobs.Values.Count(j => j.State == PrintJobState.Queued),
                FailedJobs = _allJobs.Values.Count(j => j.State == PrintJobState.Failed),
                PrintersWithQueues = _printerQueues.Count
            };
        }

        /// <summary>
        /// Clears completed and failed jobs from memory (housekeeping).
        /// </summary>
        public int ClearCompletedJobs(TimeSpan olderThan)
        {
            var cutoff = DateTime.UtcNow - olderThan;
            var removed = 0;

            foreach (var job in _allJobs.Values.Where(j => j.IsTerminal && j.CompletedAt < cutoff).ToList())
            {
                if (_allJobs.TryRemove(job.JobId, out _))
                    removed++;
            }

            if (removed > 0)
                Debug.WriteLine($"[PrintQueue] 🧹 Cleared {removed} old completed jobs");

            return removed;
        }
    }

    /// <summary>
    /// Queue statistics snapshot.
    /// </summary>
    public record QueueStatistics
    {
        public long TotalJobsQueued { get; init; }
        public long TotalJobsCompleted { get; init; }
        public long TotalJobsFailed { get; init; }
        public int ActiveJobs { get; init; }
        public int QueuedJobs { get; init; }
        public int FailedJobs { get; init; }
        public int PrintersWithQueues { get; init; }

        public double SuccessRate => TotalJobsQueued > 0
            ? (double)TotalJobsCompleted / TotalJobsQueued * 100
            : 0;
    }
}
