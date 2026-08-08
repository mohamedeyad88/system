using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// Represents a single entry in the Recent Activity log.
    /// </summary>
    public class ActivityLogEntry
    {
        public string Message { get; init; } = "";
        public string Time { get; init; } = "";
    }

    public partial class SystemPerformanceViewModel : ViewModelBase, IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Process _currentProcess;
        private TimeSpan _lastCpuTime = TimeSpan.Zero;
        private DateTime _lastMeasureAt = DateTime.MinValue;

        // ── Live stats ───────────────────────────────────────────────────────
        [ObservableProperty] private double _cpuUsage;
        [ObservableProperty] private double _memoryUsageMB;
        [ObservableProperty] private int _threadCount;
        [ObservableProperty] private string _uptime = "";

        // ── System Info (bound in SystemPerformanceView.xaml) ────────────────
        public string OsVersion => RuntimeInformation.OSDescription;
        public string DotNetVersion => RuntimeInformation.FrameworkDescription;
        public string AppVersion => Assembly.GetEntryAssembly()
                                              ?.GetName()
                                              .Version
                                              ?.ToString(3) ?? "1.0.0";
        public int ProcessId => _currentProcess.Id;

        // ── Recent Activity feed ─────────────────────────────────────────────
        public ObservableCollection<ActivityLogEntry> RecentActivities { get; } = new();

        // Fix for Title property missing in base
        public string Title { get; set; } = "System Performance";

        public SystemPerformanceViewModel()
        {
            _currentProcess = Process.GetCurrentProcess();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
            UpdateStats();

            // Seed with startup entry
            PushActivity("تم تشغيل مراقب الأداء");
        }

        private void Timer_Tick(object? sender, EventArgs e) => UpdateStats();

        private void UpdateStats()
        {
            _currentProcess.Refresh();
            MemoryUsageMB = _currentProcess.WorkingSet64 / 1024.0 / 1024.0;
            ThreadCount = _currentProcess.Threads.Count;
            Uptime = (DateTime.Now - _currentProcess.StartTime).ToString(@"hh\:mm\:ss");

            // CPU: TotalProcessorTime delta / wall-time delta / logical-core count × 100
            var now = DateTime.UtcNow;
            var cpuNow = _currentProcess.TotalProcessorTime;
            if (_lastMeasureAt != DateTime.MinValue)
            {
                double wallSec = (now - _lastMeasureAt).TotalSeconds;
                double cpuSec = (cpuNow - _lastCpuTime).TotalSeconds;
                if (wallSec > 0)
                    CpuUsage = Math.Round(
                        Math.Min(100.0, cpuSec / wallSec / Environment.ProcessorCount * 100.0), 1);
            }
            _lastCpuTime = cpuNow;
            _lastMeasureAt = now;

            // Log high-CPU events to the activity feed
            if (CpuUsage > 80)
                PushActivity($"تحذير: استخدام المعالج {CpuUsage:F1}%");
        }

        /// <summary>Adds a timestamped entry to the activity log (max 50 entries).</summary>
        public void PushActivity(string message)
        {
            if (RecentActivities.Count >= 50)
                RecentActivities.RemoveAt(RecentActivities.Count - 1);
            RecentActivities.Insert(0, new ActivityLogEntry
            {
                Message = message,
                Time = DateTime.Now.ToString("HH:mm:ss")
            });
        }

        public void Dispose()
        {
            _timer.Stop();
            _currentProcess.Dispose();
        }
    }
}
