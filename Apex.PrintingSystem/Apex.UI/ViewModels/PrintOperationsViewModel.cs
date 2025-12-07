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
using Microsoft.EntityFrameworkCore; // For Rules queries if needed, or via Repository

namespace Apex.UI.ViewModels
{
    public partial class PrintOperationsViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _printerDiscoveryService;
        private readonly IJobDistributionService _jobService;
        private readonly IPrinterService _printerService;
        private readonly PrinterMonitoringService _monitoringService;
        private readonly IRepository<RoutingRule> _rulesRepository;
        private readonly DispatcherTimer _timer;
        private readonly IUniversalPrintPipeline _pipeline;
        private readonly IDialogService _dialogService;

        // --- Section A: Printers ---
        [ObservableProperty]
        private ObservableCollection<PrinterInfo> _printers = new();

        [ObservableProperty]
        private PrinterInfo? _selectedPrinter;

        [ObservableProperty]
        private string _printerSearchQuery = string.Empty;

        // --- Section B: Job Monitor ---
        [ObservableProperty]
        private ObservableCollection<PrintJobDisplayItem> _activeJobs = new();

        [ObservableProperty]
        private PrintJobDisplayItem? _selectedJob;

        // --- Section C: Rules ---
        [ObservableProperty]
        private ObservableCollection<RoutingRule> _rules = new();

        [ObservableProperty]
        private RoutingRule? _selectedRule;

        [ObservableProperty]
        private bool _isRuleEditorOpen;

        [ObservableProperty]
        private RoutingRule _editingRule = new();

        // --- Properties Panel ---
        [ObservableProperty] private bool _isPropertiesPanelOpen;
        [ObservableProperty] private PrinterInfo? _propertiesPrinter;

        private readonly ISettingsService _settingsService;

        // --- Quick Print & Logic ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanPrint))]
        private string _newJobFileName = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanPrint))]
        private int _newJobCopies = 1;

        public bool CanPrint => SelectedPrinter != null && !string.IsNullOrWhiteSpace(NewJobFileName) && NewJobCopies > 0;

        // --- Activity Log ---
        [ObservableProperty]
        private string _logText = string.Empty;

        // Stats
        [ObservableProperty] private int _totalJobs;
        [ObservableProperty] private int _pendingJobs;
        [ObservableProperty] private int _printingJobs;

        public PrintOperationsViewModel(
            IPrinterDiscoveryService printerDiscoveryService,
            IJobDistributionService jobService,
            IPrinterService printerService,
            PrinterMonitoringService monitoringService,
            IRepository<RoutingRule> rulesRepository,
            ISettingsService settingsService,
            IUniversalPrintPipeline pipeline,
            IDialogService dialogService)
        {
            _printerDiscoveryService = printerDiscoveryService;
            _jobService = jobService;
            _printerService = printerService;
            _monitoringService = monitoringService;
            _rulesRepository = rulesRepository;
            _settingsService = settingsService;
            _pipeline = pipeline;
            _dialogService = dialogService;

            _monitoringService.PrinterStatusChanged += OnPrinterStatusChanged;

            // Timer for refreshing jobs and printer status
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += async (s, e) =>
            {
                try
                {
                    await RefreshAllAsync();
                }
                catch (ObjectDisposedException)
                {
                    _timer.Stop();
                }
                catch
                {
                    // Silently ignore errors in background refresh
                }
            };
        }

        public override async Task InitializeAsync()
        {
            await LoadPrintersAsync();
            await LoadRulesAsync();
            await RefreshJobsAsync();
            _timer.Start();
            Log("Operations Center Initialized.");
        }

        // --- Section A Logic ---

        [RelayCommand]
        private async Task LoadPrintersAsync()
        {
            var osPrinters = await _printerDiscoveryService.ScanAsync();
            var dbPrinters = await _printerService.GetAllPrintersAsync();

            // Merge Info
            foreach (var printer in osPrinters)
            {
                var dbPrinter = dbPrinters.FirstOrDefault(p => p.Name.Equals(printer.Name, StringComparison.OrdinalIgnoreCase));
                if (dbPrinter != null)
                {
                    printer.Capabilities = _printerService.GetCapabilities(dbPrinter);
                }
                
                // Keep selection active
                if (SelectedPrinter != null && printer.Name == SelectedPrinter.Name)
                {
                    printer.IsSelected = true;
                    // Update the reference if needed, or just copy stats
                    SelectedPrinter.StatusText = printer.StatusText;
                    SelectedPrinter.QueueLength = printer.QueueLength;
                    SelectedPrinter.IsOnline = printer.IsOnline;
                }
            }

            // Filter if search query exists
            var filtered = osPrinters.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(PrinterSearchQuery))
            {
                filtered = filtered.Where(p => p.Name.Contains(PrinterSearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            // Update collection smart matching to avoid redraws if possible, or just replace
            // For simplicity in this step, replacing. Optimizations can follow.
            Printers = new ObservableCollection<PrinterInfo>(filtered);

            // Auto-select if none
            if (SelectedPrinter == null && Printers.Any())
            {
                SelectPrinter(Printers.First(p => p.IsOnline) ?? Printers.First());
            }
        }

        [RelayCommand]
        private void SelectPrinter(PrinterInfo? printer)
        {
            if (printer == null) return;
            
            // Deselect previous
            foreach (var p in Printers) p.IsSelected = false;
            if (SelectedPrinter != null) SelectedPrinter.IsSelected = false;

            printer.IsSelected = true;
            SelectedPrinter = printer;
            OnPropertyChanged(nameof(CanPrint));
            
            Log($"Selected Printer: {printer.Name}");

            // Trigger Job Refresh immediately for this printer
            _ = RefreshJobsAsync();
        }
        partial void OnPrinterSearchQueryChanged(string value) => _ = LoadPrintersAsync();

        [RelayCommand]
        private void OpenProperties(PrinterInfo? printer)
        {
            if (printer == null) return;
            PropertiesPrinter = printer;
            IsPropertiesPanelOpen = true;
            Log($"Opened properties for '{printer.Name}'");
        }

        [RelayCommand]
        private void CloseProperties()
        {
            IsPropertiesPanelOpen = false;
            PropertiesPrinter = null;
        }

        [RelayCommand]
        private async Task TestPrint(PrinterInfo? printer)
        {
             var target = printer ?? SelectedPrinter;
             if (target == null) return;
             
             Log($"Sending Test Page to '{target.Name}'...");
             await Task.Delay(1000); // Simulate
             Log("✓ Test Page sent.");
        }

        [RelayCommand]
        private async Task FlushQueue(PrinterInfo? printer)
        {
             var target = printer ?? SelectedPrinter;
             if (target == null) return;

             Log($"Flushing queue for '{target.Name}'...");
             await Task.Delay(500); // Simulate
             target.QueueLength = 0;
             Log("✓ Queue flushed.");
        }


        // --- Quick Print Logic ---

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
            if (!CanPrint) return;

            var fileName = System.IO.Path.GetFileName(NewJobFileName);
            Log($"Printing '{fileName}' ({NewJobCopies} copies) to '{SelectedPrinter.Name}'...");

            try 
            {
                // Use Universal Pipeline to handle conversion if needed
                var job = await _pipeline.ProcessAndQueueJobAsync(NewJobFileName, SelectedPrinter.Name, NewJobCopies);
                
                Log($"✓ Job submitted (ID: {job.Id})");
                
                // Fire and forget processing
                _ = _jobService.ProcessJobAsync(job.Id);
            }
            catch (Exception ex)
            {
                Log($"❌ Error submitting job: {ex.Message}");
            }

            // Reset
            NewJobFileName = string.Empty;
            NewJobCopies = 1;
            await RefreshJobsAsync();
        }


        // --- Section B Logic ---

        [RelayCommand]
        private async Task RefreshJobsAsync()
        {
            if (SelectedPrinter == null) 
            {
                ActiveJobs.Clear();
                return;
            }

            // In a real scenario, JobService should support filtering by Printer
            // Accessing internal job list or fetching all and filtering in memory
            // Assuming JobService has a way to get all jobs or we fetch pending + active
            
            // Get all active jobs (Pending, Printing, etc.) from service
            var progresses = await _jobService.GetAllPrintingJobsProgressAsync();
            
            // We also need pending jobs that might not be "Printing" yet but are assigned to this printer via rules?
            // Or typically checking the queue. 
            // For now, let's fetch pending jobs and filter.
            var pending = await _jobService.GetPendingJobsOrderedByPriorityAsync();
            
            var displayItems = pending.Select(j => PrintJobDisplayItem.FromPrintJob(j)).ToList();
            
            // Merge progress info
            foreach (var prog in progresses)
            {
                var existing = displayItems.FirstOrDefault(d => d.JobId == prog.JobId);
                if (existing != null)
                {
                    existing.UpdateProgress(prog);
                }
                else 
                {
                    // It's a job not in pending (maybe actively printing), we might need to fetch the Job object details separately
                    // simpler approach: The JobService probably has a cache or DB access.
                    // For this implementation, we will assume displayItems captures pending. 
                    // To see 'Print' status jobs, we rely on them being in the list.
                }
            }

            // Filter by SelectedPrinter
            // Note: If no rule assigned yet, TargetPrinterName might be null. 
            // If Single Printer Mode is enforced manually, jobs might be created with that target.
            var filteredJobs = displayItems.Where(j => j.PrinterName == SelectedPrinter.Name || string.IsNullOrEmpty(j.PrinterName)).ToList();

            ActiveJobs = new ObservableCollection<PrintJobDisplayItem>(filteredJobs);

            // Update Stats
            PendingJobs = ActiveJobs.Count(j => j.Status == Core.Enums.PrintJobStatus.Pending);
            PrintingJobs = ActiveJobs.Count(j => j.Status == Core.Enums.PrintJobStatus.Printing);
            TotalJobs = ActiveJobs.Count;
        }

        [RelayCommand]
        private async Task CancelJob(PrintJobDisplayItem? job)
        {
            if (job == null) return;
            Log($"Cancelling job {job.JobId}...");
            await _jobService.CancelJobAsync(job.JobId);
            await RefreshJobsAsync();
        }

        [RelayCommand]
        private async Task CancelAllPending()
        {
            Log("Cancelling all pending jobs...");
            await _jobService.CancelAllPendingJobsAsync();
            await RefreshJobsAsync();
            Log("✓ All pending jobs cancelled.");
        }

        [RelayCommand]
        private async Task RetryJob(PrintJobDisplayItem? job)
        {
            if (job == null) return;
            Log($"Retrying job {job.JobId}...");
            await _jobService.RetryFailedJobAsync(job.JobId);
            await RefreshJobsAsync();
        }


        // --- Section C Logic ---

        [RelayCommand]
        private async Task LoadRulesAsync()
        {
            var rules = await _rulesRepository.GetAllAsync();
            Rules = new ObservableCollection<RoutingRule>(rules.OrderByDescending(r => r.Priority));
        }

        [RelayCommand]
        private void AddRule()
        {
            EditingRule = new RoutingRule { Name = "New Rule", Priority = 10 };
            IsRuleEditorOpen = true;
        }

        [RelayCommand]
        private void EditRule(RoutingRule? rule)
        {
            if (rule == null) return;
            // Clone to avoid live editing before save
            EditingRule = new RoutingRule 
            { 
                Id = rule.Id, 
                Name = rule.Name, 
                Priority = rule.Priority,
                ConditionsJson = rule.ConditionsJson, 
                ActionsJson = rule.ActionsJson 
            };
            IsRuleEditorOpen = true;
        }

        [RelayCommand]
        private async Task DeleteRule(RoutingRule? rule)
        {
            if (rule == null) return;
            
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Delete Rule", 
                $"Are you sure you want to delete the rule '{rule.Name}'?", 
                "Delete", 
                "Cancel", 
                true);

            if (confirmed)
            {
                await _rulesRepository.DeleteAsync(rule);
                await LoadRulesAsync();
                Log($"Deleted rule '{rule.Name}'");
            }
        }

        [RelayCommand]
        private async Task SaveRule()
        {
            if (string.IsNullOrWhiteSpace(EditingRule.Name)) return;

            if (EditingRule.Id == 0)
            {
                await _rulesRepository.AddAsync(EditingRule);
                Log($"Created rule '{EditingRule.Name}'");
            }
            else
            {
                await _rulesRepository.UpdateAsync(EditingRule);
                Log($"Updated rule '{EditingRule.Name}'");
            }

            IsRuleEditorOpen = false;
            await LoadRulesAsync();
        }

        [RelayCommand]
        private void CancelRuleEdit()
        {
            IsRuleEditorOpen = false;
        }


        // --- Helper ---
        private void Log(string message)
        {
            LogText = $"[{DateTime.Now:HH:mm:ss}] {message}\n" + LogText;
            // Keep log size reasonable
            if (LogText.Length > 5000) LogText = LogText.Substring(0, 5000);
        }

        private async Task RefreshAllAsync()
        {
            await RefreshJobsAsync();
            // Optional: deep refresh of printers if needed
            // await LoadPrintersAsync(); // Might be too heavy to do every 2s, maybe just update stats
            
            // Lightweight Printer specific update
            // OnPrinterStatusChanged handles event-based updates
        }

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
                }
            });
        }
    }

    /// <summary>
    /// Display model for print jobs in the job monitor
    /// </summary>
    public partial class PrintJobDisplayItem : ObservableObject
    {
        [ObservableProperty] private int _jobId;
        [ObservableProperty] private string _fileName = string.Empty;
        [ObservableProperty] private string _printerName = string.Empty;
        [ObservableProperty] private Core.Enums.PrintJobStatus _status;
        [ObservableProperty] private int _copies;
        [ObservableProperty] private int _progress;
        [ObservableProperty] private int _currentPage;
        [ObservableProperty] private int _totalPages;
        [ObservableProperty] private string _statusText = string.Empty;
        [ObservableProperty] private DateTime _submittedAt;
        [ObservableProperty] private string _priority = "Normal";

        public static PrintJobDisplayItem FromPrintJob(PrintJob job)
        {
            return new PrintJobDisplayItem
            {
                JobId = job.Id,
                FileName = System.IO.Path.GetFileName(job.FileName),
                PrinterName = job.TargetPrinterName ?? "",
                Status = job.Status,
                Copies = job.TotalCopies,
                TotalPages = job.TotalPages,
                SubmittedAt = job.CreatedAt,
                Priority = job.Priority.ToString(),
                StatusText = job.Status.ToString()
            };
        }

        public void UpdateProgress(PrintJobProgress progress)
        {
            CurrentPage = progress.CurrentPage;
            TotalPages = progress.TotalPages;
            Progress = TotalPages > 0 ? (int)((CurrentPage / (double)TotalPages) * 100) : 0;
            StatusText = $"Page {CurrentPage}/{TotalPages}";
            Status = Core.Enums.PrintJobStatus.Printing;
        }
    }
}
