using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Services;
using Apex.Services.Printing;
using Apex.Services.Printing.Queue;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using System;
using System.Linq;
using System.Windows;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// ViewModel for Print Execution Workspace.
    /// 
    /// PURPOSE: Execute printing only - NOT settings, NOT diagnostics.
    /// 
    /// ARCHITECTURAL RULE:
    /// All print commands MUST pass through SmartPrintManager.
    /// Direct printing to OS/drivers is FORBIDDEN.
    /// </summary>
    public partial class PrintOperationsViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _printerDiscoveryService;
        private readonly ISettingsService _settingsService;
        private readonly ISmartPrintManager _smartPrintManager;
        private readonly DispatcherTimer _statusTimer;
        private CancellationTokenSource? _printCts;

        // Active batch tracking for aggregate progress reporting
        private readonly HashSet<string> _activeBatchJobIds = new();
        private readonly object _activeBatchLock = new();

        #region File Properties

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasFile))]
        [NotifyPropertyChangedFor(nameof(DisplayFileName))]
        [NotifyPropertyChangedFor(nameof(FileTypeIcon))]
        [NotifyPropertyChangedFor(nameof(FileTypeDisplay))]
        [NotifyPropertyChangedFor(nameof(FileSizeDisplay))]
        [NotifyPropertyChangedFor(nameof(CanSmartPrint))]
        private string _selectedFilePath = string.Empty;

        [ObservableProperty]
        private int _filePageCount = 0;

        public bool HasFile => !string.IsNullOrEmpty(SelectedFilePath) && File.Exists(SelectedFilePath);
        
        public string DisplayFileName => HasFile 
            ? Path.GetFileName(SelectedFilePath) 
            : Application.Current.TryFindResource("PrintExecution_NoFileSelected")?.ToString() ?? "No file selected";
        
        public string FileTypeIcon => GetFileTypeIcon(SelectedFilePath);
        public string FileTypeDisplay => GetFileTypeDisplay(SelectedFilePath);
        public string FileSizeDisplay => GetFileSizeDisplay(SelectedFilePath);

        #endregion

        #region Printer Properties

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TotalPrintersCount))]
        [NotifyPropertyChangedFor(nameof(OnlinePrintersCount))]
        [NotifyPropertyChangedFor(nameof(FilteredPrinters))]
        private ObservableCollection<PrinterInfo> _printers = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedPrintersCount))]
        [NotifyPropertyChangedFor(nameof(HasSelectedPrinters))]
        [NotifyPropertyChangedFor(nameof(CanSmartPrint))]
        private ObservableCollection<PrinterInfo> _selectedPrinters = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilteredPrinters))]
        private string _printerSearchQuery = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilteredPrinters))]
        private string _printerFilterType = "All"; // All, Ready, Offline

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsComfortableMode))]
        [NotifyPropertyChangedFor(nameof(IsCompactMode))]
        private string _densityMode = "Comfortable"; // Comfortable, Compact

        public int TotalPrintersCount => Printers?.Count ?? 0;
        public int SelectedPrintersCount => SelectedPrinters?.Count ?? 0;
        public bool HasSelectedPrinters => SelectedPrintersCount > 0;
        public int OnlinePrintersCount => Printers?.Count(p => p.IsOnline) ?? 0;
        
        public bool IsComfortableMode => DensityMode == "Comfortable";
        public bool IsCompactMode => DensityMode == "Compact";

        /// <summary>
        /// Filtered printers based on search query and filter type.
        /// </summary>
        public ObservableCollection<PrinterInfo> FilteredPrinters
        {
            get
            {
                var query = Printers.AsEnumerable();

                // Search filter
                if (!string.IsNullOrWhiteSpace(PrinterSearchQuery))
                {
                    query = query.Where(p => p.Name.Contains(PrinterSearchQuery, StringComparison.OrdinalIgnoreCase));
                }

                // Status filter
                query = PrinterFilterType switch
                {
                    "Ready" => query.Where(p => p.IsOnline),
                    "Offline" => query.Where(p => !p.IsOnline),
                    _ => query
                };

                // Sort: Online first, then by name
                query = query.OrderByDescending(p => p.IsOnline).ThenBy(p => p.Name);

                return new ObservableCollection<PrinterInfo>(query);
            }
        }

        #endregion

        #region Print Options

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSmartPrint))]
        private int _newJobCopies = 1;

        [ObservableProperty]
        private bool _isSingleSided = true;

        [ObservableProperty]
        private bool _isDoubleSided = false;

        [ObservableProperty]
        private bool _isColorPrint = true;

        [ObservableProperty]
        private string _printQuality = "Normal"; // Draft, Normal, High

        [ObservableProperty]
        private bool _usePageRange = false;

        [ObservableProperty]
        private int _pageRangeFrom = 1;

        [ObservableProperty]
        private int _pageRangeTo = 1;

        [ObservableProperty]
        private string _paperSize = "A4"; // A4, A3, Letter, Legal

        [ObservableProperty]
        private bool _isPortrait = true;

        [ObservableProperty]
        private bool _isLandscape = false;

        [ObservableProperty]
        private bool _collate = true; // 1,2,3/1,2,3 vs 1,1,1/2,2,2

        [ObservableProperty]
        private string _fitMode = "Fit"; // Actual, Fit, Custom

        [ObservableProperty]
        private int _fitCustomPercent = 100;

        [ObservableProperty]
        private string _printOrder = "FirstToLast"; // FirstToLast, LastToFirst

        [ObservableProperty]
        private int _nUpMode = 1; // 1, 2, 4

        [ObservableProperty]
        private string _jobPriority = "Normal"; // Low, Normal, High

        [ObservableProperty]
        private bool _notifyOnComplete = true;

        [ObservableProperty]
        private bool _notifyWithSound = false;

        partial void OnIsSingleSidedChanged(bool value) { if (value) IsDoubleSided = false; }
        partial void OnIsDoubleSidedChanged(bool value) { if (value) IsSingleSided = false; }
        partial void OnIsPortraitChanged(bool value) { if (value) IsLandscape = false; }
        partial void OnIsLandscapeChanged(bool value) { if (value) IsPortrait = false; }

        #endregion

        #region Toast Notification

        [ObservableProperty]
        private bool _isToastVisible = false;

        [ObservableProperty]
        private string _toastMessage = "";

        [ObservableProperty]
        private bool _toastIsSuccess = true;

        private async Task ShowToastAsync(string message, bool success = true)
        {
            ToastMessage = message;
            ToastIsSuccess = success;
            IsToastVisible = true;

            if (NotifyWithSound)
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }

            await Task.Delay(4000);
            IsToastVisible = false;
        }

        [RelayCommand]
        private void DismissToast() => IsToastVisible = false;

        #endregion

        #region Presets

        private static readonly string PresetsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Apex", "print-presets.json");

        [ObservableProperty]
        private ObservableCollection<PrintPreset> _presets = new();

        [ObservableProperty]
        private string _newPresetName = "";

        [ObservableProperty]
        private bool _isPresetsOpen = false;

        public override async Task InitializeAsync()
        {
            await LoadPrintersAsync();
            UpdateStatusMessage();
            LoadPresets();
            _statusTimer.Start();
        }

        private void LoadPresets()
        {
            try
            {
                if (!File.Exists(PresetsPath)) return;
                var list = JsonSerializer.Deserialize<List<PrintPreset>>(File.ReadAllText(PresetsPath));
                if (list != null)
                {
                    Presets.Clear();
                    foreach (var p in list) Presets.Add(p);
                }
            }
            catch { }
        }

        private void SavePresetsFile()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PresetsPath)!);
                File.WriteAllText(PresetsPath, JsonSerializer.Serialize(Presets.ToList()));
            }
            catch { }
        }

        [RelayCommand]
        private void SavePreset()
        {
            var name = NewPresetName.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var preset = new PrintPreset
            {
                Name = name,
                Copies = NewJobCopies,
                IsSingleSided = IsSingleSided,
                IsDoubleSided = IsDoubleSided,
                IsColorPrint = IsColorPrint,
                PrintQuality = PrintQuality,
                PaperSize = PaperSize,
                IsPortrait = IsPortrait,
                Collate = Collate,
                FitMode = FitMode,
                FitCustomPercent = FitCustomPercent,
                PrintOrder = PrintOrder,
                NUpMode = NUpMode,
                JobPriority = JobPriority
            };

            // Replace if same name exists
            var existing = Presets.FirstOrDefault(p => p.Name == name);
            if (existing != null) Presets.Remove(existing);
            Presets.Insert(0, preset);
            SavePresetsFile();
            NewPresetName = "";
        }

        [RelayCommand]
        private void ApplyPreset(PrintPreset? preset)
        {
            if (preset == null) return;
            NewJobCopies = preset.Copies;
            IsSingleSided = preset.IsSingleSided;
            IsDoubleSided = preset.IsDoubleSided;
            IsColorPrint = preset.IsColorPrint;
            PrintQuality = preset.PrintQuality;
            PaperSize = preset.PaperSize;
            IsPortrait = preset.IsPortrait;
            IsLandscape = !preset.IsPortrait;
            Collate = preset.Collate;
            FitMode = preset.FitMode;
            FitCustomPercent = preset.FitCustomPercent;
            PrintOrder = preset.PrintOrder;
            NUpMode = preset.NUpMode;
            JobPriority = preset.JobPriority;
            IsPresetsOpen = false;
        }

        [RelayCommand]
        private void DeletePreset(PrintPreset? preset)
        {
            if (preset == null) return;
            Presets.Remove(preset);
            SavePresetsFile();
        }

        [RelayCommand]
        private void TogglePresets() => IsPresetsOpen = !IsPresetsOpen;

        #endregion

        #region Status Properties

        [ObservableProperty]
        private string _statusMessage = "Ready • Smart Background Printing Enabled";

        [ObservableProperty]
        private string _printProgress = string.Empty;

        [ObservableProperty]
        private bool _isPrinting = false;

        [ObservableProperty]
        private int _printProgressPercent = 0;

        [ObservableProperty]
        private string _printElapsedTime = "00:00";

        [ObservableProperty]
        private string _estimatedTimeRemaining = "";

        private DateTime _printStartTime;
        private System.Threading.Timer? _printElapsedTimer;

        #endregion

        #region Print Jobs Properties

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPrintJobs))]
        [NotifyPropertyChangedFor(nameof(PrintJobsCount))]
        private ObservableCollection<PrintJobItemViewModel> _printJobs = new();

        public bool HasPrintJobs => PrintJobs.Count > 0;
        public int PrintJobsCount => PrintJobs.Count;

        #endregion

        #region Computed Properties

        public bool CanSmartPrint
        {
            get
            {
                var result = HasFile && 
                            HasSelectedPrinters && 
                            NewJobCopies > 0 && 
                            !IsPrinting;
                
                return result;
            }
        }

        #endregion

        public PrintOperationsViewModel(
            IPrinterDiscoveryService printerDiscoveryService,
            ISettingsService settingsService)
        {
            _printerDiscoveryService = printerDiscoveryService;
            _settingsService = settingsService;
            _smartPrintManager = SmartPrintManager.Instance;
            
            // Subscribe to status updates
            _smartPrintManager.StatusChanged += OnSmartPrintStatusChanged;
            _smartPrintManager.ProgressChanged += OnSmartPrintProgressChanged;
            _smartPrintManager.JobCompleted += OnSmartPrintJobCompleted;

            // Subscribe to PrintJobsManager events for aggregate progress reporting
            // (the actual print path uses PrintJobsManager, not SmartPrintManager)
            var jobsManager = Apex.Services.Printing.PrintJobsManager.Instance;
            jobsManager.JobProgress += OnPrintJobProgress;
            jobsManager.JobCompleted += OnPrintJobCompletedOrFailed;
            jobsManager.JobFailed += OnPrintJobCompletedOrFailed;
            
            // Subscribe to collection changes to update computed properties
            Printers.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(TotalPrintersCount));
                OnPropertyChanged(nameof(OnlinePrintersCount));
            };
            
            SelectedPrinters.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(SelectedPrintersCount));
                OnPropertyChanged(nameof(HasSelectedPrinters));
                OnPropertyChanged(nameof(CanSmartPrint));
            };
            
            // Timer for refreshing printer status
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _statusTimer.Tick += async (s, e) =>
            {
                try
                {
                    await RefreshPrinterStatusAsync();
                }
                catch
                {
                    // Silently ignore background refresh errors
                }
            };
        }

        #region Commands

        /// <summary>
        /// Browse and select a file for printing.
        /// </summary>
        [RelayCommand]
        private async Task BrowseFileAsync()
        {
            var lastPath = await _settingsService.GetValueAsync("LastSelectedPath", "");

            var safeInitial = !string.IsNullOrEmpty(lastPath) && Directory.Exists(lastPath)
                ? lastPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Printable Files|*.pdf;*.docx;*.xlsx;*.png;*.jpg;*.bmp;*.tiff;*.txt|All Files|*.*",
                    InitialDirectory = safeInitial
                };

                if (dialog.ShowDialog() == true)
                {
                    SelectedFilePath = dialog.FileName;
                    await _settingsService.SetValueAsync("LastSelectedPath", Path.GetDirectoryName(dialog.FileName) ?? "");
                    
                    // Get page count for PDFs
                    await GetFilePageCountAsync();
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Reset last path and retry once with Documents
                await _settingsService.SetValueAsync("LastSelectedPath", "");
                var fallbackInitial = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Printable Files|*.pdf;*.docx;*.xlsx;*.png;*.jpg;*.bmp;*.tiff;*.txt|All Files|*.*",
                    InitialDirectory = fallbackInitial
                };

                if (dialog.ShowDialog() == true)
                {
                    SelectedFilePath = dialog.FileName;
                    await _settingsService.SetValueAsync("LastSelectedPath", Path.GetDirectoryName(dialog.FileName) ?? "");
                    
                    await GetFilePageCountAsync();
                }
            }
        }

        /// <summary>
        /// Load available printers.
        /// </summary>
        [RelayCommand]
        private async Task LoadPrintersAsync()
        {
            try
            {
                var discovered = await _printerDiscoveryService.ScanAsync();
                var printerList = discovered.ToList();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Printers.Clear();
                    foreach (var printer in printerList)
                    {
                        Printers.Add(printer);
                    }
                    OnPropertyChanged(nameof(TotalPrintersCount));
                    OnPropertyChanged(nameof(OnlinePrintersCount));
                    OnPropertyChanged(nameof(FilteredPrinters));
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading printers: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggle printer selection for multi-print.
        /// </summary>
        [RelayCommand]
        private void TogglePrinterSelection(PrinterInfo? printer)
        {
            if (printer == null) return;

            if (SelectedPrinters.Contains(printer))
            {
                SelectedPrinters.Remove(printer);
                printer.IsChecked = false;
            }
            else
            {
                SelectedPrinters.Add(printer);
                printer.IsChecked = true;
            }

            OnPropertyChanged(nameof(SelectedPrintersCount));
            OnPropertyChanged(nameof(HasSelectedPrinters));
            OnPropertyChanged(nameof(CanSmartPrint));
        }

        /// <summary>
        /// Select all online printers from filtered list.
        /// </summary>
        [RelayCommand]
        private void SelectAllPrinters()
        {
            SelectedPrinters.Clear();
            foreach (var printer in FilteredPrinters.Where(p => p.IsOnline))
            {
                printer.IsChecked = true;
                SelectedPrinters.Add(printer);
            }
            OnPropertyChanged(nameof(SelectedPrintersCount));
            OnPropertyChanged(nameof(HasSelectedPrinters));
            OnPropertyChanged(nameof(CanSmartPrint));
        }

        /// <summary>
        /// Deselect all printers.
        /// </summary>
        [RelayCommand]
        private void DeselectAllPrinters()
        {
            foreach (var printer in SelectedPrinters)
            {
                printer.IsChecked = false;
            }
            SelectedPrinters.Clear();
            OnPropertyChanged(nameof(SelectedPrintersCount));
            OnPropertyChanged(nameof(HasSelectedPrinters));
            OnPropertyChanged(nameof(CanSmartPrint));
        }

        /// <summary>
        /// Toggle density mode between Comfortable and Compact.
        /// </summary>
        [RelayCommand]
        private void ToggleDensityMode()
        {
            DensityMode = DensityMode == "Comfortable" ? "Compact" : "Comfortable";
        }

        /// <summary>
        /// Reset workspace to default state (UI only, no impact on printing engine).
        /// 
        /// ═══════════════════════════════════════════════════════════════════
        /// SAFETY: This command only resets UI state.
        /// It does NOT:
        /// - Stop running print jobs
        /// - Touch SmartPrintManager
        /// - Affect print queue
        /// - Interfere with background services
        /// ═══════════════════════════════════════════════════════════════════
        /// </summary>
        [RelayCommand]
        private void ResetWorkspace()
        {
            // 1. Clear selected file
            SelectedFilePath = string.Empty;
            FilePageCount = 0;
            UsePageRange = false;
            PageRangeFrom = 1;
            PageRangeTo = 1;

            // 2. Deselect all printers
            DeselectAllPrinters();

            // 3. Reset search and filter
            PrinterSearchQuery = string.Empty;
            PrinterFilterType = "All";

            // 4. Reset status message
            StatusMessage = "Ready • Waiting for input";
            PrintProgress = string.Empty;

            // 5. Reset density mode (optional - keep current preference)
            // DensityMode = "Comfortable";

            // Notify property changes
            OnPropertyChanged(nameof(DisplayFileName));
            OnPropertyChanged(nameof(FileTypeIcon));
            OnPropertyChanged(nameof(FileSizeDisplay));
            OnPropertyChanged(nameof(FileTypeDisplay));
            OnPropertyChanged(nameof(FilteredPrinters));
        }

        /// <summary>
        /// MAIN ACTION: Execute printing via Print Jobs Manager (with full tracking).
        /// 
        /// ═══════════════════════════════════════════════════════════════════
        /// CRITICAL: All printing goes through PrintJobsManager for visibility.
        /// Jobs are immediately visible in Print Jobs tab.
        /// ═══════════════════════════════════════════════════════════════════
        /// </summary>
        [RelayCommand]
        private async Task SmartPrintAsync()
        {
            
            if (!CanSmartPrint)
            {
                return;
            }

            IsPrinting = true;
            PrintProgressPercent = 0;
            PrintElapsedTime = "00:00";
            _printStartTime = DateTime.Now;
            _printCts = new CancellationTokenSource();
            OnPropertyChanged(nameof(CanSmartPrint));

            _printElapsedTimer = new System.Threading.Timer(_ =>
            {
                var elapsed = DateTime.Now - _printStartTime;
                Application.Current?.Dispatcher.InvokeAsync(() =>
                    PrintElapsedTime = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}");
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

            try
            {
                // Validate file exists
                if (!File.Exists(SelectedFilePath))
                {
                    StatusMessage = $"❌ File not found: {SelectedFilePath}";
                    Apex.Services.Logging.PrintLogger.Error(
                        new FileNotFoundException($"File not found: {SelectedFilePath}", SelectedFilePath),
                        "[PrintOperations] File not found: {0}",
                        SelectedFilePath);
                    return;
                }

                var printerNames = SelectedPrinters.Select(p => p.Name).ToList();
                
                if (printerNames.Count == 0)
                {
                    StatusMessage = "❌ No printers selected";
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] Submitting {printerNames.Count} job(s) to queue");
                
                // ═══════════════════════════════════════════════════════════════
                // Use PrintJobsManager for full lifecycle tracking
                // Jobs will appear in Printer Monitor (DistributionView)
                // ═══════════════════════════════════════════════════════════════
                var jobsManager = Apex.Services.Printing.PrintJobsManager.Instance;
                
                // Ensure queue manager is started
                try
                {
                    Apex.Services.Printing.Queue.PrintJobQueueManager.Instance.Start();
                }
                catch (Exception queueEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] Queue already started or error: {queueEx.Message}");
                }
                
                // ═══════════════════════════════════════════════════════════════════
                // PRINT OPERATIONS: Document-based printing ONLY
                // NO scaleMode, NO rendering, NO fallback
                // Path: PrintOperationsViewModel → PrintJobsManager → DocumentPrintService
                // ═══════════════════════════════════════════════════════════════════
                var tasks = printerNames.Select(printerName =>
                {
                    System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] ══════════════════════════════════════");
                    System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] PRINT OPERATIONS JOB SUBMISSION");
                    System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] Printer: {printerName}");
                    System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] File: {SelectedFilePath}");
                    System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] Copies: {NewJobCopies}");
                    System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] Mode: documentMode=true (NO rendering)");
                    System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] ══════════════════════════════════════");
                    
                    return jobsManager.SubmitPrintJobAsync(
                        printerName,
                        SelectedFilePath,
                        NewJobCopies,
                        useQueue: true,
                        _printCts.Token,
                        scaleMode: null,  // NOT USED in Print Operations (ignored when documentMode=true)
                        documentMode: true);  // CRITICAL: Forces DocumentPrintService path
                }).ToList();

                // Track batch job IDs as they get created (for aggregate progress).
                // We hook JobCreated and capture the IDs returned by SubmitPrintJobAsync.
                lock (_activeBatchLock)
                {
                    _activeBatchJobIds.Clear();
                }

                var jobIds = await Task.WhenAll(tasks);

                lock (_activeBatchLock)
                {
                    foreach (var id in jobIds)
                    {
                        if (!string.IsNullOrEmpty(id))
                            _activeBatchJobIds.Add(id);
                    }
                }

                var successCount = jobIds.Count(j => !string.IsNullOrEmpty(j));

                System.Diagnostics.Debug.WriteLine($"[PrintOperationsViewModel] Successfully submitted {successCount} job(s)");

                // Show result
                StatusMessage = $"✓ Submitted {successCount} job(s) to {printerNames.Count} printer(s) - Check Printer Monitor for status";

                if (NotifyOnComplete)
                    _ = ShowToastAsync($"✓ {successCount} job(s) submitted to {printerNames.Count} printer(s)", true);
                
                // Force UI update on dispatcher thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(StatusMessage));
                    OnPropertyChanged(nameof(IsPrinting));
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Printing cancelled";
                if (NotifyOnComplete)
                    _ = ShowToastAsync("⚠ Print job cancelled", false);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Apex.Services.Logging.PrintLogger.Error(ex, "[PrintOperations] Failed to submit print jobs");
                if (NotifyOnComplete)
                    _ = ShowToastAsync($"❌ Error: {ex.Message}", false);
            }
            finally
            {
                
                try
                {
                    
                    // Update UI state on dispatcher thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        
                        IsPrinting = false;
                        OnPropertyChanged(nameof(IsPrinting));
                        OnPropertyChanged(nameof(CanSmartPrint));
                    }, System.Windows.Threading.DispatcherPriority.Normal);
                }
                catch (Exception)
                {
                    // Fallback: update on current thread
                    IsPrinting = false;
                    OnPropertyChanged(nameof(IsPrinting));
                    OnPropertyChanged(nameof(CanSmartPrint));
                }
                
                _printElapsedTimer?.Dispose();
                _printElapsedTimer = null;
                _printCts?.Dispose();
                _printCts = null;

                // Reset progress after delay
                _ = ResetStatusAfterDelay();
            }
        }

        #endregion

        #region Event Handlers

        [RelayCommand]
        private void CancelPrint() => _printCts?.Cancel();

        private void OnSmartPrintStatusChanged(string status)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = status;
            });
        }

        private void OnSmartPrintProgressChanged(int progress)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                PrintProgressPercent = progress;
                PrintProgress = $"Printing... {progress}%";

                if (progress > 0 && progress < 100)
                {
                    var elapsed = DateTime.Now - _printStartTime;
                    var totalEstimated = TimeSpan.FromSeconds(elapsed.TotalSeconds * 100.0 / progress);
                    var remaining = totalEstimated - elapsed;
                    EstimatedTimeRemaining = remaining.TotalSeconds > 0
                        ? $"~{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2} left"
                        : "";
                }
                else if (progress >= 100)
                {
                    EstimatedTimeRemaining = "Done";
                }
            });
        }

        private void OnSmartPrintJobCompleted(SmartPrintResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    PrintProgress = $"Completed {result.SuccessCount} printer(s)";
                }
            });
        }

        // ─── PrintJobsManager event handlers (the actual print path) ──────────
        private void OnPrintJobProgress(object? sender, PrintJobTracking job)
        {
            UpdateBatchAggregateProgress(job);
        }

        private void OnPrintJobCompletedOrFailed(object? sender, PrintJobTracking job)
        {
            UpdateBatchAggregateProgress(job);
        }

        private void UpdateBatchAggregateProgress(PrintJobTracking changedJob)
        {
            // Only react if the changed job belongs to our active batch
            bool inBatch;
            string[] batchIds;
            lock (_activeBatchLock)
            {
                inBatch = _activeBatchJobIds.Contains(changedJob.JobId);
                batchIds = _activeBatchJobIds.ToArray();
            }
            if (!inBatch || batchIds.Length == 0) return;

            var jobs = batchIds
                .Select(id => Apex.Services.Printing.PrintJobsManager.Instance.GetJob(id))
                .Where(j => j != null)
                .ToList();
            if (jobs.Count == 0) return;

            var avgProgress = (int)Math.Round(jobs.Average(j => (double)j!.Progress));
            var completed = jobs.Count(j => j!.State == Apex.Services.Printing.Queue.PrintJobState.Completed);
            var failed = jobs.Count(j => j!.State == Apex.Services.Printing.Queue.PrintJobState.Failed);
            var done = completed + failed + jobs.Count(j => j!.State == Apex.Services.Printing.Queue.PrintJobState.Cancelled || j!.State == Apex.Services.Printing.Queue.PrintJobState.Skipped);

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                PrintProgressPercent = avgProgress;
                PrintProgress = done >= jobs.Count
                    ? $"Done ({completed}/{jobs.Count} succeeded)"
                    : $"Printing... {done}/{jobs.Count} printers • {avgProgress}%";

                if (avgProgress > 0 && avgProgress < 100)
                {
                    var elapsed = DateTime.Now - _printStartTime;
                    var totalEstimated = TimeSpan.FromSeconds(elapsed.TotalSeconds * 100.0 / avgProgress);
                    var remaining = totalEstimated - elapsed;
                    EstimatedTimeRemaining = remaining.TotalSeconds > 0
                        ? $"~{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2} left"
                        : "";
                }
                else if (avgProgress >= 100)
                {
                    EstimatedTimeRemaining = "Done";
                }
            });
        }

        #endregion

        #region Helper Methods

        private async Task RefreshPrinterStatusAsync()
        {
            try
            {
                var discovered = await _printerDiscoveryService.ScanAsync();
                var discoveredList = discovered.ToList();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var printer in Printers)
                    {
                        var updated = discoveredList.FirstOrDefault(p => p.Name == printer.Name);
                        if (updated != null)
                        {
                            printer.IsOnline = updated.IsOnline;
                            printer.StatusText = updated.StatusText;
                            printer.QueueLength = updated.QueueLength;
                        }
                    }
                    OnPropertyChanged(nameof(TotalPrintersCount));
                    OnPropertyChanged(nameof(OnlinePrintersCount));
                    OnPropertyChanged(nameof(FilteredPrinters));
                });
            }
            catch
            {
                // Silently ignore
            }
        }

        /// <summary>
        /// Gets file page count for display purposes only.
        /// Print Operations does NOT use page-by-page rendering,
        /// so this is purely informational for the UI.
        /// </summary>
        private async Task GetFilePageCountAsync()
        {
            if (!HasFile) return;

            await Task.Run(() =>
            {
                try
                {
                    var ext = Path.GetExtension(SelectedFilePath).ToLowerInvariant();
                    if (ext == ".pdf")
                    {
                        // Estimate page count based on file size (for UI display only)
                        // Print Operations sends entire document to printer - no page processing
                        var fileInfo = new FileInfo(SelectedFilePath);
                        FilePageCount = Math.Max(1, (int)(fileInfo.Length / 50000));
                    }
                    else
                    {
                        // Images and other files = 1 page
                        FilePageCount = 1;
                    }
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        PageRangeFrom = 1;
                        PageRangeTo = FilePageCount;
                    });
                }
                catch
                {
                    FilePageCount = 1;
                }
            });
        }

        private void UpdateStatusMessage()
        {
            var summary = _smartPrintManager.GetStatusSummary();
            StatusMessage = summary;
        }

        private async Task ResetStatusAfterDelay()
        {
            await Task.Delay(5000);
            if (!IsPrinting)
            {
                StatusMessage = "Ready • Smart Background Printing Enabled";
                PrintProgress = string.Empty;
                PrintProgressPercent = 0;
                PrintElapsedTime = "00:00";
                EstimatedTimeRemaining = "";
            }
        }

        private static string GetFileTypeIcon(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "📄";
            
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "📕",
                ".docx" or ".doc" => "📘",
                ".xlsx" or ".xls" => "📗",
                ".pptx" or ".ppt" => "📙",
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => "🖼️",
                ".txt" => "📝",
                _ => "📄"
            };
        }

        private static string GetFileTypeDisplay(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "-";
            
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "PDF Document",
                ".docx" => "Word Document",
                ".doc" => "Word Document",
                ".xlsx" => "Excel Spreadsheet",
                ".xls" => "Excel Spreadsheet",
                ".pptx" => "PowerPoint",
                ".ppt" => "PowerPoint",
                ".png" => "PNG Image",
                ".jpg" or ".jpeg" => "JPEG Image",
                ".bmp" => "Bitmap Image",
                ".gif" => "GIF Image",
                ".txt" => "Text File",
                _ => ext.ToUpperInvariant().TrimStart('.')
            };
        }

        private static string GetFileSizeDisplay(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) 
                return "-";
            
            try
            {
                var fileInfo = new FileInfo(filePath);
                var bytes = fileInfo.Length;
                
                if (bytes < 1024) return $"{bytes} B";
                if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
                if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            }
            catch
            {
                return "-";
            }
        }

        #endregion
    }

}
