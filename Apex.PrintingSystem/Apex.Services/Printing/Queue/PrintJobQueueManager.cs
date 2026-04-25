using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Apex.Core.Models;

namespace Apex.Services.Printing.Queue
{
    /// <summary>
    /// HIGH-LEVEL API for the print queue system.
    /// This is the main entry point for submitting and managing print jobs.
    /// 
    /// USAGE:
    /// var manager = PrintJobQueueManager.Instance;
    /// manager.Start(); // Start processing
    /// var jobId = await manager.SubmitPrintJobAsync(printerName, filePath, copies);
    /// 
    /// FEATURES:
    /// - Instant job submission (non-blocking)
    /// - Automatic queue management
    /// - Built-in retry logic
    /// - Network-safe throttling
    /// - Progress tracking
    /// </summary>
    public class PrintJobQueueManager
    {
        private readonly CentralizedPrintQueue _queue;
        private readonly QueuedPrintExecutor _executor;
        
        private static readonly Lazy<PrintJobQueueManager> _instance = 
            new(() => new PrintJobQueueManager());
        
        public static PrintJobQueueManager Instance => _instance.Value;
        
        private PrintJobQueueManager()
        {
            _queue = CentralizedPrintQueue.Instance;
            _executor = QueuedPrintExecutor.Instance;
            
            Debug.WriteLine("[QueueManager] ✓ Print Job Queue Manager initialized");
        }
        
        /// <summary>
        /// Event fired when any job changes state.
        /// </summary>
        public event EventHandler<PrintJob>? JobStateChanged
        {
            add => _queue.JobStateChanged += value;
            remove => _queue.JobStateChanged -= value;
        }
        
        /// <summary>
        /// Event fired when any job's progress updates.
        /// </summary>
        public event EventHandler<PrintJob>? JobProgressChanged
        {
            add => _queue.JobProgressChanged += value;
            remove => _queue.JobProgressChanged -= value;
        }
        
        /// <summary>
        /// Starts the queue processing system.
        /// MUST be called before submitting jobs.
        /// </summary>
        public void Start()
        {
            _executor.Start();
            Debug.WriteLine("[QueueManager] ▶️ Queue system started");
        }
        
        /// <summary>
        /// Stops the queue processing system gracefully.
        /// </summary>
        public async Task StopAsync()
        {
            await _executor.StopAsync();
            Debug.WriteLine("[QueueManager] ⏹️ Queue system stopped");
        }
        
        /// <summary>
        /// Submits a print job to the queue.
        /// Returns immediately with job ID - printing happens asynchronously.
        /// 
        /// This is the CORRECT way to print multiple jobs:
        /// - Submit all jobs instantly (non-blocking)
        /// - System handles execution, throttling, retries automatically
        /// - No network congestion even with 50+ jobs
        /// </summary>
        public async Task<string> SubmitPrintJobAsync(
            string printerName,
            string filePath,
            int copies = 1,
            int priority = 5,
            int maxRetries = 3,
            string? dependsOnJobId = null,
            bool treatSkippedAsComplete = true,
            bool documentMode = false)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(printerName))
                throw new ArgumentException("Printer name is required", nameof(printerName));
            
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path is required", nameof(filePath));
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            
            if (copies < 1)
                throw new ArgumentException("Copies must be at least 1", nameof(copies));
            
            // Create job
            var job = new PrintJob
            {
                PrinterName = printerName,
                FilePath = filePath,
                Copies = copies,
                Priority = priority,
                MaxRetries = maxRetries,
                JobName = Path.GetFileName(filePath),
                DependsOnJobId = dependsOnJobId,
                TreatSkippedAsComplete = treatSkippedAsComplete,
                UseRawDocumentMode = documentMode
            };
            
            // Enqueue (instant, non-blocking)
            var jobId = _queue.EnqueueJob(job);
            
            Debug.WriteLine($"[QueueManager] ✓ Job submitted: {jobId} - {job.JobName} → {printerName} x{copies}");
            
            return await Task.FromResult(jobId);
        }
        
        /// <summary>
        /// Submits a print job with custom settings.
        /// </summary>
        public async Task<string> SubmitPrintJobAsync(
            string printerName,
            string filePath,
            PrintJob settings)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(printerName))
                throw new ArgumentException("Printer name is required", nameof(printerName));
            
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path is required", nameof(filePath));
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            
            // Create new job with updated settings (JobId is init-only, cannot be modified)
            var job = new PrintJob
            {
                JobId = string.IsNullOrEmpty(settings.JobId) ? Guid.NewGuid().ToString() : settings.JobId,
                PrinterName = printerName,
                FilePath = filePath,
                JobName = settings.JobName ?? Path.GetFileName(filePath),
                Copies = settings.Copies,
                Priority = settings.Priority,
                MaxRetries = settings.MaxRetries,
                DependsOnJobId = settings.DependsOnJobId,
                TreatSkippedAsComplete = settings.TreatSkippedAsComplete,
                Metadata = settings.Metadata,
                UseRawDocumentMode = settings.UseRawDocumentMode
            };
            
            var jobId = _queue.EnqueueJob(job);
            
            return await Task.FromResult(jobId);
        }
        
        /// <summary>
        /// Gets a job by ID.
        /// </summary>
        public PrintJob? GetJob(string jobId)
        {
            return _queue.GetAllJobs().FirstOrDefault(j => j.JobId == jobId);
        }
        
        /// <summary>
        /// Gets all jobs for a printer.
        /// </summary>
        public IEnumerable<PrintJob> GetJobsForPrinter(string printerName)
        {
            return _queue.GetJobsForPrinter(printerName);
        }
        
        /// <summary>
        /// Gets all jobs in the system.
        /// </summary>
        public IEnumerable<PrintJob> GetAllJobs()
        {
            return _queue.GetAllJobs();
        }
        
        /// <summary>
        /// Gets queue depth for a printer.
        /// </summary>
        public int GetQueueDepth(string printerName)
        {
            return _queue.GetQueueDepth(printerName);
        }
        
        /// <summary>
        /// Gets the active job for a printer.
        /// </summary>
        public PrintJob? GetActiveJob(string printerName)
        {
            return _queue.GetActiveJob(printerName);
        }
        
        /// <summary>
        /// Cancels a queued job.
        /// </summary>
        public bool CancelJob(string jobId)
        {
            return _queue.CancelJob(jobId);
        }

        /// <summary>
        /// Skips a job (logically complete, no printing).
        /// </summary>
        public bool SkipJob(string jobId, string? reason = null)
        {
            return _queue.SkipJob(jobId, reason);
        }
        
        /// <summary>
        /// Retries a failed job.
        /// </summary>
        public bool RetryJob(string jobId)
        {
            return _queue.RetryJob(jobId);
        }
        
        /// <summary>
        /// Gets queue statistics.
        /// </summary>
        public QueueStatistics GetStatistics()
        {
            return _queue.GetStatistics();
        }
        
        /// <summary>
        /// Clears old completed jobs from memory.
        /// </summary>
        public int ClearOldJobs(TimeSpan olderThan)
        {
            return _queue.ClearCompletedJobs(olderThan);
        }
        
        /// <summary>
        /// Convenience method: Submit multiple jobs at once.
        /// This demonstrates the CORRECT pattern for "Print All":
        /// - All jobs are submitted instantly (non-blocking UI)
        /// - System handles execution with throttling
        /// - No network congestion
        /// </summary>
        public async Task<List<string>> SubmitMultipleJobsAsync(
            string printerName,
            IEnumerable<string> filePaths,
            int copies = 1)
        {
            var jobIds = new List<string>();
            
            foreach (var filePath in filePaths)
            {
                try
                {
                    var jobId = await SubmitPrintJobAsync(printerName, filePath, copies);
                    jobIds.Add(jobId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[QueueManager] Failed to submit {filePath}: {ex.Message}");
                }
            }
            
            Debug.WriteLine($"[QueueManager] ✓ Submitted {jobIds.Count} jobs to '{printerName}' - processing will be throttled automatically");
            
            return jobIds;
        }
    }
}
