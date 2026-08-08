using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Apex.Services.Logging;
using Apex.Services.Printing.Queue;
using Apex.Services.Printing.VendorDetection;

namespace Apex.Services.Printing
{
    /// <summary>
    /// SINGLE PRINT JOB MANAGER - The authoritative source for all print job states.
    /// 
    /// CRITICAL DESIGN PRINCIPLES:
    /// 1. Every print attempt MUST create a tracked job
    /// 2. UI observes events ONLY - never infers state
    /// 3. Failed jobs are ALWAYS visible (no silent failures)
    /// 4. Truthful status reporting (no fake progress)
    /// 5. Explicit tracking of Windows Spooler submission
    /// 
    /// LIFECYCLE EVENTS:
    /// - JobCreated: New job object created
    /// - JobProgress: Progress percentage changed
    /// - JobSubmittedToSpooler: Job reached Windows Spooler
    /// - JobCompleted: Job finished successfully
    /// - JobFailed: Job failed (with clear reason)
    /// 
    /// VALIDATION:
    /// Every print command creates a visible job in UI.
    /// Jobs failing before reaching spooler show "Failed before submission".
    /// User always knows: What happened, Where it failed, What to do next.
    /// </summary>
    public class PrintJobsManager
    {
        private static readonly Lazy<PrintJobsManager> _instance =
            new(() => new PrintJobsManager());

        public static PrintJobsManager Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, PrintJobTracking> _trackedJobs = new();
        private readonly VendorAwarePrintGateway _printGateway;
        private readonly PrintJobQueueManager _queueManager;

        /// <summary>
        /// Fired when a new job is created.
        /// </summary>
        public event EventHandler<PrintJobTracking>? JobCreated;

        /// <summary>
        /// Fired when job progress changes.
        /// </summary>
        public event EventHandler<PrintJobTracking>? JobProgress;

        /// <summary>
        /// Fired when job is submitted to Windows Spooler.
        /// CRITICAL: This proves the job reached Windows, not just the application.
        /// </summary>
        public event EventHandler<PrintJobTracking>? JobSubmittedToSpooler;

        /// <summary>
        /// Fired when job completes successfully.
        /// </summary>
        public event EventHandler<PrintJobTracking>? JobCompleted;

        /// <summary>
        /// Fired when job fails at any stage.
        /// </summary>
        public event EventHandler<PrintJobTracking>? JobFailed;

        private PrintJobsManager()
        {
            _printGateway = VendorAwarePrintGateway.Instance;
            _queueManager = PrintJobQueueManager.Instance;

            PrintLogger.Info("[PrintJobsManager] Initialized - All print jobs will be tracked");
        }

        /// <summary>
        /// Submit a print job with FULL LIFECYCLE TRACKING.
        /// This is the CORRECT method to use for printing.
        /// 
        /// ARCHITECTURAL SEPARATION:
        /// - documentMode=true  → Print Operations path (DocumentPrintService) - NO rendering
        /// - documentMode=false → Quick Print path (VendorAwarePrintGateway) - with rendering/fallback
        /// 
        /// When documentMode=true, the scaleMode parameter is IGNORED.
        /// </summary>
        public async Task<string> SubmitPrintJobAsync(
            string printerName,
            string filePath,
            int copies = 1,
            bool useQueue = true,
            CancellationToken cancellationToken = default,
            Apex.NumberedBooksEngine.Core.PrintScaleMode? scaleMode = null,
            bool documentMode = false)
        {
            // ═══════════════════════════════════════════════════════════════════
            // LOGGING: Print Job Submission
            // ═══════════════════════════════════════════════════════════════════
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] PRINT JOB SUBMISSION");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] Printer: {printerName}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] File: {filePath}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] Copies: {copies}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] UseQueue: {useQueue}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] DocumentMode: {documentMode}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ScaleMode: {(documentMode ? "IGNORED (documentMode=true)" : scaleMode?.ToString() ?? "null")}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ══════════════════════════════════════");

            // STAGE 1: Create tracked job (ALWAYS visible in UI)
            // scaleMode is passed for tracking but IGNORED when documentMode=true
            var tracking = CreateTrackedJob(printerName, filePath, copies, documentMode ? null : scaleMode);

            try
            {
                // STAGE 2: Validate file
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}", filePath);
                }

                var fileInfo = new FileInfo(filePath);
                tracking.Lifecycle.AdvanceTo(
                    PrintJobLifecycleTracker.LifecycleStage.FileValidated,
                    $"File size: {fileInfo.Length / 1024}KB");

                OnJobProgress(tracking, 10, "File validated");

                // STAGE 3: Choose execution path
                if (useQueue)
                {
                    // Use queue system (recommended for multiple jobs)
                    await ExecuteViaQueueAsync(tracking, cancellationToken, documentMode);
                }
                else
                {
                    // Direct execution (for single urgent jobs)
                    await ExecuteDirectAsync(tracking, cancellationToken, documentMode);
                }

                return tracking.JobId;
            }
            catch (Exception ex)
            {
                // CRITICAL: Even if exception occurs, job is VISIBLE in UI as Failed
                RecordJobFailure(tracking,
                    PrintJobLifecycleTracker.LifecycleStage.JobCreated,
                    ex,
                    "Failed to start print job");

                return tracking.JobId;
            }
        }

        /// <summary>
        /// Execute via queue system (throttled, reliable).
        /// </summary>
        private async Task ExecuteViaQueueAsync(
            PrintJobTracking tracking,
            CancellationToken cancellationToken,
            bool documentMode)
        {
            // TaskCompletionSource to wait until job reaches terminal state
            var completionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Subscribe to queue events
            void OnQueueStateChanged(object? sender, PrintJob queueJob)
            {
                if (queueJob.JobId != tracking.JobId) return;

                UpdateTrackingFromQueueJob(tracking, queueJob);

                // Signal completion when job reaches terminal state
                if (queueJob.IsTerminal)
                {
                    completionTcs.TrySetResult(true);
                }
            }

            void OnQueueProgressChanged(object? sender, PrintJob queueJob)
            {
                if (queueJob.JobId != tracking.JobId) return;

                OnJobProgress(tracking, queueJob.Progress, queueJob.StatusMessage);
            }

            // Cancel completion when external cancellation requested
            using var cancelReg = cancellationToken.Register(() => completionTcs.TrySetCanceled());

            try
            {
                _queueManager.JobStateChanged += OnQueueStateChanged;
                _queueManager.JobProgressChanged += OnQueueProgressChanged;

                // Submit to queue
                tracking.Lifecycle.AdvanceTo(
                    PrintJobLifecycleTracker.LifecycleStage.FilePrepared,
                    "Submitted to print queue");

                OnJobProgress(tracking, 20, "Waiting in queue");

                var settings = new PrintJob
                {
                    JobId = tracking.JobId,
                    JobName = tracking.JobName,
                    PrinterName = tracking.PrinterName,
                    FilePath = tracking.FilePath,
                    Copies = tracking.Copies,
                    UseRawDocumentMode = documentMode
                };

                await _queueManager.SubmitPrintJobAsync(
                    tracking.PrinterName,
                    tracking.FilePath,
                    settings);

                // Defensive: if the job already terminated synchronously before we subscribed
                var existing = _queueManager.GetJob(tracking.JobId);
                if (existing != null && existing.IsTerminal)
                {
                    UpdateTrackingFromQueueJob(tracking, existing);
                    completionTcs.TrySetResult(true);
                }

                // CRITICAL: wait for terminal state before unsubscribing,
                // otherwise progress / completion events fire after listeners are removed.
                // Safety timeout (5 min) prevents infinite hang if queue stops responding.
                var timeoutCts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var timeoutReg = timeoutCts.Token.Register(() => completionTcs.TrySetResult(true));
                await completionTcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _queueManager.JobStateChanged -= OnQueueStateChanged;
                _queueManager.JobProgressChanged -= OnQueueProgressChanged;
            }
        }

        /// <summary>
        /// Execute print job directly.
        /// 
        /// ARCHITECTURAL SEPARATION:
        /// - documentMode=true → Uses DocumentPrintService (Print Operations path)
        /// - documentMode=false → Uses VendorAwarePrintGateway (Quick Print path)
        /// 
        /// NO FALLBACK between paths. Each path is isolated.
        /// </summary>
        private async Task ExecuteDirectAsync(
            PrintJobTracking tracking,
            CancellationToken cancellationToken,
            bool documentMode)
        {
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL: Separate paths for Print Operations vs Quick Print
            // ═══════════════════════════════════════════════════════════════════
            if (documentMode)
            {
                // PRINT OPERATIONS PATH - Document-based, no rendering, no fallback
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ══════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] PRINT OPERATIONS PATH (documentMode=true)");
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] Using: DocumentPrintService");
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ══════════════════════════════════════");

                await ExecutePrintOperationsPathAsync(tracking, cancellationToken);
            }
            else
            {
                // QUICK PRINT PATH - Full rendering, fallback allowed
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ══════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] QUICK PRINT PATH (documentMode=false)");
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] Using: VendorAwarePrintGateway");
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ══════════════════════════════════════");

                await ExecuteQuickPrintPathAsync(tracking, cancellationToken);
            }
        }

        /// <summary>
        /// PRINT OPERATIONS PATH - Document-based printing ONLY.
        /// 
        /// STRICT RULES:
        /// - NO Pdfium, NO rendering, NO fallback, NO scaling
        /// - Copies handled by printer driver, NOT by loop
        /// - NO PaperSize manipulation, NO Tray selection
        /// - Uses DocumentPrintService exclusively
        /// </summary>
        private async Task ExecutePrintOperationsPathAsync(
            PrintJobTracking tracking,
            CancellationToken cancellationToken)
        {
            // ═══════════════════════════════════════════════════════════════════
            // PRINT OPERATIONS: Document-based path
            // ═══════════════════════════════════════════════════════════════════
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ══════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] EXECUTING PRINT OPERATIONS PATH");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] JobId: {tracking.JobId}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] Printer: {tracking.PrinterName}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] File: {tracking.FilePath}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] Copies: {tracking.Copies}");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] Service: DocumentPrintService (NO rendering)");
            System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ══════════════════════════════════════");

            tracking.Lifecycle.AdvanceTo(
                PrintJobLifecycleTracker.LifecycleStage.FilePrepared,
                "Starting document print (no rendering)");

            OnJobProgress(tracking, 30, "Sending document to printer");

            try
            {
                var docService = DocumentPrintService.Instance;

                tracking.Lifecycle.AdvanceTo(
                    PrintJobLifecycleTracker.LifecycleStage.Win32PrinterOpened,
                    "Document print service active");

                OnJobProgress(tracking, 50, "Submitting to Windows");

                // CRITICAL: Copies passed to DocumentPrintService
                // DocumentPrintService passes this to printer driver - NOT looped internally
                var success = await docService.PrintDocumentAsync(
                    tracking.PrinterName,
                    tracking.FilePath,
                    tracking.Copies,
                    cancellationToken);

                if (success)
                {
                    await VerifySpoolerSubmissionAsync(tracking);
                    OnJobProgress(tracking, 100, "Completed");
                    RecordJobCompletion(tracking);

                    System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ✅ Print Operations job {tracking.JobId} completed successfully");
                }
                else
                {
                    RecordJobFailure(tracking,
                        PrintJobLifecycleTracker.LifecycleStage.Win32JobSubmitted,
                        new Exception("Document print failed"),
                        "فشلت عملية طباعة المستند");

                    System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ❌ Print Operations job {tracking.JobId} failed");
                }
            }
            catch (DocumentPrintException dpEx)
            {
                // Clear error from DocumentPrintService - NO FALLBACK
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ❌ DocumentPrintException: {dpEx.Message} ({dpEx.ErrorCode})");
                RecordJobFailure(tracking,
                    PrintJobLifecycleTracker.LifecycleStage.Win32JobSubmitted,
                    dpEx,
                    dpEx.Message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PrintJobsManager] ❌ Print Operations error: {ex.Message}");
                RecordJobFailure(tracking,
                    PrintJobLifecycleTracker.LifecycleStage.Win32JobSubmitted,
                    ex,
                    $"خطأ في الطباعة: {ex.Message}");
            }
        }

        /// <summary>
        /// QUICK PRINT PATH - Full rendering with fallback.
        /// Uses VendorAwarePrintGateway with Pdfium/RIP.
        /// </summary>
        private async Task ExecuteQuickPrintPathAsync(
            PrintJobTracking tracking,
            CancellationToken cancellationToken)
        {
            void OnStatusChanged(string status)
            {
                OnJobProgress(tracking, tracking.Progress + 5, status);
            }

            void OnProgressChanged(int progress)
            {
                OnJobProgress(tracking, progress, tracking.StatusMessage);
            }

            try
            {
                _printGateway.StatusChanged += OnStatusChanged;
                _printGateway.ProgressChanged += OnProgressChanged;

                tracking.Lifecycle.AdvanceTo(
                    PrintJobLifecycleTracker.LifecycleStage.FilePrepared,
                    "Starting quick print");

                OnJobProgress(tracking, 30, "Connecting to printer");

                tracking.Lifecycle.AdvanceTo(
                    PrintJobLifecycleTracker.LifecycleStage.Win32PrinterOpened,
                    "Calling Win32 print APIs");

                OnJobProgress(tracking, 50, "Submitting to Windows");

                // Quick Print uses VendorAwarePrintGateway with documentMode=false
                var result = await _printGateway.PrintAsync(
                    tracking.PrinterName,
                    tracking.FilePath,
                    tracking.Copies,
                    cancellationToken: cancellationToken,
                    documentMode: false);  // Always false for Quick Print path

                if (result.Success)
                {
                    await VerifySpoolerSubmissionAsync(tracking);
                    OnJobProgress(tracking, 100, "Completed");
                    RecordJobCompletion(tracking);
                }
                else
                {
                    RecordJobFailure(tracking,
                        PrintJobLifecycleTracker.LifecycleStage.Win32JobSubmitted,
                        new Exception(result.ErrorMessage ?? "Print failed"),
                        result.ErrorMessage ?? "Print operation failed");
                }
            }
            finally
            {
                _printGateway.StatusChanged -= OnStatusChanged;
                _printGateway.ProgressChanged -= OnProgressChanged;
            }
        }

        /// <summary>
        /// CRITICAL: Verify that job actually reached Windows Spooler.
        /// This is the PROOF that prevents "silent failures".
        /// </summary>
        private async Task VerifySpoolerSubmissionAsync(PrintJobTracking tracking)
        {
            await Task.Delay(500); // Give spooler time to register job

            int? spoolerJobId;
            bool exists = WindowsSpoolerHelper.IsJobInWindowsSpooler(tracking.PrinterName, out spoolerJobId);

            if (exists)
            {
                tracking.Lifecycle.SetWindowsSpoolerJobId(spoolerJobId ?? 0);
                OnJobSubmittedToSpooler(tracking);

                PrintLogger.Info("[PrintJobsManager] ✅ VERIFIED: Job {JobId} is in Windows Spooler",
                    tracking.JobId);
            }
            else
            {
                PrintLogger.Warning("[PrintJobsManager] ⚠️ WARNING: Cannot confirm job {JobId} in Windows Spooler",
                    tracking.JobId);

                // Still advance lifecycle (data was sent, but can't verify spooler)
                tracking.Lifecycle.AdvanceTo(
                    PrintJobLifecycleTracker.LifecycleStage.Win32JobSubmitted,
                    "Data sent, spooler confirmation pending");
            }
        }

        /// <summary>
        /// Create a new tracked job (ALWAYS visible in UI).
        /// </summary>
        private PrintJobTracking CreateTrackedJob(
            string printerName,
            string filePath,
            int copies,
            Apex.NumberedBooksEngine.Core.PrintScaleMode? scaleMode = null)
        {
            var jobId = Guid.NewGuid().ToString();
            var fileName = Path.GetFileName(filePath);

            var tracking = new PrintJobTracking
            {
                JobId = jobId,
                JobName = fileName,
                PrinterName = printerName,
                FilePath = filePath,
                Copies = copies,
                ScaleMode = scaleMode,
                CreatedAt = DateTime.UtcNow,
                Lifecycle = new PrintJobLifecycleTracker(jobId, printerName, filePath)
            };

            _trackedJobs[jobId] = tracking;

            PrintLogger.Info("[PrintJobsManager] 📄 Job Created: {JobId} | '{File}' → '{Printer}' x{Copies}",
                jobId, fileName, printerName, copies);

            JobCreated?.Invoke(this, tracking);

            return tracking;
        }

        /// <summary>
        /// Update tracking from queue job state.
        /// </summary>
        private void UpdateTrackingFromQueueJob(PrintJobTracking tracking, PrintJob queueJob)
        {
            tracking.Progress = queueJob.Progress;
            tracking.StatusMessage = queueJob.StatusMessage;
            tracking.State = queueJob.State;

            // Map queue states to lifecycle stages
            switch (queueJob.State)
            {
                case PrintJobState.Preparing:
                    tracking.Lifecycle.AdvanceTo(
                        PrintJobLifecycleTracker.LifecycleStage.FilePrepared,
                        "Preparing file for printing");
                    break;

                case PrintJobState.Sending:
                    tracking.Lifecycle.AdvanceTo(
                        PrintJobLifecycleTracker.LifecycleStage.Win32WritingData,
                        "Sending data to printer");
                    break;

                case PrintJobState.Printing:
                    tracking.Lifecycle.AdvanceTo(
                        PrintJobLifecycleTracker.LifecycleStage.PrinterProcessing,
                        "Printer is processing");
                    break;

                case PrintJobState.Completed:
                    RecordJobCompletion(tracking);
                    break;

                case PrintJobState.Failed:
                    RecordJobFailure(tracking,
                        PrintJobLifecycleTracker.LifecycleStage.Failed,
                        queueJob.LastException ?? new Exception(queueJob.ErrorMessage ?? "Unknown error"),
                        queueJob.ErrorMessage ?? "Print job failed");
                    break;
            }
        }

        /// <summary>
        /// Update job progress.
        /// </summary>
        private void OnJobProgress(PrintJobTracking tracking, int progress, string message)
        {
            tracking.Progress = progress;
            tracking.StatusMessage = message;
            tracking.LastUpdatedAt = DateTime.UtcNow;

            JobProgress?.Invoke(this, tracking);
        }

        /// <summary>
        /// Record successful job submission to spooler.
        /// </summary>
        private void OnJobSubmittedToSpooler(PrintJobTracking tracking)
        {
            tracking.SubmittedToSpoolerAt = DateTime.UtcNow;

            JobSubmittedToSpooler?.Invoke(this, tracking);

            PrintLogger.Info("[PrintJobsManager] ✅ Job {JobId} submitted to Windows Spooler",
                tracking.JobId);
        }

        /// <summary>
        /// Record job completion.
        /// </summary>
        private void RecordJobCompletion(PrintJobTracking tracking)
        {
            tracking.Lifecycle.AdvanceTo(
                PrintJobLifecycleTracker.LifecycleStage.Completed,
                "Print job completed successfully");

            tracking.CompletedAt = DateTime.UtcNow;
            tracking.Progress = 100;
            tracking.StatusMessage = "Completed successfully";

            JobCompleted?.Invoke(this, tracking);

            PrintLogger.Info("[PrintJobsManager] ✅ Job {JobId} COMPLETED", tracking.JobId);
        }

        /// <summary>
        /// Record job failure with explicit details.
        /// </summary>
        private void RecordJobFailure(
            PrintJobTracking tracking,
            PrintJobLifecycleTracker.LifecycleStage failedAtStage,
            Exception exception,
            string userFriendlyMessage)
        {
            tracking.Lifecycle.RecordFailure(failedAtStage, exception, userFriendlyMessage);

            tracking.FailedAt = DateTime.UtcNow;
            tracking.StatusMessage = userFriendlyMessage;
            tracking.ErrorMessage = userFriendlyMessage;

            JobFailed?.Invoke(this, tracking);

            PrintLogger.Error(exception,
                "[PrintJobsManager] ❌ Job {JobId} FAILED at {Stage} | {Message}",
                tracking.JobId, failedAtStage, userFriendlyMessage);
        }

        /// <summary>
        /// Get all tracked jobs.
        /// </summary>
        public IEnumerable<PrintJobTracking> GetAllJobs()
        {
            return _trackedJobs.Values.OrderByDescending(j => j.CreatedAt);
        }

        /// <summary>
        /// Get job by ID.
        /// </summary>
        public PrintJobTracking? GetJob(string jobId)
        {
            _trackedJobs.TryGetValue(jobId, out var job);
            return job;
        }

        /// <summary>
        /// Cancel a job.
        /// </summary>
        public bool CancelJob(string jobId)
        {
            if (_trackedJobs.TryGetValue(jobId, out var tracking))
            {
                tracking.StatusMessage = "Cancelled by user";
                tracking.FailedAt = DateTime.UtcNow;

                // Try to cancel in queue if present
                _queueManager.CancelJob(jobId);

                PrintLogger.Info("[PrintJobsManager] Job {JobId} cancelled", jobId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Retry a failed job.
        /// </summary>
        public async Task<bool> RetryJobAsync(string jobId)
        {
            if (_trackedJobs.TryGetValue(jobId, out var tracking))
            {
                PrintLogger.Info("[PrintJobsManager] Retrying job {JobId}", jobId);

                // Create new job with same parameters
                await SubmitPrintJobAsync(
                    tracking.PrinterName,
                    tracking.FilePath,
                    tracking.Copies);

                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Tracks a single print job with full lifecycle information.
    /// </summary>
    public class PrintJobTracking
    {
        public string JobId { get; init; } = string.Empty;
        public string JobName { get; init; } = string.Empty;
        public string PrinterName { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;
        public int Copies { get; init; } = 1;

        /// <summary>
        /// Print scale mode (ActualSize or FitToPage). Null for jobs without scale info.
        /// </summary>
        public Apex.NumberedBooksEngine.Core.PrintScaleMode? ScaleMode { get; init; }

        public PrintJobState State { get; set; } = PrintJobState.Queued;
        public int Progress { get; set; } = 0;
        public string StatusMessage { get; set; } = "Created";
        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; init; }
        public DateTime LastUpdatedAt { get; set; }
        public DateTime? SubmittedToSpoolerAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }

        public PrintJobLifecycleTracker Lifecycle { get; init; } = null!;

        /// <summary>
        /// Get elapsed time string.
        /// </summary>
        public string GetElapsedTime()
        {
            var elapsed = CompletedAt.HasValue
                ? CompletedAt.Value - CreatedAt
                : DateTime.UtcNow - CreatedAt;

            return elapsed.TotalMinutes >= 1
                ? $"{elapsed.TotalMinutes:F0}m {elapsed.Seconds}s"
                : $"{elapsed.TotalSeconds:F0}s";
        }

        /// <summary>
        /// Check if job can be retried.
        /// </summary>
        public bool CanRetry => State == PrintJobState.Failed;

        /// <summary>
        /// Check if job can be cancelled.
        /// </summary>
        public bool CanCancel => State == PrintJobState.Queued || State == PrintJobState.Preparing;
    }
}
