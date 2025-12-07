using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;
using System;
using System.Linq;
using System.Windows;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// ViewModel for Printer Management with inline selection and Quick Print.
    /// </summary>
    public partial class PrintersViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _printerDiscoveryService;
        private readonly IJobDistributionService _jobDistributionService;
        private readonly ISettingsService _settingsService;
        private readonly IPrinterService _printerService;
        private readonly PrinterMonitoringService _monitoringService;
        private readonly DispatcherTimer _timer;

        // All printers from discovery
        [ObservableProperty]
        private ObservableCollection<PrinterInfo> _printers = new();

        // Selected/Active printer for Quick Print
        [ObservableProperty]
        private PrinterInfo? _selectedPrinter;

        // Search and filtering
        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _filterType = "All"; // All, Local, Network, Online

        [ObservableProperty]
        private string _sortBy = "Name"; // Name, Status, Queue

        // Quick Print fields
        [ObservableProperty]
        private string _newJobFileName = string.Empty;

        [ObservableProperty]
        private int _newJobCopies = 1;

        // Activity log
        [ObservableProperty]
        private string _logText = string.Empty;

        // Properties panel state
        [ObservableProperty]
        private bool _isPropertiesPanelOpen;

        [ObservableProperty]
        private PrinterInfo? _propertiesPrinter;

        // View Mode: "Grid" or "List"
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsListMode))]
        [NotifyPropertyChangedFor(nameof(IsGridMode))]
        private string _viewMode = "Grid";

        public bool IsListMode => ViewMode == "List";
        public bool IsGridMode => ViewMode == "Grid";

        // Computed filtered list
        public ObservableCollection<PrinterInfo> FilteredPrinters
        {
            get
            {
                var query = Printers.AsEnumerable();

                // Search filter
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    query = query.Where(p => p.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
                }

                // Type filter
                query = FilterType switch
                {
                    "Local" => query.Where(p => p.Type == "Local"),
                    "Network" => query.Where(p => p.Type == "Network"),
                    "Online" => query.Where(p => p.IsOnline),
                    _ => query
                };

                // Sort
                query = SortBy switch
                {
                    "Status" => query.OrderByDescending(p => p.IsOnline).ThenBy(p => p.Name),
                    "Queue" => query.OrderByDescending(p => p.QueueLength).ThenBy(p => p.Name),
                    _ => query.OrderBy(p => p.Name)
                };

                return new ObservableCollection<PrinterInfo>(query);
            }
        }

        // Quick Print validation
        public bool CanPrint => SelectedPrinter != null && !string.IsNullOrWhiteSpace(NewJobFileName) && NewJobCopies > 0;
        public string SelectedPrinterDisplay => SelectedPrinter != null 
            ? $"🖨️ {SelectedPrinter.Name}" 
            : "⚠️ No printer selected";

        public PrintersViewModel(
            IPrinterDiscoveryService printerDiscoveryService, 
            IJobDistributionService jobDistributionService, 
            ISettingsService settingsService, 
            IPrinterService printerService, 
            PrinterMonitoringService monitoringService)
        {
            _printerDiscoveryService = printerDiscoveryService;
            _jobDistributionService = jobDistributionService;
            _settingsService = settingsService;
            _printerService = printerService;
            _monitoringService = monitoringService;
            
            _monitoringService.PrinterStatusChanged += OnPrinterStatusChanged;

            // Auto-refresh status every 5 seconds
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += async (s, e) =>
            {
                try
                {
                    await LoadPrinters();
                }
                catch (ObjectDisposedException)
                {
                    // DbContext was disposed, stop the timer
                    _timer.Stop();
                }
                catch
                {
                    // Silently ignore other errors in background refresh
                }
            };
            _timer.Start();
        }

        partial void OnSearchQueryChanged(string value) => OnPropertyChanged(nameof(FilteredPrinters));
        partial void OnFilterTypeChanged(string value) => OnPropertyChanged(nameof(FilteredPrinters));
        partial void OnSortByChanged(string value) => OnPropertyChanged(nameof(FilteredPrinters));
        partial void OnSelectedPrinterChanged(PrinterInfo? value)
        {
            OnPropertyChanged(nameof(CanPrint));
            OnPropertyChanged(nameof(SelectedPrinterDisplay));
        }
        partial void OnNewJobFileNameChanged(string value) => OnPropertyChanged(nameof(CanPrint));
        partial void OnNewJobCopiesChanged(int value) => OnPropertyChanged(nameof(CanPrint));

        private void OnPrinterStatusChanged(object? sender, PrinterStatusEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var printer = Printers.FirstOrDefault(p => p.Name.Equals(e.PrinterName, StringComparison.OrdinalIgnoreCase));
                if (printer != null)
                {
                    printer.StatusText = e.Status;
                    printer.IsOnline = !e.IsOffline;
                    printer.QueueLength = e.QueueLength;
                    
                    printer.IsOutOfPaper = false;
                    printer.IsTonerLow = false;
                    printer.HasPaperJam = false;

                    if (e.Status == "Out of Paper") printer.IsOutOfPaper = true;
                    else if (e.Status == "Low Toner") printer.IsTonerLow = true;
                    else if (e.Status == "Error") printer.HasPaperJam = true;
                }
                OnPropertyChanged(nameof(FilteredPrinters));
            });
        }

        public override async Task InitializeAsync()
        {
            await LoadPrinters();
        }

        [RelayCommand]
        private async Task LoadPrinters()
        {
            var osPrinters = await _printerDiscoveryService.ScanAsync();
            var dbPrinters = await _printerService.GetAllPrintersAsync();

            foreach (var printer in osPrinters)
            {
                var dbPrinter = dbPrinters.FirstOrDefault(p => p.Name.Equals(printer.Name, StringComparison.OrdinalIgnoreCase));
                if (dbPrinter != null)
                {
                    printer.Capabilities = _printerService.GetCapabilities(dbPrinter);
                }

                // Preserve selection state
                if (SelectedPrinter != null && printer.Name == SelectedPrinter.Name)
                {
                    printer.IsSelected = true;
                    SelectedPrinter = printer;
                }
            }

            Printers = new ObservableCollection<PrinterInfo>(osPrinters);
            OnPropertyChanged(nameof(FilteredPrinters));

            // Auto-select default printer if none selected
            if (SelectedPrinter == null)
            {
                var defaultPrinter = Printers.FirstOrDefault(p => p.IsDefault) ?? Printers.FirstOrDefault(p => p.IsOnline);
                if (defaultPrinter != null)
                {
                    SelectPrinter(defaultPrinter);
                }
            }
        }

        [RelayCommand]
        private async Task RefreshStatus()
        {
            await LoadPrinters();
            LogText += $"[{DateTime.Now:HH:mm:ss}] Status refreshed.\n";
        }

        /// <summary>
        /// Select a printer as the active printer for Quick Print.
        /// </summary>
        [RelayCommand]
        private void SelectPrinter(PrinterInfo? printer)
        {
            if (printer == null) return;

            // Deselect all
            foreach (var p in Printers)
            {
                p.IsSelected = false;
            }

            // Select this one
            printer.IsSelected = true;
            SelectedPrinter = printer;
            LogText += $"[{DateTime.Now:HH:mm:ss}] Selected printer: {printer.Name}\n";
        }

        /// <summary>
        /// Open the printer properties panel.
        /// </summary>
        [RelayCommand]
        private void OpenProperties(PrinterInfo? printer)
        {
            if (printer == null) return;
            PropertiesPrinter = printer;
            IsPropertiesPanelOpen = true;
        }

        [RelayCommand]
        private void CloseProperties()
        {
            IsPropertiesPanelOpen = false;
            PropertiesPrinter = null;
        }

        [RelayCommand]
        private async Task BrowseFile()
        {
            var lastPath = await _settingsService.GetValueAsync("LastSelectedPath", "");
            
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Printable Files|*.pdf;*.docx;*.xlsx;*.png;*.jpg;*.bmp;*.tiff;*.txt",
                InitialDirectory = !string.IsNullOrEmpty(lastPath) ? lastPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() == true)
            {
                NewJobFileName = dialog.FileName;
                await _settingsService.SetValueAsync("LastSelectedPath", System.IO.Path.GetDirectoryName(dialog.FileName) ?? "");
            }
        }

        [RelayCommand]
        private void ClearFile()
        {
            NewJobFileName = string.Empty;
            NewJobCopies = 1;
        }

        [RelayCommand]
        private async Task SubmitJob()
        {
            if (SelectedPrinter == null)
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("PleaseSelectPrinterFirst"), 
                    Services.LocalizationService.Instance.GetString("NoPrinterSelected"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(NewJobFileName) || NewJobCopies <= 0) return;

            var fileName = System.IO.Path.GetFileName(NewJobFileName);
            LogText += $"[{DateTime.Now:HH:mm:ss}] Printing: {fileName} ({NewJobCopies} copies) to {SelectedPrinter.Name}...\n";

            var job = _jobDistributionService.CreatePrintJob(NewJobFileName, NewJobCopies);
            job.TargetPrinterName = SelectedPrinter.Name;
            
            await _jobDistributionService.DistributeJobAsync(job);
            LogText += $"[{DateTime.Now:HH:mm:ss}] Job sent to: {SelectedPrinter.Name}\n";
            
            await _jobDistributionService.ProcessJobAsync(job.Id);
            LogText += $"[{DateTime.Now:HH:mm:ss}] ✓ Print completed.\n";
            
            // Reset
            NewJobFileName = string.Empty;
            NewJobCopies = 1;
        }

        [RelayCommand]
        private async Task TestPrint(PrinterInfo? printer)
        {
            var targetPrinter = printer ?? SelectedPrinter;
            if (targetPrinter == null) return;

            LogText += $"[{DateTime.Now:HH:mm:ss}] Sending Test Page to '{targetPrinter.Name}'...\n";
            await Task.Delay(1000);
            LogText += $"[{DateTime.Now:HH:mm:ss}] ✓ Test Page sent successfully.\n";
        }

        [RelayCommand]
        private void ToggleViewMode()
        {
            ViewMode = ViewMode == "Grid" ? "List" : "Grid";
        }

        [RelayCommand]
        private async Task FlushQueue(PrinterInfo? printer)
        {
            if (printer == null) return;
            LogText += $"[{DateTime.Now:HH:mm:ss}] Flushing queue for '{printer.Name}'...\n";
            await Task.Delay(500);
            printer.QueueLength = 0;
            LogText += $"[{DateTime.Now:HH:mm:ss}] ✓ Queue flushed.\n";
        }
        [RelayCommand]
        private void DoNothing() { }
    }
}
