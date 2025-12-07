using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Apex.UI.ViewModels
{
    public partial class PrinterDiagnosticsViewModel : ViewModelBase
    {
        private readonly IRepository<Printer> _printerRepository;
        private readonly ILoggerService _logger;
        private readonly PrinterMonitoringService _monitoringService; // Use concrete type to access specific methods/events
        private readonly DispatcherTimer _timer;

        [ObservableProperty]
        private ObservableCollection<PrinterStatusInfo> _printers = new();

        [ObservableProperty]
        private ObservableCollection<DiskInfo> _drives = new();

        [ObservableProperty]
        private string _systemStatus = "Healthy";

        public PrinterDiagnosticsViewModel(
            IRepository<Printer> printerRepository, 
            ILoggerService logger, 
            PrinterMonitoringService monitoringService)
        {
            Title = "System Diagnostics";
            _printerRepository = printerRepository;
            _logger = logger;
            _monitoringService = monitoringService;

            _monitoringService.PrinterStatusChanged += OnPrinterStatusChanged;
            
            // Auto refresh
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (s, e) =>
            {
                try { RefreshSystemInfo(); }
                catch (ObjectDisposedException) { _timer.Stop(); }
                catch { /* Ignore */ }
            };
            _timer.Start();
        }

        public override async Task InitializeAsync()
        {
            await LoadPrintersAsync();
            RefreshSystemInfo();
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            await LoadPrintersAsync();
            RefreshSystemInfo();
        }

        [RelayCommand]
        public void OpenLogViewer()
        {
             // This replicates the logic in MainViewModel for convenience, 
             // relying on DI to handle the Window creation if we were to do it properly.
             // But since specific Window logic is tied to View layer, we'll implement a simple opener here 
             // or assume MainViewModel handles it via a shared message or service.
             // For now, simpler: user likely navigates via Sidebar for Logs, 
             // but we will add specific button command here if needed.
             // Actually, let's just trigger a lightweight log status check here.
        }

        private async Task LoadPrintersAsync()
        {
            var dbPrinters = await _printerRepository.GetAllAsync();
            
            // We want to merge DB info with Live Monitor info
            // If already populating, update existing items
            
            // Identify stale
            var dbIds = dbPrinters.Select(p => p.Name).ToHashSet();
            var toRemove = Printers.Where(p => !dbIds.Contains(p.Name)).ToList();
            foreach(var r in toRemove) Printers.Remove(r);

            foreach (var p in dbPrinters)
            {
                var vm = Printers.FirstOrDefault(x => x.Name == p.Name);
                if (vm == null)
                {
                    vm = new PrinterStatusInfo { Name = p.Name };
                    Printers.Add(vm);
                }

                // Get latest status from service
                var status = _monitoringService.GetCurrentStatus(p.Name);
                if (status != null)
                {
                    UpdatePrinterInfo(vm, status);
                }
                else
                {
                    // Fallback to DB
                    vm.Status = p.Status; 
                }
            }
        }

        private void OnPrinterStatusChanged(object? sender, PrinterStatusEventArgs e)
        {
             Application.Current.Dispatcher.Invoke(() => 
             {
                 var vm = Printers.FirstOrDefault(p => p.Name == e.PrinterName);
                 if (vm != null) UpdatePrinterInfo(vm, e);
             });
        }

        private void UpdatePrinterInfo(PrinterStatusInfo vm, PrinterStatusEventArgs e)
        {
            vm.Status = e.Status;
            vm.IsOffline = e.IsOffline;
            vm.HasError = e.HasError;
            vm.QueueLength = e.QueueLength;
        }

        private void RefreshSystemInfo()
        {
            // Update Drives
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            
            Drives.Clear(); // Simple clear/add for drives as they rarely change
            bool lowSpace = false;

            foreach (var d in drives)
            {
                // Warn if < 10%
                double percent = 0;
                if (d.TotalSize > 0) percent = (double)d.TotalFreeSpace / d.TotalSize;
                
                bool isLow = percent < 0.10;
                if (isLow) lowSpace = true;

                Drives.Add(new DiskInfo
                {
                    Name = d.Name,
                    Label = d.VolumeLabel,
                    TotalSizeGb = d.TotalSize / 1024d / 1024d / 1024d,
                    FreeSpaceGb = d.TotalFreeSpace / 1024d / 1024d / 1024d,
                    UsagePercent = 100 - (percent * 100),
                    IsLowSpace = isLow
                });
            }

            SystemStatus = lowSpace ? "Warning: Low Disk Space" : "Healthy";
        }
        
        public string Title { get; set; }
    }

    public partial class PrinterStatusInfo : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _status = "Unknown";
        [ObservableProperty] private bool _isOffline;
        [ObservableProperty] private bool _hasError;
        [ObservableProperty] private int _queueLength;
    }

    public class DiskInfo
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public double TotalSizeGb { get; set; }
        public double FreeSpaceGb { get; set; }
        public double UsagePercent { get; set; }
        public bool IsLowSpace { get; set; }
        
        // Formatted
        public string UsageText => $"{UsagePercent:F1}% Used ({FreeSpaceGb:F0} GB Free)";
    }
}
