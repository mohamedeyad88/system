using Apex.Services.Printing.Queue;
using Apex.Services.Printing.Resilience;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.API.Hubs
{
    /// <summary>
    /// Hosted service that bridges print queue / circuit-breaker events → SignalR clients.
    /// Pushes: OnJobUpdate, OnPrinterHealth, OnStatsUpdate.
    /// </summary>
    public class PrintHubService : IHostedService
    {
        private readonly IHubContext<PrintHub> _hub;
        private readonly PrintJobQueueManager _queueManager;
        private readonly CircuitBreakerManager _circuitBreakers;
        private System.Timers.Timer? _statsTimer;

        public PrintHubService(IHubContext<PrintHub> hub)
        {
            _hub = hub;
            _queueManager = PrintJobQueueManager.Instance;
            _circuitBreakers = CircuitBreakerManager.Instance;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _queueManager.JobStateChanged += OnJobStateChanged;
            _circuitBreakers.AnyCircuitStateChanged += OnCircuitStateChanged;

            // Push aggregate stats every 5 seconds
            _statsTimer = new System.Timers.Timer(5000);
            _statsTimer.Elapsed += async (s, e) => await PushStatsAsync();
            _statsTimer.AutoReset = true;
            _statsTimer.Start();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _queueManager.JobStateChanged -= OnJobStateChanged;
            _circuitBreakers.AnyCircuitStateChanged -= OnCircuitStateChanged;

            _statsTimer?.Stop();
            _statsTimer?.Dispose();
            _statsTimer = null;

            return Task.CompletedTask;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnJobStateChanged(object? sender, PrintJob job)
        {
            var jobData = new
            {
                jobId = job.JobId,
                jobName = job.JobName,
                printerName = job.PrinterName,
                state = job.State.ToString(),
                progress = job.Progress,
                message = job.StatusMessage,
                retryCount = job.RetryCount,
                updatedAt = DateTime.Now.ToString("HH:mm:ss")
            };

            // Push to the printer-specific group and to every connected client
            _ = _hub.Clients.Group($"printer:{job.PrinterName}")
                    .SendAsync("OnJobUpdate", jobData);
            _ = _hub.Clients.All.SendAsync("OnJobUpdate", jobData);
        }

        private void OnCircuitStateChanged(object? sender, CircuitStateChangedEventArgs e)
        {
            var healthData = new
            {
                printerName = e.PrinterName,
                circuitState = e.NewState.ToString(),
                healthScore = _circuitBreakers.GetHealthScore(e.PrinterName),
                failures = e.ConsecutiveFailures,
                updatedAt = DateTime.Now.ToString("HH:mm:ss")
            };

            _ = _hub.Clients.All.SendAsync("OnPrinterHealth", healthData);
        }

        private async Task PushStatsAsync()
        {
            try
            {
                var stats = _queueManager.GetStatistics();

                double successRate = stats.TotalJobsCompleted + stats.TotalJobsFailed > 0
                    ? Math.Round((double)stats.TotalJobsCompleted /
                      (stats.TotalJobsCompleted + stats.TotalJobsFailed) * 100, 1)
                    : 100.0;

                var statsData = new
                {
                    pendingJobs = stats.QueuedJobs,
                    activeJobs = stats.ActiveJobs,
                    completedJobs = stats.TotalJobsCompleted,
                    failedJobs = stats.TotalJobsFailed,
                    successRate,
                    timestamp = DateTime.Now.ToString("HH:mm:ss")
                };

                await _hub.Clients.All.SendAsync("OnStatsUpdate", statsData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintHubService] Stats push error: {ex.Message}");
            }
        }
    }
}
