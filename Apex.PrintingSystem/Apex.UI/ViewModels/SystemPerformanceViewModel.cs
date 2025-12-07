using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Apex.UI.ViewModels
{
    public partial class SystemPerformanceViewModel : ViewModelBase, IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Process _currentProcess;

        [ObservableProperty]
        private double _cpuUsage;

        [ObservableProperty]
        private double _memoryUsageMB;

        [ObservableProperty]
        private int _threadCount;

        [ObservableProperty]
        private string _uptime = "";

        public SystemPerformanceViewModel()
        {
            Title = "System Performance";
            _currentProcess = Process.GetCurrentProcess();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
            UpdateStats();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            UpdateStats();
        }

        private void UpdateStats()
        {
            _currentProcess.Refresh();
            MemoryUsageMB = _currentProcess.WorkingSet64 / 1024.0 / 1024.0;
            ThreadCount = _currentProcess.Threads.Count;
            Uptime = (DateTime.Now - _currentProcess.StartTime).ToString(@"hh\:mm\:ss");
            
            // CPU usage is tricky to get accurately for just this process without PerformanceCounter, 
            // which might require admin rights. For now, we'll mock it or use TotalProcessorTime delta.
            CpuUsage = 0; // Placeholder
        }

        public void Dispose()
        {
            _timer.Stop();
            _currentProcess.Dispose();
        }
        
        // Fix for Title property missing in base
        public string Title { get; set; }
    }
}
