using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Apex.Core.Interfaces;
using System;
using System.Linq;
using System.Windows.Threading;
using System.Windows;
using Apex.Services.Printing.Queue;
using Apex.Services.Printing.Resilience;
using PrintJobState = Apex.Services.Printing.Queue.PrintJobState;
using QueuePrintJob = Apex.Services.Printing.Queue.PrintJob;

namespace Apex.UI.ViewModels
{
    public partial class DashboardViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _printerDiscoveryService;
        private readonly DispatcherTimer _refreshTimer;
        private readonly PrintJobQueueManager _queueManager = PrintJobQueueManager.Instance;
        private readonly CircuitBreakerManager _circuitBreakers = CircuitBreakerManager.Instance;

        // ── Counters tracked from live events (reset at session start) ──
        private int _jobsCompletedSession;
        private int _jobsFailedSession;
        private readonly DateTime _sessionStart = DateTime.Now;
        private readonly object _counterLock = new();

        // ── Printer Statistics ────────────────────────────────────────
        [ObservableProperty] private int _totalPrinters;
        [ObservableProperty] private int _onlinePrinters;
        [ObservableProperty] private int _offlinePrinters;
        [ObservableProperty] private int _busyPrinters;

        // ── Print Job Statistics ──────────────────────────────────────
        [ObservableProperty] private int _todayJobs;
        [ObservableProperty] private int _pendingJobs;
        [ObservableProperty] private int _completedJobs;
        [ObservableProperty] private int _failedJobs;
        [ObservableProperty] private int _activeJobs;

        // ── System Status ─────────────────────────────────────────────
        [ObservableProperty] private string _systemStatus = "Ready";
        [ObservableProperty] private bool _isSystemOnline = true;
        [ObservableProperty] private string _lastRefreshTime = DateTime.Now.ToString("HH:mm:ss");

        // ── Queue Statistics ──────────────────────────────────────────
        [ObservableProperty] private double _successRate = 100.0;
        [ObservableProperty] private string _successRateText = "100%";
        [ObservableProperty] private int _totalPagesSession_;

        // ── Recent Activity ───────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<ActivityItem> _recentActivity = new();

        // ── Printer List for Dashboard ────────────────────────────────
        [ObservableProperty] private ObservableCollection<DashboardPrinterInfo> _printers = new();

        // ── Quick Stats ───────────────────────────────────────────────
        [ObservableProperty] private string _topPrinter = "N/A";
        [ObservableProperty] private double _averageJobsPerDay;

        public DashboardViewModel(IPrinterDiscoveryService printerDiscoveryService)
        {
            _printerDiscoveryService = printerDiscoveryService;

            // Subscribe to real queue events
            _queueManager.JobStateChanged += OnJobStateChanged;

            // Subscribe to circuit breaker state changes
            _circuitBreakers.AnyCircuitStateChanged += OnCircuitStateChanged;

            // Refresh printer list every 15 seconds
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _refreshTimer.Tick += async (s, e) => await RefreshPrintersAsync();
            _refreshTimer.Start();
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await RefreshData();
        }

        // ── Event Handlers ────────────────────────────────────────────

        private void OnJobStateChanged(object? sender, QueuePrintJob job)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                // Update live counters
                switch (job.State)
                {
                    case PrintJobState.Completed:
                        lock (_counterLock)
                        {
                            _jobsCompletedSession++;
                        }
                        AddActivity($"{job.JobName}", L("Dash_PrintDone"), "✅", "Success");
                        break;

                    case PrintJobState.Failed:
                        lock (_counterLock)
                        {
                            _jobsFailedSession++;
                        }
                        AddActivity($"{job.JobName}", Lf("Dash_FailedOn", job.PrinterName), "❌", "Error");
                        break;

                    case PrintJobState.Sending:
                        AddActivity($"{job.JobName}", Lf("Dash_SendingTo", job.PrinterName), "📤", "Info");
                        break;

                    case PrintJobState.Retrying:
                        AddActivity($"{job.JobName}", Lf("Dash_Retry", job.RetryCount + 1), "🔄", "Warning");
                        break;
                }

                // Refresh stats display
                RefreshJobStats();
            });
        }

        private void OnCircuitStateChanged(object? sender, Apex.Services.Printing.Resilience.CircuitStateChangedEventArgs e)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                switch (e.NewState)
                {
                    case Apex.Services.Printing.Resilience.CircuitState.Open:
                        AddActivity(e.PrinterName, Lf("Dash_PrinterDown", e.ConsecutiveFailures), "⛔", "Error");
                        break;
                    case Apex.Services.Printing.Resilience.CircuitState.Closed when e.OldState == Apex.Services.Printing.Resilience.CircuitState.HalfOpen:
                        AddActivity(e.PrinterName, L("Dash_PrinterRecovered"), "✅", "Success");
                        break;
                    case Apex.Services.Printing.Resilience.CircuitState.HalfOpen:
                        AddActivity(e.PrinterName, L("Dash_TestingRecovery"), "🔄", "Warning");
                        break;
                }

                // Refresh printer health scores
                RefreshPrinterHealthScores();
            });
        }

        // ── Commands ──────────────────────────────────────────────────

        [RelayCommand]
        private async Task RefreshData()
        {
            await RefreshPrintersAsync();
            RefreshJobStats();
            LastRefreshTime = DateTime.Now.ToString("HH:mm:ss");
        }

        private async Task RefreshPrintersAsync()
        {
            try
            {
                var printers = await _printerDiscoveryService.ScanAsync();
                var printerList = printers.ToList();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TotalPrinters = printerList.Count;
                    OnlinePrinters = printerList.Count(p => p.IsOnline);
                    OfflinePrinters = printerList.Count(p => !p.IsOnline);
                    BusyPrinters = printerList.Count(p => p.QueueLength > 0);

                    IsSystemOnline = OnlinePrinters > 0;
                    SystemStatus = IsSystemOnline ? L("Dash_SystemRunning") : L("Dash_NoPrinters");

                    // Rebuild printer list with real health scores
                    Printers.Clear();
                    foreach (var printer in printerList.Take(6))
                    {
                        int healthScore = _circuitBreakers.GetHealthScore(printer.Name);
                        var circuitState = _circuitBreakers.GetState(printer.Name);

                        string statusText;
                        if (!printer.IsOnline)
                            statusText = L("Dash_Offline");
                        else if (circuitState == Apex.Services.Printing.Resilience.CircuitState.Open)
                            statusText = L("Dash_Disabled");
                        else if (circuitState == Apex.Services.Printing.Resilience.CircuitState.HalfOpen)
                            statusText = L("Dash_Recovering");
                        else if (printer.QueueLength > 0)
                            statusText = Lf("Dash_Printing", printer.QueueLength);
                        else
                            statusText = L("Num_Ready");

                        Printers.Add(new DashboardPrinterInfo
                        {
                            Name = printer.Name,
                            IsOnline = printer.IsOnline && circuitState != Apex.Services.Printing.Resilience.CircuitState.Open,
                            QueueLength = printer.QueueLength,
                            Type = printer.Type,
                            StatusText = statusText,
                            HealthScore = printer.IsOnline ? healthScore : 0,
                            CircuitState = circuitState.ToString()
                        });
                    }

                    // Top printer = most active queue
                    if (printerList.Any())
                        TopPrinter = printerList.OrderByDescending(p => p.QueueLength).First().Name;

                    LastRefreshTime = DateTime.Now.ToString("HH:mm:ss");
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SystemStatus = Lf("Num_ErrorColon", ex.Message);
                    IsSystemOnline = false;
                });
            }
        }

        private void RefreshJobStats()
        {
            // Get live queue statistics
            var stats = _queueManager.GetStatistics();

            int completed, failed;
            lock (_counterLock)
            {
                completed = _jobsCompletedSession;
                failed = _jobsFailedSession;
            }

            PendingJobs = stats.QueuedJobs;
            ActiveJobs = stats.ActiveJobs;
            CompletedJobs = completed;
            FailedJobs = failed;
            TodayJobs = completed + failed + stats.ActiveJobs + stats.QueuedJobs;

            // Success rate — a dash until something has actually run.
            //
            // With no jobs the old code reported 100%, which reads as a measurement
            // and is not one. On a fresh install the dashboard claimed a perfect
            // record before the shop had printed a single page.
            long total = stats.TotalJobsCompleted + stats.TotalJobsFailed;
            if (total > 0)
            {
                SuccessRate = Math.Round((double)stats.TotalJobsCompleted / total * 100, 1);
                SuccessRateText = $"{SuccessRate:F1}%";
            }
            else
            {
                SuccessRate = 0;
                SuccessRateText = "—";
            }

            // Average (session hours → jobs/hour)
            double hoursElapsed = (DateTime.Now - _sessionStart).TotalHours;
            AverageJobsPerDay = hoursElapsed > 0.01
                ? Math.Round(TodayJobs / hoursElapsed * 8, 1)  // extrapolate to 8-hour day
                : TodayJobs;
        }

        private void RefreshPrinterHealthScores()
        {
            foreach (var p in Printers)
            {
                p.HealthScore = p.IsOnline ? _circuitBreakers.GetHealthScore(p.Name) : 0;
            }
        }

        // ── Activity Log ──────────────────────────────────────────────

        private void AddActivity(string subject, string description, string icon, string type)
        {
            // Keep max 20 recent activities
            if (RecentActivity.Count >= 20)
                RecentActivity.RemoveAt(RecentActivity.Count - 1);

            RecentActivity.Insert(0, new ActivityItem
            {
                Time = DateTime.Now.ToString("HH:mm"),
                Subject = subject,
                Description = description,
                Type = type,
                Icon = icon
            });
        }

        // ── Cleanup ───────────────────────────────────────────────────
        public void Dispose()
        {
            _refreshTimer.Stop();
            _queueManager.JobStateChanged -= OnJobStateChanged;
            _circuitBreakers.AnyCircuitStateChanged -= OnCircuitStateChanged;
        }
    }

    // ── Supporting models ─────────────────────────────────────────────

    public class ActivityItem
    {
        public string Time { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "Info";
        public string Icon { get; set; } = "•";

        /// <summary>Combined text for display: "Subject — Description"</summary>
        public string DisplayText => string.IsNullOrEmpty(Subject)
            ? Description
            : $"{Subject} — {Description}";
    }

    public class DashboardPrinterInfo : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public bool IsOnline { get; set; }
        public int QueueLength { get; set; }
        public string Type { get; set; } = "Local";
        public string StatusText { get; set; } = "";
        public string CircuitState { get; set; } = "Closed";

        private int _healthScore = 100;
        public int HealthScore
        {
            get => _healthScore;
            set
            {
                if (_healthScore != value)
                {
                    _healthScore = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HealthScore)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HealthColor)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HealthText)));
                }
            }
        }

        /// <summary>Color string for health score display.</summary>
        public string HealthColor =>
            HealthScore >= 80 ? "#22C55E" :
            HealthScore >= 50 ? "#F59E0B" : "#EF4444";

        /// <summary>Short health label.</summary>
        public string HealthText =>
            HealthScore >= 80 ? ViewModelBase.L("Dash_Good") :
            HealthScore >= 50 ? ViewModelBase.L("Dash_Medium") : ViewModelBase.L("Dash_Weak");

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
