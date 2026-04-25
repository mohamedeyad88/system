using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using Apex.Core.Models;
using Apex.Core.Interfaces;
using Apex.Services.Printing;
using Apex.Services.Printing.Queue;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Timer = System.Timers.Timer;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// ViewModel for Printer Monitor - displays live status of all printers
    /// </summary>
    public partial class DistributionViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _discoveryService;
        private readonly Apex.Services.Printing.PrintJobsManager _printJobsManager;
        private readonly Timer _statusRefreshTimer;

        // Printers list with live status
        [ObservableProperty] private ObservableCollection<PrinterStatusItem> _availablePrinters = new();
        
        // Print Jobs (embedded monitoring)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPrintJobs))]
        [NotifyPropertyChangedFor(nameof(PrintJobsCount))]
        private ObservableCollection<PrintJobItemViewModel> _activeJobs = new();
        
        public bool HasPrintJobs => ActiveJobs.Count > 0;
        public int PrintJobsCount => ActiveJobs.Count;

        // Statistics
        [ObservableProperty] private int _totalPrinters;
        [ObservableProperty] private int _onlinePrinters;
        [ObservableProperty] private int _offlinePrinters;
        [ObservableProperty] private int _busyPrinters;
        [ObservableProperty] private DateTime _lastUpdated = DateTime.Now;
        [ObservableProperty] private bool _hasNoPrinters;
        [ObservableProperty] private bool _isRefreshing;

        public DistributionViewModel(IPrinterDiscoveryService discoveryService)
        {
            _discoveryService = discoveryService;
            _printJobsManager = Apex.Services.Printing.PrintJobsManager.Instance;

            // Setup auto-refresh timer (every 5 seconds)
            _statusRefreshTimer = new Timer(5000);
            _statusRefreshTimer.Elapsed += async (s, e) => await RefreshPrinterStatusAsync();
            _statusRefreshTimer.AutoReset = true;

            // Subscribe to printer updates from discovery service
            _discoveryService.PrintersUpdated += (s, printers) =>
            {
                Application.Current.Dispatcher.Invoke(() => UpdatePrinterList(printers));
            };
            
            // Subscribe to print jobs events (passive observer - NO DIALOGS)
            _printJobsManager.JobCreated += OnPrintJobCreated;
            _printJobsManager.JobProgress += OnPrintJobProgress;
            _printJobsManager.JobSubmittedToSpooler += OnPrintJobSubmittedToSpooler;
            _printJobsManager.JobCompleted += OnPrintJobCompleted;
            _printJobsManager.JobFailed += OnPrintJobFailed; // NO DIALOGS - errors inline
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await LoadPrintersAsync();
            _statusRefreshTimer.Start();
            _discoveryService.StartMonitoring();
        }

        private async Task LoadPrintersAsync()
        {
            try
            {
                IsRefreshing = true;
                var printers = await _discoveryService.ScanAsync();
                UpdatePrinterList(printers);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading printers: {ex.Message}");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private void UpdatePrinterList(IEnumerable<PrinterInfo> printers)
        {
            var printerList = printers.ToList();
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                AvailablePrinters.Clear();
                
                foreach (var printer in printerList)
                {
                    AvailablePrinters.Add(new PrinterStatusItem
                    {
                        Name = printer.Name,
                        Status = printer.StatusText,
                        IsOnline = printer.IsOnline,
                        QueueLength = printer.QueueLength,
                        Type = printer.Type
                    });
                }

                // Update statistics
                UpdateStatistics();
                LastUpdated = DateTime.Now;
            });
        }

        private void UpdateStatistics()
        {
            TotalPrinters = AvailablePrinters.Count;
            OnlinePrinters = AvailablePrinters.Count(p => p.Status == "Ready" || p.IsOnline);
            OfflinePrinters = AvailablePrinters.Count(p => p.Status == "Offline" || !p.IsOnline);
            BusyPrinters = AvailablePrinters.Count(p => p.Status == "Busy" || p.Status == "Printing");
            HasNoPrinters = TotalPrinters == 0;
        }

        private async Task RefreshPrinterStatusAsync()
        {
            try
            {
                var printers = await _discoveryService.ScanAsync();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Update existing printers status
                    foreach (var printer in printers)
                    {
                        var existing = AvailablePrinters.FirstOrDefault(p => p.Name == printer.Name);
                        if (existing != null)
                        {
                            existing.Status = printer.StatusText;
                            existing.IsOnline = printer.IsOnline;
                            existing.QueueLength = printer.QueueLength;
                        }
                        else
                        {
                            // New printer detected
                            AvailablePrinters.Add(new PrinterStatusItem
                            {
                                Name = printer.Name,
                                Status = printer.StatusText,
                                IsOnline = printer.IsOnline,
                                QueueLength = printer.QueueLength,
                                Type = printer.Type
                            });
                        }
                    }

                    // Remove printers that no longer exist
                    var currentNames = printers.Select(p => p.Name).ToHashSet();
                    var toRemove = AvailablePrinters.Where(p => !currentNames.Contains(p.Name)).ToList();
                    foreach (var item in toRemove)
                    {
                        AvailablePrinters.Remove(item);
                    }

                    UpdateStatistics();
                    LastUpdated = DateTime.Now;
                });
            }
            catch
            {
                // Ignore refresh errors - will try again next interval
            }
        }

        [RelayCommand]
        private async Task Refresh()
        {
            await LoadPrintersAsync();
        }

        [RelayCommand]
        private void OpenProperties(string printerName)
        {
            if (string.IsNullOrEmpty(printerName)) return;

            try
            {
                // Open Windows printer properties dialog
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"printui.dll,PrintUIEntry /p /n \"{printerName}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                // NO DIALOGS - Log error only
                System.Diagnostics.Debug.WriteLine($"Could not open printer properties: {ex.Message}");
            }
        }

        [RelayCommand]
        private void OpenPreferences(string printerName)
        {
            if (string.IsNullOrEmpty(printerName)) return;

            try
            {
                // Open Windows printer preferences dialog (printing preferences)
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"printui.dll,PrintUIEntry /e /n \"{printerName}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                // NO DIALOGS - Log error only
                System.Diagnostics.Debug.WriteLine($"Could not open printer preferences: {ex.Message}");
            }
        }
        
        #region Print Jobs Event Handlers (Passive Observer - NO DIALOGS)
        
        private void OnPrintJobCreated(object? sender, Apex.Services.Printing.PrintJobTracking tracking)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = new PrintJobItemViewModel(tracking);
                ActiveJobs.Add(item);
                OnPropertyChanged(nameof(HasPrintJobs));
                OnPropertyChanged(nameof(PrintJobsCount));
            });
        }
        
        private void OnPrintJobProgress(object? sender, Apex.Services.Printing.PrintJobTracking tracking)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = ActiveJobs.FirstOrDefault(j => j.JobId == tracking.JobId);
                if (item != null)
                {
                    item.UpdateFrom(tracking);
                }
            });
        }
        
        private void OnPrintJobSubmittedToSpooler(object? sender, Apex.Services.Printing.PrintJobTracking tracking)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = ActiveJobs.FirstOrDefault(j => j.JobId == tracking.JobId);
                if (item != null)
                {
                    item.UpdateFrom(tracking);
                }
            });
        }
        
        private void OnPrintJobCompleted(object? sender, Apex.Services.Printing.PrintJobTracking tracking)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = ActiveJobs.FirstOrDefault(j => j.JobId == tracking.JobId);
                if (item != null)
                {
                    item.UpdateFrom(tracking);
                }
            });
        }
        
        private void OnPrintJobFailed(object? sender, Apex.Services.Printing.PrintJobTracking tracking)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = ActiveJobs.FirstOrDefault(j => j.JobId == tracking.JobId);
                if (item != null)
                {
                    item.UpdateFrom(tracking);
                }
                // NO DIALOGS - Error shown inline in panel
            });
        }
        
        #endregion
    }

    /// <summary>
    /// ViewModel for print job item display.
    /// </summary>
    public partial class PrintJobItemViewModel : ObservableObject
    {
        public string JobId { get; }

        [ObservableProperty]
        private string _fileName = string.Empty;

        [ObservableProperty]
        private string _printerName = string.Empty;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private string _statusColor = "#64748B";

        [ObservableProperty]
        private int _progress;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private string _scaleMode = string.Empty;

        public PrintJobItemViewModel(Apex.Services.Printing.PrintJobTracking tracking)
        {
            JobId = tracking.JobId;
            UpdateFrom(tracking);
        }

        public void UpdateFrom(Apex.Services.Printing.PrintJobTracking tracking)
        {
            FileName = System.IO.Path.GetFileName(tracking.FilePath);
            PrinterName = tracking.PrinterName;

            StatusText = tracking.State.ToString();
            var queueState = tracking.State;
            StatusColor = queueState switch
            {
                Apex.Services.Printing.Queue.PrintJobState.Queued => "#64748B",
                Apex.Services.Printing.Queue.PrintJobState.Preparing => "#F59E0B",
                Apex.Services.Printing.Queue.PrintJobState.Sending => "#3B82F6",
                Apex.Services.Printing.Queue.PrintJobState.Printing => "#10B981",
                Apex.Services.Printing.Queue.PrintJobState.Completed => "#10B981",
                Apex.Services.Printing.Queue.PrintJobState.Failed => "#DC2626",
                _ => "#64748B"
            };

            Progress = tracking.Progress;
            IsRunning = queueState == Apex.Services.Printing.Queue.PrintJobState.Queued ||
                       queueState == Apex.Services.Printing.Queue.PrintJobState.Preparing ||
                       queueState == Apex.Services.Printing.Queue.PrintJobState.Sending ||
                       queueState == Apex.Services.Printing.Queue.PrintJobState.Printing;

            if (tracking.ScaleMode.HasValue)
            {
                ScaleMode = tracking.ScaleMode.Value switch
                {
                    Apex.NumberedBooksEngine.Core.PrintScaleMode.ActualSize => "Scale: Actual Size (100%)",
                    Apex.NumberedBooksEngine.Core.PrintScaleMode.FitToPage => "Scale: Fit to Page",
                    _ => string.Empty
                };
            }
            else
            {
                ScaleMode = string.Empty;
            }

            HasError = queueState == Apex.Services.Printing.Queue.PrintJobState.Failed;
            if (HasError && tracking.Lifecycle.FailureInfo != null)
            {
                ErrorMessage = tracking.Lifecycle.FailureInfo.GetActionableMessage();
            }
        }
    }

    /// <summary>
    /// Printer status item for display in the monitor grid
    /// </summary>
    public partial class PrinterStatusItem : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _status = "Unknown";
        [ObservableProperty] private bool _isOnline;
        [ObservableProperty] private int _queueLength;
        [ObservableProperty] private string _type = "Local";
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPrintJobs))]
        private ObservableCollection<PrintJobItemViewModel> _printJobs = new();
        
        public bool HasPrintJobs => PrintJobs.Count > 0;
    }
}
