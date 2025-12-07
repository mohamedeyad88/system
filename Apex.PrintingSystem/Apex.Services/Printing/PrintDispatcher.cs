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
    public class PrintDispatcher
    {
        private readonly IPrintEngine _printEngine;
        private readonly IPrinterValidationService _validationService;
        private readonly IPrintJobLogger _logger;

        public event EventHandler<DistributionJob> OnJobStatusChanged;

        public PrintDispatcher(
            IPrintEngine printEngine, 
            IPrinterValidationService validationService,
            IPrintJobLogger logger)
        {
            _printEngine = printEngine;
            _validationService = validationService;
            _logger = logger;
        }

        public async Task<List<DistributionJob>> DispatchAsync(string filePath, List<string> printers, DistributionSettings settings)
        {
            var jobs = printers.Select(p => new DistributionJob { PrinterName = p, StartTime = DateTime.Now }).ToList();
            
            // Initial status update
            foreach (var job in jobs) NotifyStatus(job);

            if (settings.ParallelMode)
            {
                var tasks = jobs.Select(job => ProcessJobAsync(job, filePath, settings));
                await Task.WhenAll(tasks);
            }
            else
            {
                foreach (var job in jobs)
                {
                    await ProcessJobAsync(job, filePath, settings);
                }
            }

            return jobs;
        }

        private async Task ProcessJobAsync(DistributionJob job, string filePath, DistributionSettings settings)
        {
            job.Status = "Printing";
            NotifyStatus(job);

            var stopwatch = Stopwatch.StartNew();
            bool success = false;
            int attempts = 0;

            while (!success && attempts <= settings.RetryCount)
            {
                if (attempts > 0)
                {
                    job.Status = $"Retrying ({attempts}/{settings.RetryCount})...";
                    NotifyStatus(job);
                    await Task.Delay(1000 * attempts); // Backoff
                }

                try
                {
                    // 1. Check Offline Skip
                    if (settings.SkipOffline)
                    {
                        var validation = await _validationService.ValidatePrinterAsync(job.PrinterName);
                        if (!validation.IsValid && validation.Message.Contains("offline", StringComparison.OrdinalIgnoreCase))
                        {
                            job.Status = "Skipped (Offline)";
                            job.ErrorMessage = validation.Message;
                            break; // Don't retry if skipped
                        }
                    }

                    // 2. Print with Timeout
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds)))
                    {
                        // Note: PrintEngine.PrintAsync doesn't currently support cancellation token, 
                        // but we wrap it in a task that respects timeout at the caller level roughly.
                        // Ideally PrintEngine should accept CancellationToken.
                        var printTask = _printEngine.PrintAsync(job.PrinterName, filePath);
                        var completedTask = await Task.WhenAny(printTask, Task.Delay(Timeout.Infinite, cts.Token));

                        if (completedTask == printTask)
                        {
                            success = await printTask;
                            if (!success) job.ErrorMessage = "Print Engine returned failure.";
                        }
                        else
                        {
                            job.ErrorMessage = "Timeout exceeded.";
                            // We can't easily cancel the underlying print operation without support in PrintEngine,
                            // but we stop waiting.
                        }
                    }
                }
                catch (Exception ex)
                {
                    job.ErrorMessage = ex.Message;
                }

                if (success) break;
                attempts++;
                job.RetryCount = attempts;
            }

            stopwatch.Stop();
            job.TimeTaken = stopwatch.Elapsed;
            job.EndTime = DateTime.Now;
            job.Status = success ? "Completed" : "Failed";
            
            _logger.LogJob(job.PrinterName, filePath, success, success ? "Success" : job.ErrorMessage);
            NotifyStatus(job);
        }

        private void NotifyStatus(DistributionJob job)
        {
            OnJobStatusChanged?.Invoke(this, job);
        }
    }
}
