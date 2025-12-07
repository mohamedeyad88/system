using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// Manages batch print jobs with integrated document conversion.
    /// Flow: Pending → Converting → Printing → Completed
    /// </summary>
    public class BatchPrintJobManager
    {
        private readonly IPrintEngine _printEngine;
        private readonly IPrintJobLogger _logger;
        private readonly IDocumentConverter? _converter;
        
        public event EventHandler<BatchJob>? OnJobStatusChanged;
        public event EventHandler<string>? OnBatchStatusChanged;
        public event EventHandler<BatchProgress>? OnBatchProgressChanged;

        private CancellationTokenSource? _cts;
        private bool _isPaused;
        private readonly ManualResetEventSlim _pauseEvent = new(true);

        public BatchPrintJobManager(
            IPrintEngine printEngine, 
            IPrintJobLogger logger,
            IDocumentConverter? converter = null)
        {
            _printEngine = printEngine;
            _logger = logger;
            _converter = converter;
        }

        /// <summary>
        /// Processes a batch of jobs: converts then prints sequentially to one printer.
        /// </summary>
        public async Task ProcessBatchAsync(string printerName, List<BatchJob> jobs, BatchSettings settings)
        {
            _cts = new CancellationTokenSource();
            _isPaused = false;
            _pauseEvent.Set();

            var batchStopwatch = Stopwatch.StartNew();
            int completedCount = 0;
            int failedCount = 0;

            OnBatchStatusChanged?.Invoke(this, "Starting batch...");

            foreach (var job in jobs)
            {
                if (_cts.IsCancellationRequested) break;
                
                // Wait if paused
                _pauseEvent.Wait(_cts.Token);
                
                if (job.Status == "Completed" || job.Status == "Skipped") 
                {
                    completedCount++;
                    continue;
                }

                try
                {
                    // Step 1: Convert if needed
                    if (!await ConvertJobAsync(job, _cts.Token))
                    {
                        failedCount++;
                        if (settings.StopOnError) break;
                        continue;
                    }

                    // Step 2: Print with retry
                    var printSuccess = await PrintJobWithRetryAsync(printerName, job, settings, _cts.Token);
                    
                    if (printSuccess)
                        completedCount++;
                    else
                        failedCount++;

                    // Stop on error if configured
                    if (!printSuccess && settings.StopOnError)
                    {
                        OnBatchStatusChanged?.Invoke(this, "Batch stopped on error");
                        break;
                    }

                    // Delay between jobs
                    if (settings.DelayBetweenJobsMs > 0)
                    {
                        await Task.Delay(settings.DelayBetweenJobsMs, _cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    job.Status = "Failed";
                    job.ErrorMessage = ex.Message;
                    failedCount++;
                    NotifyJobStatus(job);
                    
                    if (settings.StopOnError) break;
                }

                // Update batch progress
                ReportBatchProgress(jobs, completedCount, failedCount, batchStopwatch.Elapsed);
            }

            batchStopwatch.Stop();
            var finalStatus = _cts.IsCancellationRequested 
                ? "Batch cancelled" 
                : $"Batch completed: {completedCount} succeeded, {failedCount} failed";
            
            OnBatchStatusChanged?.Invoke(this, finalStatus);
        }

        /// <summary>
        /// Converts a single job to PDF if needed.
        /// </summary>
        private async Task<bool> ConvertJobAsync(BatchJob job, CancellationToken ct)
        {
            // PDF files skip conversion
            if (job.OriginalExtension == ".pdf")
            {
                job.ConversionStatus = ConversionStatus.Skipped;
                job.ConvertedFilePath = job.FilePath;
                return true;
            }

            if (_converter == null)
            {
                job.ConversionStatus = ConversionStatus.Failed;
                job.ConversionError = "No converter available";
                job.Status = "Failed";
                job.ErrorMessage = "Document conversion not available. Please install LibreOffice.";
                NotifyJobStatus(job);
                return false;
            }

            job.ConversionStatus = ConversionStatus.Converting;
            job.Status = "Converting";
            NotifyJobStatus(job);

            try
            {
                var result = await _converter.ConvertToPdfAsync(job.FilePath, null, ct);
                
                if (result.Success)
                {
                    job.ConversionStatus = ConversionStatus.Ready;
                    job.ConvertedFilePath = result.OutputPath;
                    job.TotalPages = result.EstimatedPages;
                    return true;
                }
                else
                {
                    job.ConversionStatus = ConversionStatus.Failed;
                    job.ConversionError = result.ErrorMessage;
                    job.Status = "Failed";
                    job.ErrorMessage = $"Conversion failed: {result.ErrorMessage}";
                    NotifyJobStatus(job);
                    return false;
                }
            }
            catch (Exception ex)
            {
                job.ConversionStatus = ConversionStatus.Failed;
                job.ConversionError = ex.Message;
                job.Status = "Failed";
                job.ErrorMessage = $"Conversion error: {ex.Message}";
                NotifyJobStatus(job);
                return false;
            }
        }

        /// <summary>
        /// Prints a job with retry logic and progress tracking.
        /// </summary>
        private async Task<bool> PrintJobWithRetryAsync(
            string printerName, BatchJob job, BatchSettings settings, CancellationToken ct)
        {
            job.Status = "Printing";
            job.StartTime = DateTime.Now;
            job.CurrentPage = 0;
            NotifyJobStatus(job);

            bool success = false;
            int attempts = 0;
            var stopwatch = Stopwatch.StartNew();

            while (!success && attempts <= settings.RetryCount)
            {
                ct.ThrowIfCancellationRequested();
                _pauseEvent.Wait(ct);

                if (attempts > 0)
                {
                    job.Status = $"Retrying ({attempts}/{settings.RetryCount})...";
                    NotifyJobStatus(job);
                    await Task.Delay(1000 * attempts, ct);
                }

                try
                {
                    // Use converted file path if available
                    var fileToPrint = job.PrintFilePath;
                    
                    // Create progress callback for page-by-page updates
                    var pageProgress = new Progress<int>(page =>
                    {
                        job.CurrentPage = page;
                        
                        // Calculate speed and ETA
                        var elapsed = stopwatch.Elapsed.TotalSeconds;
                        if (elapsed > 0 && page > 0)
                        {
                            job.PagesPerSecond = page / elapsed;
                            var remainingPages = job.TotalPages - page;
                            if (job.PagesPerSecond > 0)
                            {
                                var etaSeconds = remainingPages / job.PagesPerSecond;
                                job.ETA = FormatEta(TimeSpan.FromSeconds(etaSeconds));
                            }
                        }
                        
                        NotifyJobStatus(job);
                    });

                    success = await _printEngine.PrintAsync(printerName, fileToPrint);
                    
                    if (!success)
                    {
                        job.ErrorMessage = "Print engine returned failure.";
                    }
                }
                catch (Exception ex)
                {
                    job.ErrorMessage = ex.Message;
                }

                if (!success)
                {
                    attempts++;
                    job.RetryCount = attempts;
                }
            }

            stopwatch.Stop();
            job.TimeTaken = stopwatch.Elapsed;
            job.EndTime = DateTime.Now;
            job.Status = success ? "Completed" : "Failed";
            job.ETA = "";

            _logger.LogJob(printerName, job.FilePath, success, success ? "Success" : job.ErrorMessage);
            NotifyJobStatus(job);

            return success;
        }

        public void Pause()
        {
            _isPaused = true;
            _pauseEvent.Reset();
            OnBatchStatusChanged?.Invoke(this, "Paused");
        }

        public void Resume()
        {
            _isPaused = false;
            _pauseEvent.Set();
            OnBatchStatusChanged?.Invoke(this, "Resumed");
        }

        public void CancelBatch()
        {
            _cts?.Cancel();
            _pauseEvent.Set(); // Unblock if paused
        }

        public bool IsPaused => _isPaused;

        private void NotifyJobStatus(BatchJob job)
        {
            OnJobStatusChanged?.Invoke(this, job);
        }

        private void ReportBatchProgress(List<BatchJob> jobs, int completed, int failed, TimeSpan elapsed)
        {
            var progress = new BatchProgress
            {
                TotalJobs = jobs.Count,
                CompletedJobs = completed,
                FailedJobs = failed,
                PendingJobs = jobs.Count - completed - failed,
                ConvertingJobs = jobs.Count(j => j.ConversionStatus == ConversionStatus.Converting),
                PrintingJobs = jobs.Count(j => j.Status == "Printing"),
                ElapsedTime = elapsed,
                PercentComplete = jobs.Count > 0 ? (double)completed / jobs.Count * 100 : 0
            };

            OnBatchProgressChanged?.Invoke(this, progress);
        }

        private static string FormatEta(TimeSpan eta)
        {
            if (eta.TotalMinutes >= 1)
                return $"{(int)eta.TotalMinutes}m {eta.Seconds}s";
            return $"{eta.Seconds}s";
        }
    }

    /// <summary>
    /// Overall batch progress information.
    /// </summary>
    public class BatchProgress
    {
        public int TotalJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int FailedJobs { get; set; }
        public int PendingJobs { get; set; }
        public int ConvertingJobs { get; set; }
        public int PrintingJobs { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public double PercentComplete { get; set; }
    }
}
