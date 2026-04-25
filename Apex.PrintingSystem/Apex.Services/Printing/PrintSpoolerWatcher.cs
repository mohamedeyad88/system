using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// Progress info from the Windows Print Spooler.
    /// </summary>
    public class SpoolerJobProgress
    {
        public string PrinterName { get; init; } = "";
        public string JobName     { get; init; } = "";
        public int    TotalPages  { get; init; }
        public int    PagesPrinted { get; init; }
        public int    PercentComplete => TotalPages > 0 ? (int)((double)PagesPrinted / TotalPages * 100) : 0;
        public string Status      { get; init; } = "";
        public uint   SpoolerJobId { get; init; }
    }

    /// <summary>
    /// 🖨️ PRINT SPOOLER WATCHER
    ///
    /// Subscribes to Windows WMI Win32_PrintJob events and reports
    /// real page-by-page progress for active print jobs.
    ///
    /// This replaces estimated/fake progress with actual Spooler data.
    ///
    /// Usage:
    ///   var watcher = PrintSpoolerWatcher.Instance;
    ///   watcher.JobProgress += (s, e) => Console.WriteLine($"{e.PagesPrinted}/{e.TotalPages}");
    ///   watcher.Start();
    ///
    /// Thread safety: Events are raised on ThreadPool threads. Marshal to UI thread as needed.
    /// </summary>
    public sealed class PrintSpoolerWatcher : IDisposable
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        private static readonly Lazy<PrintSpoolerWatcher> _instance =
            new(() => new PrintSpoolerWatcher());
        public static PrintSpoolerWatcher Instance => _instance.Value;

        // ── WMI Watchers ─────────────────────────────────────────────────────
        private ManagementEventWatcher? _modificationWatcher;
        private ManagementEventWatcher? _deletionWatcher;
        private readonly ConcurrentDictionary<uint, SpoolerJobProgress> _activeJobs = new();

        // ── State ─────────────────────────────────────────────────────────────
        private bool _isRunning;
        private bool _disposed;
        private readonly object _startLock = new();

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired whenever a tracked print job makes progress.</summary>
        public event EventHandler<SpoolerJobProgress>? JobProgress;

        /// <summary>Fired when a print job completes or is deleted from the spooler.</summary>
        public event EventHandler<SpoolerJobProgress>? JobCompleted;

        // ── Constructor ───────────────────────────────────────────────────────
        private PrintSpoolerWatcher() { }

        // ── API ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Start watching the Windows Print Spooler.
        /// Safe to call multiple times — only starts once.
        /// </summary>
        public void Start()
        {
            lock (_startLock)
            {
                if (_isRunning || _disposed) return;

                try
                {
                    StartWmiWatchers();
                    _isRunning = true;
                    Debug.WriteLine("[SpoolerWatcher] ▶️ Started — watching Win32_PrintJob events");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SpoolerWatcher] ⚠️ Failed to start WMI watcher: {ex.Message}");
                    // Non-fatal: progress will fall back to estimation
                    StartPollingFallback();
                }
            }
        }

        /// <summary>Stop the watcher.</summary>
        public void Stop()
        {
            lock (_startLock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                StopWmiWatchers();
                Debug.WriteLine("[SpoolerWatcher] ⏹️ Stopped");
            }
        }

        /// <summary>
        /// Get current progress for a printer (latest known state).
        /// Returns null if no active jobs on that printer.
        /// </summary>
        public SpoolerJobProgress? GetCurrentProgress(string printerName)
        {
            foreach (var job in _activeJobs.Values)
            {
                if (string.Equals(job.PrinterName, printerName, StringComparison.OrdinalIgnoreCase))
                    return job;
            }
            return null;
        }

        /// <summary>
        /// Perform a one-shot query to get all active spooler jobs for a printer.
        /// </summary>
        public Task<SpoolerJobProgress?> QueryCurrentJobAsync(string printerName)
        {
            return Task.Run(() =>
            {
                try
                {
                    var escapedName = printerName.Replace("'", "''");
                    var query = $"SELECT * FROM Win32_PrintJob WHERE Name LIKE '{escapedName}%'";
                    using var searcher = new ManagementObjectSearcher(query);
                    using var results = searcher.Get();

                    foreach (ManagementObject job in results)
                    {
                        return new SpoolerJobProgress
                        {
                            PrinterName   = printerName,
                            JobName       = job["Document"]?.ToString() ?? "",
                            TotalPages    = Convert.ToInt32(job["TotalPages"] ?? 0),
                            PagesPrinted  = Convert.ToInt32(job["PagesPrinted"] ?? 0),
                            Status        = job["StatusMask"]?.ToString() ?? "",
                            SpoolerJobId  = Convert.ToUInt32(job["JobId"] ?? 0u)
                        };
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SpoolerWatcher] Query failed: {ex.Message}");
                }
                return null;
            });
        }

        // ── WMI Watchers ─────────────────────────────────────────────────────

        private void StartWmiWatchers()
        {
            // Watch for job MODIFICATIONS (page count changes = progress)
            var modQuery = new WqlEventQuery(
                "__InstanceModificationEvent",
                TimeSpan.FromSeconds(1),
                "TargetInstance ISA 'Win32_PrintJob'");

            _modificationWatcher = new ManagementEventWatcher(modQuery);
            _modificationWatcher.EventArrived += OnJobModified;
            _modificationWatcher.Start();

            // Watch for job DELETION (job completed or cancelled)
            var delQuery = new WqlEventQuery(
                "__InstanceDeletionEvent",
                TimeSpan.FromSeconds(1),
                "TargetInstance ISA 'Win32_PrintJob'");

            _deletionWatcher = new ManagementEventWatcher(delQuery);
            _deletionWatcher.EventArrived += OnJobDeleted;
            _deletionWatcher.Start();
        }

        private void StopWmiWatchers()
        {
            try { _modificationWatcher?.Stop(); } catch { /* ignore */ }
            try { _deletionWatcher?.Stop(); } catch { /* ignore */ }
            _modificationWatcher?.Dispose();
            _deletionWatcher?.Dispose();
            _modificationWatcher = null;
            _deletionWatcher = null;
        }

        private void OnJobModified(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (e.NewEvent["TargetInstance"] is not ManagementObject job) return;

                var progress = ExtractProgress(job);
                _activeJobs[progress.SpoolerJobId] = progress;

                Debug.WriteLine($"[SpoolerWatcher] 📄 '{progress.PrinterName}' " +
                                $"— {progress.PagesPrinted}/{progress.TotalPages} pages " +
                                $"({progress.PercentComplete}%)");

                JobProgress?.Invoke(this, progress);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpoolerWatcher] OnJobModified error: {ex.Message}");
            }
        }

        private void OnJobDeleted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (e.NewEvent["TargetInstance"] is not ManagementObject job) return;

                var progress = ExtractProgress(job);
                _activeJobs.TryRemove(progress.SpoolerJobId, out _);

                Debug.WriteLine($"[SpoolerWatcher] ✅ '{progress.PrinterName}' job completed/removed from spooler");

                JobCompleted?.Invoke(this, progress);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpoolerWatcher] OnJobDeleted error: {ex.Message}");
            }
        }

        private static SpoolerJobProgress ExtractProgress(ManagementObject job)
        {
            // Extract printer name: Win32_PrintJob.Name format is "PrinterName, JobId"
            var nameRaw = job["Name"]?.ToString() ?? "";
            var printerName = nameRaw.Contains(',') ? nameRaw[..nameRaw.LastIndexOf(',')].Trim() : nameRaw;

            return new SpoolerJobProgress
            {
                PrinterName  = printerName,
                JobName      = job["Document"]?.ToString() ?? "",
                TotalPages   = Convert.ToInt32(job["TotalPages"] ?? 0),
                PagesPrinted = Convert.ToInt32(job["PagesPrinted"] ?? 0),
                Status       = job["Status"]?.ToString() ?? "",
                SpoolerJobId = Convert.ToUInt32(job["JobId"] ?? 0u)
            };
        }

        // ── Polling Fallback ─────────────────────────────────────────────────
        // Used when WMI event subscription is unavailable (permissions, etc.)

        private CancellationTokenSource? _pollCts;

        private void StartPollingFallback()
        {
            Debug.WriteLine("[SpoolerWatcher] ⚠️ Falling back to WMI polling (events unavailable)");
            _pollCts = new CancellationTokenSource();
            Task.Run(() => PollSpoolerAsync(_pollCts.Token));
        }

        private async Task PollSpoolerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, ct);

                    var query = "SELECT * FROM Win32_PrintJob";
                    using var searcher = new ManagementObjectSearcher(query);
                    using var results = searcher.Get();

                    var seen = new System.Collections.Generic.HashSet<uint>();

                    foreach (ManagementObject job in results)
                    {
                        var progress = ExtractProgress(job);
                        seen.Add(progress.SpoolerJobId);

                        bool isNew = !_activeJobs.ContainsKey(progress.SpoolerJobId);
                        _activeJobs[progress.SpoolerJobId] = progress;

                        if (!isNew)
                            JobProgress?.Invoke(this, progress);
                    }

                    // Remove completed jobs
                    foreach (var jobId in _activeJobs.Keys)
                    {
                        if (!seen.Contains(jobId))
                        {
                            if (_activeJobs.TryRemove(jobId, out var completed))
                                JobCompleted?.Invoke(this, completed);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SpoolerWatcher] Polling error: {ex.Message}");
                    await Task.Delay(5000, ct);
                }
            }
        }

        // ── IDisposable ───────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _pollCts?.Cancel();
            _pollCts?.Dispose();
        }
    }
}
