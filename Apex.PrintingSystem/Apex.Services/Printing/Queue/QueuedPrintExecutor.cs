using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Apex.Services.Printing.VendorDetection;
using Apex.Services.Printing.Resilience;

namespace Apex.Services.Printing.Queue
{
    /// <summary>
    /// The EXECUTOR - controls print job execution with throttling and network safety.
    /// 
    /// CRITICAL DESIGN PRINCIPLES:
    /// 1. ONE job per printer at a time (enforced via locks)
    /// 2. Controlled delays between jobs (throttling)
    /// 3. Streaming data transmission (never send whole file at once)
    /// 4. Automatic retry on failures
    /// 5. Connection reuse where possible
    /// 6. Network-safe: no TCP storms, no buffer flooding
    /// 
    /// ARCHITECTURE:
    /// - Worker threads monitor queues continuously
    /// - Each printer has dedicated execution context
    /// - Failed jobs are retried with intelligent backoff
    /// - System remains stable under burst load (20+ jobs)
    /// </summary>
    public class QueuedPrintExecutor : IDisposable
    {
        private readonly CentralizedPrintQueue _queue;
        private readonly PrinterLockManager _lockManager;
        private readonly ConcurrentDictionary<string, Task> _workerTasks = new();
        private readonly CancellationTokenSource _shutdownTokenSource = new();

        // Configuration (tuned for snappy field-trial dispatch)
        private readonly TimeSpan _jobThrottleDelay = TimeSpan.FromMilliseconds(50);  // Delay between jobs (was 500ms — too slow)
        private readonly TimeSpan _retryBackoffBase = TimeSpan.FromSeconds(5);        // Base retry delay
        private readonly int _maxConcurrentPrinters = 10;                              // Max printers processing simultaneously

        // Throttling semaphore to prevent network storms
        private readonly SemaphoreSlim _networkThrottle;

        private bool _isRunning;
        private bool _disposed;

        // Singleton
        private static readonly Lazy<QueuedPrintExecutor> _instance =
            new(() => new QueuedPrintExecutor());

        public static QueuedPrintExecutor Instance => _instance.Value;

        private QueuedPrintExecutor()
        {
            _queue = CentralizedPrintQueue.Instance;
            _lockManager = PrinterLockManager.Instance;
            _networkThrottle = new SemaphoreSlim(_maxConcurrentPrinters, _maxConcurrentPrinters);

            Debug.WriteLine("[Executor] ✓ Queued Print Executor initialized");
        }

        /// <summary>
        /// Starts the executor - begins processing print queues.
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                Debug.WriteLine("[Executor] Already running");
                return;
            }

            _isRunning = true;

            // Start master worker that spawns per-printer workers
            Task.Run(() => MasterWorkerAsync(_shutdownTokenSource.Token), _shutdownTokenSource.Token);

            Debug.WriteLine("[Executor] ▶️ Executor started - ready to process jobs");
        }

        /// <summary>
        /// Stops the executor gracefully.
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            Debug.WriteLine("[Executor] ⏸️ Stopping executor...");

            _isRunning = false;
            _shutdownTokenSource.Cancel();

            // Wait for all workers to complete (with exception handling)
            try
            {
                await Task.WhenAll(_workerTasks.Values);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Executor] Worker shutdown errors: {ex.Message}");
                // Continue with shutdown - don't rethrow
            }

            Debug.WriteLine("[Executor] ⏹️ Executor stopped");
        }

        /// <summary>
        /// Master worker - monitors queue and spawns per-printer workers.
        /// </summary>
        private async Task MasterWorkerAsync(CancellationToken cancellationToken)
        {
            Debug.WriteLine("[Executor] Master worker started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Check all printers with queued jobs
                    var printers = _queue.GetAllJobs()
                        .Where(j => j.State == PrintJobState.Queued)
                        .Select(j => j.PrinterName)
                        .Distinct()
                        .ToList();

                    foreach (var printer in printers)
                    {
                        // Ensure worker exists for this printer
                        if (!_workerTasks.ContainsKey(printer))
                        {
                            var workerTask = Task.Run(
                                () => PrinterWorkerAsync(printer, cancellationToken),
                                cancellationToken);

                            _workerTasks[printer] = workerTask;

                            Debug.WriteLine($"[Executor] Spawned worker for printer '{printer}'");
                        }
                    }

                    // Clean up completed workers
                    var completed = _workerTasks
                        .Where(kvp => kvp.Value.IsCompleted)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var printer in completed)
                    {
                        _workerTasks.TryRemove(printer, out _);
                    }

                    // Sleep before next iteration (reduced for snappier dispatch)
                    await Task.Delay(150, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Executor] Master worker error: {ex.Message}");
                    await Task.Delay(5000, cancellationToken);
                }
            }

            Debug.WriteLine("[Executor] Master worker stopped");
        }

        /// <summary>
        /// Per-printer worker - processes jobs for a specific printer.
        /// </summary>
        private async Task PrinterWorkerAsync(string printerName, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[Executor] Worker for '{printerName}' started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Get next job for this printer
                    var job = _queue.GetNextJob(printerName);

                    if (job == null)
                    {
                        // No jobs available - sleep and check again
                        await Task.Delay(300, cancellationToken);
                        continue;
                    }

                    // ── CIRCUIT BREAKER CHECK ──────────────────────────────
                    // If the printer has had too many consecutive failures,
                    // its circuit is Open — skip and wait for reset timeout.
                    if (!CircuitBreakerManager.Instance.CanExecute(printerName))
                    {
                        var state = CircuitBreakerManager.Instance.GetState(printerName);
                        Debug.WriteLine($"[Executor] ⛔ Circuit breaker OPEN for '{printerName}' — " +
                                        $"skipping job {job.JobId}. State: {state}");
                        // Re-queue the job so it doesn't get lost
                        _queue.UpdateJobState(job.JobId, PrintJobState.Queued,
                            "انتظار استعادة الطابعة (circuit breaker مفتوح)...");
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }
                    // ──────────────────────────────────────────────────────

                    // THROTTLING: Wait for network slot
                    await _networkThrottle.WaitAsync(cancellationToken);

                    try
                    {
                        // Execute the job
                        await ExecuteJobAsync(job, cancellationToken);

                        // THROTTLING: Delay before next job to prevent network storms
                        if (_jobThrottleDelay > TimeSpan.Zero)
                        {
                            Debug.WriteLine($"[Executor] Throttling: waiting {_jobThrottleDelay.TotalMilliseconds}ms before next job");
                            await Task.Delay(_jobThrottleDelay, cancellationToken);
                        }
                    }
                    finally
                    {
                        // Release network slot
                        _networkThrottle.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Executor] Worker error for '{printerName}': {ex.Message}");
                    await Task.Delay(5000, cancellationToken);
                }
            }

            Debug.WriteLine($"[Executor] Worker for '{printerName}' stopped");
        }

        /// <summary>
        /// Executes a single print job with full lifecycle management.
        /// </summary>
        private async Task ExecuteJobAsync(PrintJob job, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[Executor] ▶️ Executing job {job.JobId} ({job.JobName}) on '{job.PrinterName}'");

            PrinterLockHandle? lockHandle = null;

            try
            {
                // CRITICAL: Acquire printer lock (blocks if printer busy)
                lockHandle = await _lockManager.AcquireLockAsync(
                    job.PrinterName,
                    job.JobId,
                    cancellationToken);

                // Validate file exists
                if (!File.Exists(job.FilePath))
                {
                    throw new FileNotFoundException($"File not found: {job.FilePath}");
                }

                // Update state
                _queue.UpdateJobState(job.JobId, PrintJobState.Preparing, "Preparing to print...");

                // Get file info
                var fileInfo = new FileInfo(job.FilePath);
                job.FileSize = fileInfo.Length;

                // Use VendorAwarePrintGateway for actual printing
                var gateway = VendorAwarePrintGateway.Instance;

                // Wire up progress events with named handlers for proper cleanup
                Action<string> statusHandler = (msg) =>
                {
                    _queue.UpdateJobProgress(job.JobId, job.Progress, msg);
                };

                Action<int> progressHandler = (progress) =>
                {
                    _queue.UpdateJobProgress(job.JobId, progress);
                };

                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL: Separate paths for Print Operations vs Quick Print
                // NO FALLBACK between paths.
                // ═══════════════════════════════════════════════════════════════════
                Debug.WriteLine($"");
                Debug.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
                Debug.WriteLine($"║               PRINT JOB EXECUTION - DECISION POINT              ║");
                Debug.WriteLine($"╠══════════════════════════════════════════════════════════════════╣");
                Debug.WriteLine($"║ Job ID:              {job.JobId}");
                Debug.WriteLine($"║ File:                {job.JobName}");
                Debug.WriteLine($"║ Printer:             {job.PrinterName}");
                Debug.WriteLine($"║ Copies:              {job.Copies}");
                Debug.WriteLine($"║ UseRawDocumentMode:  {job.UseRawDocumentMode} ◄◄◄ THIS DECIDES THE PATH");
                Debug.WriteLine($"╚══════════════════════════════════════════════════════════════════╝");
                Debug.WriteLine($"");

                if (job.UseRawDocumentMode)
                {
                    // PRINT OPERATIONS PATH - Document-based, no rendering, no fallback
                    Debug.WriteLine($"[Executor] ══════════════════════════════════════");
                    Debug.WriteLine($"[Executor] ★★★ PRINT OPERATIONS PATH (UseRawDocumentMode=true) ★★★");
                    Debug.WriteLine($"[Executor] Using: DocumentPrintService (NO PdfiumViewer, NO RIP)");
                    Debug.WriteLine($"[Executor] ══════════════════════════════════════");

                    _queue.UpdateJobState(job.JobId, PrintJobState.Sending, "Sending document to printer...");

                    try
                    {
                        var docService = DocumentPrintService.Instance;
                        var success = await docService.PrintDocumentAsync(
                            job.PrinterName,
                            job.FilePath,
                            job.Copies,
                            cancellationToken);

                        if (success)
                        {
                            _queue.CompleteJob(job.JobId, true);
                            CircuitBreakerManager.Instance.RecordSuccess(job.PrinterName);
                            Debug.WriteLine($"[Executor] ✅ Job {job.JobId} completed (Document mode)");
                        }
                        else
                        {
                            _queue.CompleteJob(job.JobId, false, "فشلت عملية طباعة المستند");
                            CircuitBreakerManager.Instance.RecordFailure(job.PrinterName, "document mode failure");
                            Debug.WriteLine($"[Executor] ❌ Job {job.JobId} failed (Document mode)");
                        }
                    }
                    catch (DocumentPrintException dpEx)
                    {
                        // NO RETRY for document print - clear error message
                        _queue.CompleteJob(job.JobId, false, dpEx.Message);
                        CircuitBreakerManager.Instance.RecordFailure(job.PrinterName, dpEx.Message);
                        Debug.WriteLine($"[Executor] ❌ Job {job.JobId} failed: {dpEx.Message} ({dpEx.ErrorCode})");
                    }
                }
                else
                {
                    // QUICK PRINT PATH - Full rendering with fallback
                    Debug.WriteLine($"[Executor] ══════════════════════════════════════");
                    Debug.WriteLine($"[Executor] ⚠️⚠️⚠️ QUICK PRINT PATH (UseRawDocumentMode=false) ⚠️⚠️⚠️");
                    Debug.WriteLine($"[Executor] Using: VendorAwarePrintGateway (PdfiumViewer + RIP)");
                    Debug.WriteLine($"[Executor] WARNING: This will render pages and may create large spool files!");
                    Debug.WriteLine($"[Executor] ══════════════════════════════════════");

                    try
                    {
                        gateway.StatusChanged += statusHandler;
                        gateway.ProgressChanged += progressHandler;

                        _queue.UpdateJobState(job.JobId, PrintJobState.Sending, "Sending to printer...");

                        var result = await gateway.PrintAsync(
                            job.PrinterName,
                            job.FilePath,
                            job.Copies,
                            null,
                            cancellationToken,
                            documentMode: false);  // Always false for Quick Print

                        if (result.Success)
                        {
                            _queue.CompleteJob(job.JobId, true);
                            CircuitBreakerManager.Instance.RecordSuccess(job.PrinterName);
                            Debug.WriteLine($"[Executor] ✅ Job {job.JobId} completed (Quick Print)");
                        }
                        else
                        {
                            if (job.CanRetry)
                            {
                                await HandleFailedJobWithRetryAsync(job, result.ErrorMessage);
                            }
                            else
                            {
                                _queue.CompleteJob(job.JobId, false, result.ErrorMessage);
                                CircuitBreakerManager.Instance.RecordFailure(job.PrinterName, result.ErrorMessage);
                                Debug.WriteLine($"[Executor] ❌ Job {job.JobId} failed: {result.ErrorMessage}");
                            }
                        }
                    }
                    finally
                    {
                        gateway.StatusChanged -= statusHandler;
                        gateway.ProgressChanged -= progressHandler;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _queue.UpdateJobState(job.JobId, PrintJobState.Cancelled, "Cancelled");
                Debug.WriteLine($"[Executor] Job {job.JobId} cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Executor] ❌ Job {job.JobId} error: {ex.Message}");

                // Check if can retry
                if (job.CanRetry)
                {
                    CircuitBreakerManager.Instance.RecordFailure(job.PrinterName, ex.Message);
                    await HandleFailedJobWithRetryAsync(job, ex.Message);
                }
                else
                {
                    _queue.CompleteJob(job.JobId, false, ex.Message);
                    CircuitBreakerManager.Instance.RecordFailure(job.PrinterName, ex.Message);
                }
            }
            finally
            {
                // CRITICAL: Always release lock
                lockHandle?.Dispose();
            }
        }

        /// <summary>
        /// Handles failed job with intelligent retry logic.
        /// </summary>
        private async Task HandleFailedJobWithRetryAsync(PrintJob job, string? errorMessage)
        {
            // Calculate backoff delay (exponential with jitter)
            var retryDelay = _retryBackoffBase * Math.Pow(2, job.RetryCount);
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
            var totalDelay = retryDelay + jitter;

            Debug.WriteLine($"[Executor] ↻ Job {job.JobId} will retry in {totalDelay.TotalSeconds:F1}s (attempt {job.RetryCount + 1}/{job.MaxRetries})");

            // Mark as retrying
            _queue.UpdateJobState(
                job.JobId,
                PrintJobState.Retrying,
                $"Retrying in {totalDelay.TotalSeconds:F0}s... (attempt {job.RetryCount + 1}/{job.MaxRetries})");

            // Wait before retry
            await Task.Delay(totalDelay);

            // Re-queue for retry
            _queue.RetryJob(job.JobId);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _shutdownTokenSource.Cancel();
            _shutdownTokenSource.Dispose();
            _networkThrottle.Dispose();

            _disposed = true;

            Debug.WriteLine("[Executor] Disposed");
        }
    }
}
