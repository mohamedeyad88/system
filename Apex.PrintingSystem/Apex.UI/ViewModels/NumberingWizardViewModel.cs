using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Services.Numbering;
using Apex.NumberedBooksEngine.Models;
using Apex.NumberedBooksEngine.Core;
using Apex.Core.Enums;
using Apex.Core.Models;
using Apex.UI.Models;
using Apex.NumberedBooksEngine.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Drawing.Printing;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Apex.UI.ViewModels
{
    public enum WorkflowMode
    {
        Prepare,
        Design,
        Print
    }

    /// <summary>
    /// Information about a cycle job for UI display
    /// </summary>
    public class CycleJobInfo
    {
        public string JobId { get; set; } = "";
        public int CycleNumber { get; set; }
        public long Number { get; set; }
        public CycleStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public partial class NumberingWizardViewModel : ViewModelBase
    {
        private readonly NumberingService _numberingService;
        private readonly CheckpointManager _checkpointManager = new();
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;
        private bool _isLoadingTemplate = false; // Flag to prevent recursive calls
        private DateTime _lastLoadTemplateCall = DateTime.MinValue; // Track last call time to prevent rapid double-clicks
        
        // ═══════════════════════════════════════════════════════════════════
        // SINGLE SOURCE OF TRUTH: Central print job model
        // All calculations happen ONCE in this model
        // UI screens read from this model, never recalculate
        // ═══════════════════════════════════════════════════════════════════
        private NumberingPrintJobModel _printJobModel = new();
        
        /// <summary>
        /// Navigation history for back button
        /// </summary>
        private readonly Stack<WorkflowMode> _navigationHistory = new();

        // ── Undo / Redo stacks ───────────────────────────────────────────
        private readonly Stack<List<NumberingSlotData>> _undoStack = new();
        private readonly Stack<List<NumberingSlotData>> _redoStack = new();
        private const int MaxUndoSteps = 30;

        // ── Undo / Redo state ────────────────────────────────────────────
        [ObservableProperty] private bool _canUndo = false;
        [ObservableProperty] private bool _canRedo = false;

        // Basic properties (from XAML bindings)
        [ObservableProperty]
        private string? _selectedPrinter;

        partial void OnSelectedPrinterChanged(string? value)
        {
        }

        [ObservableProperty]
        private long _startNumber = 1;

        partial void OnStartNumberChanged(long value)
        {
            // Update PreviewNumber for all slots when StartNumber changes
            // For preview, show different numbers for each slot based on numbering mode
            for (int i = 0; i < Slots.Count; i++)
            {
                Slots[i].PreviewNumber = CalculatePreviewNumber(value, i);
            }
            SchedulePreviewUpdate();
        }

        [ObservableProperty]
        private long _totalNumbers = 100;

        partial void OnTotalNumbersChanged(long value)
        {
            // Refresh preview numbers when TotalNumbers changes (affects Imposed mode calculation)
            RefreshAllPreviewNumbers();

            // Also update computed values
            UpdateComputedValues();
            SchedulePreviewUpdate();
        }
        
        /// <summary>
        /// Refreshes preview numbers for all slots based on current settings.
        /// </summary>
        private void RefreshAllPreviewNumbers()
        {
            if (Slots == null) return;
            
            for (int i = 0; i < Slots.Count; i++)
            {
                Slots[i].PreviewNumber = CalculatePreviewNumber(StartNumber, i);
            }
        }

        [ObservableProperty]
        private bool _useCopy1 = false;

        [ObservableProperty]
        private bool _useCopy2 = false;

        [ObservableProperty]
        private bool _useCopy3 = false;

        [ObservableProperty]
        private bool _useSmartTrayPrinting = false;

        [ObservableProperty]
        private bool _isPrinting = false;

        /// <summary>
        /// Returns a description of the printing mode behavior for UI display.
        /// </summary>
        public string PrintingModeDescription
        {
            get
            {
                if (UseSmartTrayPrinting)
                {
                    return "سيتم طباعة كل صفحة مع صورها بالتتابع، وسيخرج الدفتر مترتب تلقائيًا بدون حاجة للتجميع اليدوي.";
                }
                else
                {
                    return "سيتم طباعة كل صفحات الأصل أولًا، ثم كل صفحات الصور. سيتم التجميع يدويًا بعد الطباعة.";
                }
            }
        }

        /// <summary>
        /// Returns the printing mode name for UI display.
        /// </summary>
        public string PrintingModeName
        {
            get
            {
                return UseSmartTrayPrinting ? "طباعة ذكية" : "طباعة تقليدية";
            }
        }

        partial void OnUseSmartTrayPrintingChanged(bool value)
        {
            OnPropertyChanged(nameof(PrintingModeDescription));
            OnPropertyChanged(nameof(PrintingModeName));
        }

        [ObservableProperty]
        private string _printStatus = "جاهز";

        [ObservableProperty]
        private double _printProgress = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPrepareMode))]
        [NotifyPropertyChangedFor(nameof(IsLayoutMode))]
        [NotifyPropertyChangedFor(nameof(IsExecuteMode))]
        private WorkflowMode _workflowMode = WorkflowMode.Prepare;

        // Computed properties for UI visibility
        public bool IsPrepareMode => WorkflowMode == WorkflowMode.Prepare;
        public bool IsLayoutMode => WorkflowMode == WorkflowMode.Design;
        public bool IsExecuteMode => WorkflowMode == WorkflowMode.Print;
        
        // Alias for XAML bindings that use CurrentMode
        public WorkflowMode CurrentMode => WorkflowMode;

        partial void OnWorkflowModeChanged(WorkflowMode value)
        {
            
            OnPropertyChanged(nameof(IsPrepareMode));
            OnPropertyChanged(nameof(IsLayoutMode));
            OnPropertyChanged(nameof(IsExecuteMode));
        }

        // Cycle-based printing properties
        [ObservableProperty]
        private bool _useCycleBasedPrinting = false;

        [ObservableProperty]
        private int _currentCycleNumber = 0;

        [ObservableProperty]
        private int _totalCycles = 0;

        [ObservableProperty]
        private int _completedCycles = 0;

        [ObservableProperty]
        private int _failedCycles = 0;

        [ObservableProperty]
        private string _currentCycleStatus = "";

        [ObservableProperty]
        private ObservableCollection<CycleJobInfo> _failedCyclesList = new();

        [ObservableProperty]
        private CycleJobInfo? _selectedFailedCycle;

        // Additional properties that might be needed from XAML
        [ObservableProperty]
        private ObservableCollection<string> _availablePrinters = new();

        public record TrayOption(string DisplayName, PaperSourceKind Value);

        [ObservableProperty]
        private ObservableCollection<TrayOption> _availableTrayOptions = new();

        [ObservableProperty]
        private PaperSourceKind? _copy1Tray;

        [ObservableProperty]
        private PaperSourceKind? _copy2Tray;

        [ObservableProperty]
        private PaperSourceKind? _copy3Tray;

        [ObservableProperty]
        private PaperSourceKind _originalTray = PaperSourceKind.Upper;  // Tray for Original (Copy 0)

        [ObservableProperty]
        private int _copiesCount = 1;

        [ObservableProperty]
        private int _numberOfCopies = 1;  // Single number input for copies (1-3)

        [ObservableProperty]
        private long _totalPagesComputed = 0;

        // Template and slots properties
        [ObservableProperty]
        private string? _templatePath;

        [ObservableProperty]
        private BitmapSource? _templateImage;

        [ObservableProperty]
        private double _canvasWidth = 800;

        [ObservableProperty]
        private double _canvasHeight = 1131; // A4 ratio

        [ObservableProperty]
        private NumberSlot? _selectedSlot;

        [ObservableProperty]
        private ObservableCollection<GuideLine> _guides = new();

        [ObservableProperty]
        private bool _isBold = false;

        [ObservableProperty]
        private double _rotation = 0;

        [ObservableProperty]
        private double _zoomLevel = 1.0;

        [ObservableProperty]
        private bool _showGrid = false;

        [ObservableProperty]
        private ObservableCollection<GridLine> _gridLines = new();

        [ObservableProperty]
        private PrintScaleMode _printScaleMode = PrintScaleMode.ActualSize;

        // Numbering mode properties (Linear/Imposed)
        [ObservableProperty]
        private bool _isLinearMode = true;  // Default to linear (ترقيم الشرشرة)

        partial void OnIsLinearModeChanged(bool value)
        {
            if (value)
            {
                IsImposedMode = false;
            }
            
            // Update preview numbers when mode changes
            OnStartNumberChanged(StartNumber);
        }

        [ObservableProperty]
        private bool _isImposedMode = false;  // Imposed mode (ترقيم القص)

        partial void OnIsImposedModeChanged(bool value)
        {
            if (value)
            {
                IsLinearMode = false;
            }
            
            // Update preview numbers when mode changes
            OnStartNumberChanged(StartNumber);
        }

        // Current job ID for cycle printing
        [ObservableProperty]
        private string? _currentJobId;

        // Pending states for resume
        [ObservableProperty]
        private ObservableCollection<CyclePrintState> _pendingStates = new();

        [ObservableProperty]
        private CyclePrintState? _selectedPendingState;

        [ObservableProperty]
        private ObservableCollection<NumberSlot> _slots = new();

        [ObservableProperty]
        private ObservableCollection<string> _availableFonts = new();

        private int _slotCounter = 0;

        public NumberingWizardViewModel(NumberingService numberingService)
        {
            _numberingService = numberingService;
            
            // Initialize available fonts
            InitializeFonts();
            
            // Initialize grid lines
            InitializeGridLines();
            
            // Subscribe to Slots collection changes for debugging
            Slots.CollectionChanged += (s, e) =>
            {
            };
            
            // Initialize available printers
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                AvailablePrinters.Add(printer);
            }
            
            // Auto-select first printer if available
            if (AvailablePrinters.Count > 0 && string.IsNullOrEmpty(SelectedPrinter))
            {
                SelectedPrinter = AvailablePrinters[0];
            }
            
            // Initialize WorkflowMode properties to ensure UI visibility is correct
            // This is needed because OnWorkflowModeChanged is not called during initialization
            // Use Dispatcher to ensure UI updates happen on the correct thread
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(WorkflowMode));
                OnPropertyChanged(nameof(IsPrepareMode));
                OnPropertyChanged(nameof(IsLayoutMode));
                OnPropertyChanged(nameof(IsExecuteMode));
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Filter and translate tray options for user-friendly display
            // Only show common, practical tray options instead of raw enum values
            // ═══════════════════════════════════════════════════════════════════
            InitializeTrayOptions();
            
            // Set default tray for Original
            OriginalTray = PaperSourceKind.Upper;

            // Load recent projects list
            LoadRecentProjects();

            // Update computed values when properties change
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TotalNumbers) || e.PropertyName == nameof(NumberOfCopies))
                {
                    UpdateComputedValues();
                }
            };

            UpdateComputedValues();
        }

        // Load template/design file
        [RelayCommand]
        private void LoadTemplate()
        {
            // Prevent recursive calls
            if (_isLoadingTemplate)
            {
                return;
            }

            _isLoadingTemplate = true;
            try
            {
                var stackTrace = Environment.StackTrace;

                var dialog = new OpenFileDialog
                {
                    Filter = "Design Files|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.svg|All Files|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    TemplatePath = dialog.FileName;
                    PrintStatus = "تم اختيار التصميم";
                }
                else
                {
                }
            }
            finally
            {
                _isLoadingTemplate = false;
            }
        }
        
        partial void OnTemplatePathChanged(string? value)
        {
            // Load and display template image
            if (!string.IsNullOrEmpty(value) && File.Exists(value))
            {
                try
                {
                    // Use TemplateRenderer to convert file to BitmapSource
                    var bitmap = TemplateRenderer.RenderPreview(value);
                    TemplateImage = bitmap;

                    // Update canvas dimensions based on template
                    CanvasWidth = bitmap.PixelWidth;
                    CanvasHeight = bitmap.PixelHeight;

                    // Reset live preview so template shows fresh
                    LivePreviewImage = null;
                    SchedulePreviewUpdate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطأ في تحميل التصميم: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    TemplateImage = null;
                }
            }
            else
            {
                TemplateImage = null;
                LivePreviewImage = null;
            }
        }

        private void UpdateComputedValues()
        {
            // Update from NumberOfCopies (single input)
            // NumberOfCopies can be 1, 2, or 3
            int numCopies = Math.Max(1, Math.Min(3, NumberOfCopies));
            CopiesCount = numCopies;
            
            // Update UseCopy1, UseCopy2, UseCopy3 based on NumberOfCopies
            UseCopy1 = numCopies >= 2;
            UseCopy2 = numCopies >= 3;
            UseCopy3 = false; // Only support up to 3 copies (Original + 2 copies)
            
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Correct calculation for TotalPagesComputed
            // Formula: (TotalNumbers / SlotsPerPage) * CopiesCount
            // This ensures accurate page count for both Linear and Cutting modes
            // ═══════════════════════════════════════════════════════════════════
            int slotsPerPage = Slots?.Count ?? 1;
            if (slotsPerPage == 0) slotsPerPage = 1; // Prevent division by zero
            
            long pagesPerCopy = (long)Math.Ceiling((double)TotalNumbers / slotsPerPage);
            TotalPagesComputed = pagesPerCopy * CopiesCount;
            
            OnPropertyChanged(nameof(HasMultipleCopies));
        }

        partial void OnNumberOfCopiesChanged(int value)
        {
            // Clamp value between 1 and 3
            if (value < 1) NumberOfCopies = 1;
            else if (value > 3) NumberOfCopies = 3;
            
            UpdateComputedValues();
        }

        // Computed property for tray selection visibility
        public bool HasMultipleCopies => CopiesCount > 1;

        [RelayCommand]
        private async Task StartPrint()
        {

            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show("يرجى اختيار طابعة", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TotalNumbers <= 0)
            {
                MessageBox.Show("يرجى إدخال عدد صحيح من الأرقام", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsPrinting = true;
            PrintStatus = "جاري التحضير...";
            PrintProgress = 0;

            try
            {
                if (UseCycleBasedPrinting)
                {
                    await ExecuteCyclePrintJob();
                }
                else
                {
                    await ExecuteTraditionalPrintJob();
                }
            }
            catch (OperationCanceledException)
            {
                PrintStatus = "تم إلغاء الطباعة";
                RecordPrintHistory((long)(PrintProgress / 100.0 * TotalPagesComputed), "ملغي");
            }
            catch (Exception ex)
            {
                // Show user-friendly error message
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                    errorMessage += $"\n\nتفاصيل: {ex.InnerException.Message}";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"حدث خطأ أثناء الطباعة:\n{errorMessage}", "خطأ في الطباعة", MessageBoxButton.OK, MessageBoxImage.Error);
                });

                PrintStatus = $"خطأ: {ex.Message}";
                RecordPrintHistory((long)(PrintProgress / 100.0 * TotalPagesComputed), "خطأ");
            }
            finally
            {
                // Record successful completion
                if (PrintProgress >= 99.9)
                    RecordPrintHistory(TotalPagesComputed, "مكتمل");

                IsPrinting = false;
                if (_cts != null)
                {
                    try { _cts.Dispose(); } catch { }
                    _cts = null;
                }
            }
        }

        private async Task ExecuteTraditionalPrintJob()
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show("يرجى اختيار طابعة", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(TemplatePath))
            {
                MessageBox.Show("يرجى تحديد مسار القالب", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var slots = Slots.Select(s => s.ToSlotSpec()).ToList();
            if (slots.Count == 0)
            {
                MessageBox.Show("يرجى إضافة slot واحد على الأقل", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PrintStatus = "جاري الطباعة...";
            PrintProgress = 0;

            try
            {
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                var progress = new Progress<Apex.NumberedBooksEngine.Models.ProgressInfo>(info =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PrintProgress = info.Percent;
                        PrintStatus = $"جاري الطباعة... {info.Percent:F0}%";
                    });
                });

                // Build tray mapping for traditional printing
                var trayMapping = new Dictionary<int, PaperSourceKind>();
                
                // Original (Copy 0) - allow user to select tray
                trayMapping[0] = OriginalTray;
                
                // Copy 1 (الصورة 1)
                if (UseCopy1 && Copy1Tray.HasValue)
                {
                    trayMapping[1] = Copy1Tray.Value;
                }
                
                // Copy 2 (الصورة 2)
                if (UseCopy2 && Copy2Tray.HasValue)
                {
                    trayMapping[2] = Copy2Tray.Value;
                }
                
                // Copy 3 (not used, but keep for compatibility)
                if (UseCopy3 && Copy3Tray.HasValue)
                {
                    trayMapping[3] = Copy3Tray.Value;
                }

                // Determine NumberingMode based on IsLinearMode/IsImposedMode
                NumberingMode numberingMode = NumberingMode.Auto;
                if (IsLinearMode)
                {
                    numberingMode = NumberingMode.Linear;
                }
                else if (IsImposedMode)
                {
                    numberingMode = NumberingMode.Imposed;
                }
                
                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL FIX: Pass UseSmartTrayPrinting to control printing mode
                // true = Smart Printing (Interleaved: Page→Copies)
                // false = Traditional Printing (Batch: Copy→Pages)
                // ═══════════════════════════════════════════════════════════════════
                var result = await _numberingService.RunStreamingJobAsync(
                    SelectedPrinter,
                    TemplatePath,
                    slots,
                    StartNumber,
                    TotalNumbers,
                    CopiesCount,
                    trayMapping,
                    PrintScaleMode,
                    progress,
                    ct,
                    numberingMode,
                    useSmartPrinting: UseSmartTrayPrinting);  // Pass printing mode

                if (result.Success)
                {
                    PrintStatus = "اكتملت الطباعة بنجاح";
                    PrintProgress = 100;
                    MessageBox.Show("اكتملت الطباعة بنجاح!", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var errorText = (result.Errors != null && result.Errors.Count > 0)
                        ? string.Join("; ", result.Errors)
                        : "سبب غير معروف";
                    PrintStatus = $"خطأ في الطباعة: {errorText}";
                    MessageBox.Show($"خطأ في الطباعة: {errorText}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                PrintStatus = "تم إلغاء الطباعة";
            }
            catch (Exception ex)
            {
                PrintStatus = $"خطأ: {ex.Message}";
                MessageBox.Show($"حدث خطأ أثناء الطباعة:\n{ex.Message}", "خطأ في الطباعة", MessageBoxButton.OK, MessageBoxImage.Error);
                // Don't re-throw - handle error gracefully without crashing
            }
        }

        private async Task ExecuteCyclePrintJob()
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
                return;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // Build tray mapping
            var trayMapping = new Dictionary<int, PaperSourceKind>();
            
            // Original (Copy 0) - allow user to select tray
            trayMapping[0] = OriginalTray;

            if (UseCopy1 && Copy1Tray.HasValue)
            {
                trayMapping[1] = Copy1Tray.Value;
            }

            if (UseCopy2 && Copy2Tray.HasValue)
            {
                trayMapping[2] = Copy2Tray.Value;
            }

            if (UseCopy3 && Copy3Tray.HasValue)
            {
                trayMapping[3] = Copy3Tray.Value;
            }

            // Get slots from UI
            var slots = Slots.Select(s => s.ToSlotSpec()).ToList();
            if (slots.Count == 0)
            {
                MessageBox.Show("يرجى إضافة slot واحد على الأقل", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(TemplatePath))
            {
                MessageBox.Show("يرجى تحديد مسار القالب", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Start cycle printing
            PrintStatus = "جاري بدء الطباعة الدورية...";
            TotalCycles = (int)TotalNumbers;
            CurrentCycleNumber = 0;
            CompletedCycles = 0;
            FailedCycles = 0;
            FailedCyclesList.Clear();

            var progress = new Progress<Apex.NumberedBooksEngine.Models.ProgressInfo>(info =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    PrintProgress = info.Percent;
                    PrintStatus = $"جاري الطباعة... {info.Percent:F0}%";
                });
            });

            // Start monitoring in parallel
            _monitorTask = MonitorCycleProgressAsync(progress);

            try
            {
                var result = await _numberingService.RunCyclePrintAsync(
                    SelectedPrinter,
                    TemplatePath,
                    slots,
                    StartNumber,
                    TotalNumbers,
                    CopiesCount,
                    trayMapping,
                    300,
                    ct);

                CurrentJobId = result.JobId;

                // Cancel monitor after completion
                _cts?.Cancel();

                if (result.Success)
                {
                    PrintStatus = "اكتملت الطباعة بنجاح";
                    PrintProgress = 100;
                    MessageBox.Show($"اكتملت الطباعة بنجاح!\nتم طباعة {result.Completed} دورة.", 
                        "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    PrintStatus = $"اكتملت مع أخطاء: {result.Failed} دورة فاشلة";
                    MessageBox.Show($"اكتملت الطباعة مع أخطاء.\nنجحت: {result.Completed}\nفشلت: {result.Failed}", 
                        "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                PrintStatus = "تم إلغاء الطباعة";
                MessageBox.Show("تم إلغاء الطباعة", "إلغاء", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                PrintStatus = $"خطأ: {ex.Message}";
                MessageBox.Show($"حدث خطأ أثناء الطباعة الدورية:\n{ex.Message}", "خطأ في الطباعة", MessageBoxButton.OK, MessageBoxImage.Error);
                // Don't re-throw - handle error gracefully without crashing
            }
            finally
            {
                if (_monitorTask != null)
                {
                    try
                    {
                        await _monitorTask;
                    }
                    catch { /* Ignore */ }
                    _monitorTask = null;
                }
            }
        }

        private async Task MonitorCycleProgressAsync(IProgress<Apex.NumberedBooksEngine.Models.ProgressInfo> progress)
        {
            try
            {
                while (!_cts?.Token.IsCancellationRequested ?? false)
                {
                    var cycles = _numberingService.GetAllCycles();
                    if (cycles != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var completed = cycles.Count(c => c.Status == CycleStatus.CompletedPhysical);
                            var failed = cycles.Count(c => c.Status == CycleStatus.Failed);
                            var current = cycles.FirstOrDefault(c => c.Status == CycleStatus.Printing || c.Status == CycleStatus.Retrying);

                            CompletedCycles = completed;
                            FailedCycles = failed;
                            CurrentCycleNumber = current?.CycleNumber ?? 0;
                            CurrentCycleStatus = current?.Status.ToString() ?? "";

                            // Update failed cycles list
                            var failedList = cycles.Where(c => c.Status == CycleStatus.Failed)
                                .Select(c => new CycleJobInfo
                                {
                                    JobId = c.JobId,
                                    CycleNumber = c.CycleNumber,
                                    Number = c.StartNumber,
                                    Status = c.Status,
                                    ErrorMessage = c.ErrorMessage,
                                    StartedAt = c.StartedAtUtc,
                                    CompletedAt = c.CompletedAtUtc
                                }).ToList();

                            FailedCyclesList.Clear();
                            foreach (var item in failedList)
                            {
                                FailedCyclesList.Add(item);
                            }

                            // Update progress
                            if (TotalCycles > 0)
                            {
                                var progressValue = ((double)(completed + failed) / TotalCycles) * 100;
                                PrintProgress = progressValue;
                            }
                        });
                    }

                    await Task.Delay(500, _cts?.Token ?? CancellationToken.None);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
        }

        [RelayCommand]
        private void PauseResume()
        {
            if (_numberingService.IsCyclePrintingPaused)
            {
                _numberingService.ResumeCyclePrinting();
                PrintStatus = "جاري الاستئناف...";
            }
            else
            {
                _numberingService.PauseCyclePrinting();
                PrintStatus = "متوقف مؤقتاً";
            }
        }

        [RelayCommand]
        private async Task RetryCycle()
        {
            if (SelectedFailedCycle == null)
            {
                MessageBox.Show("يرجى اختيار دورة فاشلة لإعادة المحاولة", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var success = _numberingService.RetryCycle(SelectedFailedCycle.JobId);
            if (success)
            {
                PrintStatus = $"جاري إعادة محاولة الدورة {SelectedFailedCycle.CycleNumber}...";
                MessageBox.Show($"تم بدء إعادة محاولة الدورة {SelectedFailedCycle.CycleNumber}", 
                    "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("فشل في إعادة المحاولة. تأكد من أن الدورة في حالة فاشلة.", 
                    "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task SkipCycle()
        {
            if (SelectedFailedCycle == null)
            {
                MessageBox.Show("يرجى اختيار دورة فاشلة للتخطي", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"هل تريد تخطي الدورة {SelectedFailedCycle.CycleNumber}؟\nسيتم تخطي هذه الدورة والمتابعة إلى التالية.",
                "تأكيد التخطي",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var success = _numberingService.SkipCycle(SelectedFailedCycle.JobId, "تم التخطي بواسطة المستخدم");
                if (success)
                {
                    PrintStatus = $"تم تخطي الدورة {SelectedFailedCycle.CycleNumber}";
                    MessageBox.Show($"تم تخطي الدورة {SelectedFailedCycle.CycleNumber}", 
                        "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("فشل في تخطي الدورة.", 
                        "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            await Task.CompletedTask;
        }

        [RelayCommand]
        private void PauseCyclePrinting()
        {
            _numberingService.PauseCyclePrinting();
            PrintStatus = "متوقف مؤقتاً";
        }

        [RelayCommand]
        private void ResumeCyclePrinting()
        {
            _numberingService.ResumeCyclePrinting();
            PrintStatus = "جاري الاستئناف...";
        }

        [RelayCommand]
        private async Task ResumeFromState()
        {
            if (SelectedPendingState == null)
            {
                MessageBox.Show("يرجى اختيار حالة محفوظة للاستئناف", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"هل تريد الاستئناف من الحالة المحفوظة؟\nالطابعة: {SelectedPendingState.PrinterName}\nالرقم الأول: {SelectedPendingState.StartNumber}\nإجمالي الأرقام: {SelectedPendingState.TotalNumbers}",
                "تأكيد الاستئناف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                IsPrinting = true;
                PrintStatus = "جاري الاستئناف من الحالة المحفوظة...";
                PrintProgress = 0;

                _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                try
                {
                    var resumeResult = await _numberingService.ResumeCyclePrintAsync(SelectedPendingState.JobId, ct);
                    CurrentJobId = resumeResult.JobId;

                    // Restore template path and slots from state
                    TemplatePath = SelectedPendingState.TemplatePath;
                    Slots.Clear();
                    foreach (var slot in SelectedPendingState.Slots)
                    {
                        Slots.Add(NumberSlot.FromSlotSpec(slot));
                    }

                    // Start monitoring
                    _monitorTask = MonitorCycleProgressAsync(new Progress<Apex.NumberedBooksEngine.Models.ProgressInfo>(info =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            PrintProgress = info.Percent;
                            PrintStatus = $"جاري الاستئناف... {info.Percent:F0}%";
                        });
                    }));

                    if (resumeResult.Success)
                    {
                        PrintStatus = "اكتمل الاستئناف بنجاح";
                        PrintProgress = 100;
                        MessageBox.Show($"اكتمل الاستئناف بنجاح!\nتم طباعة {resumeResult.Completed} دورة.",
                            "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        PrintStatus = $"اكتمل الاستئناف مع أخطاء: {resumeResult.Failed} دورة فاشلة";
                        MessageBox.Show($"اكتمل الاستئناف مع أخطاء.\nنجحت: {resumeResult.Completed}\nفشلت: {resumeResult.Failed}",
                            "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (OperationCanceledException)
                {
                    PrintStatus = "تم إلغاء الاستئناف";
                    MessageBox.Show("تم إلغاء الاستئناف", "إلغاء", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    PrintStatus = $"خطأ: {ex.Message}";
                    MessageBox.Show($"خطأ في الاستئناف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsPrinting = false;
                    if (_cts != null)
                    {
                        _cts.Dispose();
                        _cts = null;
                    }
                    if (_monitorTask != null)
                    {
                        try
                        {
                            await _monitorTask;
                        }
                        catch { /* Ignore */ }
                        _monitorTask = null;
                    }
                }
            }
        }

        [RelayCommand]
        private async Task LoadPendingStates()
        {
            try
            {
                var states = await _numberingService.GetPendingStatesAsync();
                PendingStates.Clear();
                foreach (var state in states)
                {
                    PendingStates.Add(state);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل الحالات المحفوظة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Navigate to Prepare mode (Home tab)
        /// </summary>
        [RelayCommand]
        private void NavigateToPrepare()
        {
            
            _navigationHistory.Push(WorkflowMode);
            WorkflowMode = WorkflowMode.Prepare;
        }
        
        /// <summary>
        /// Navigate to Layout mode (Design tab)
        /// </summary>
        [RelayCommand]
        private void NavigateToLayout()
        {
            
            if (string.IsNullOrEmpty(TemplatePath))
            {
                MessageBox.Show("يرجى إدراج التصميم أولاً", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            _navigationHistory.Push(WorkflowMode);
            WorkflowMode = WorkflowMode.Design;
        }
        
        [RelayCommand]
        private void StartLayout()
        {

            if (string.IsNullOrEmpty(TemplatePath))
            {
                MessageBox.Show("يرجى إدراج التصميم أولاً", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Save current mode to navigation history for back button
            // ═══════════════════════════════════════════════════════════════════
            _navigationHistory.Push(WorkflowMode);
            WorkflowMode = WorkflowMode.Design;
            
        }

        [RelayCommand]
        private void NavigateToExecute()
        {

            // Check printer first (most important)
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show("يرجى اختيار طابعة", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(TemplatePath))
            {
                MessageBox.Show("يرجى تحديد مسار القالب أولاً", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Slots == null || Slots.Count == 0)
            {
                MessageBox.Show("يرجى إضافة slot واحد على الأقل", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Save current mode to navigation history for back button
            // ═══════════════════════════════════════════════════════════════════
            _navigationHistory.Push(WorkflowMode);
            WorkflowMode = WorkflowMode.Print;
        }

        /// <summary>
        /// Navigate back to previous workflow mode
        /// </summary>
        [RelayCommand]
        private void NavigateBack()
        {
            if (_navigationHistory.Count > 0)
            {
                var previousMode = _navigationHistory.Pop();
                WorkflowMode = previousMode;
            }
            else
            {
                // If no history, go to Prepare mode
                WorkflowMode = WorkflowMode.Prepare;
            }
        }

        [RelayCommand]
        private void AddSlot()
        {

            if (WorkflowMode != WorkflowMode.Design)
            {
                MessageBox.Show("يرجى الانتقال إلى وضع التصميم أولاً", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveUndoState(); // snapshot before adding
            _slotCounter++;
            
            // Position each new slot slightly offset from the previous
            float yOffset = 0.1f + ((_slotCounter - 1) * 0.08f) % 0.6f;
            
            var newSlot = new NumberSlot
            {
                Id = $"Slot {_slotCounter}",
                X = 0.1f,
                Y = yOffset,
                Width = 0.3f,  // Increased width for better visibility
                Height = 0.08f,  // Increased height for better visibility
                FontFamily = "Arial",
                FontSize = 24,
                FontColor = "#000000",
                PreviewNumber = CalculatePreviewNumber(StartNumber, Slots?.Count ?? 0),  // Use actual slot index (after adding this slot)
                IsSelected = true,  // Select new slot by default so it's visible
                IsBold = false,
                Rotation = 0,
                Opacity = 1.0,
                Alignment = "Center"
            };
            
            if (Slots == null)
            {
                Slots = new ObservableCollection<NumberSlot>();
            }
            
            Slots.Add(newSlot);
            SelectedSlot = newSlot;
            
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL: Refresh ALL preview numbers after adding a new slot
            // Because Imposed mode calculation depends on total slot count
            // ═══════════════════════════════════════════════════════════════════
            RefreshAllPreviewNumbers();
            
            // Force UI update
            OnPropertyChanged(nameof(Slots));
            OnPropertyChanged(nameof(SelectedSlot));
            
            // Force collection change notification
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                OnPropertyChanged(nameof(Slots));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        [RelayCommand]
        private void RemoveSlot(NumberSlot? slot)
        {
            if (slot != null && Slots.Contains(slot))
            {
                SaveUndoState(); // snapshot before removing
                Slots.Remove(slot);
                if (SelectedSlot == slot)
                {
                    SelectedSlot = Slots.FirstOrDefault();
                }
                
                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL: Refresh ALL preview numbers after removing a slot
                // Because Imposed mode calculation depends on total slot count
                // ═══════════════════════════════════════════════════════════════════
                RefreshAllPreviewNumbers();
                
            }
        }

        [RelayCommand]
        private void DeleteSelectedSlot()
        {

            if (SelectedSlot != null && Slots.Contains(SelectedSlot))
            {
                SaveUndoState(); // snapshot before deleting
                var slotToRemove = SelectedSlot;
                Slots.Remove(slotToRemove);
                SelectedSlot = Slots.FirstOrDefault();
                
                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL: Refresh ALL preview numbers after removing a slot
                // Because Imposed mode calculation depends on total slot count
                // ═══════════════════════════════════════════════════════════════════
                RefreshAllPreviewNumbers();
                
            }
            else
            {
            }
        }

        [RelayCommand]
        private void ZoomIn()
        {

            ZoomLevel = Math.Min(ZoomLevel + 0.1, 3.0);
            
        }

        [RelayCommand]
        private void ZoomOut()
        {

            ZoomLevel = Math.Max(ZoomLevel - 0.1, 0.1);
            
        }

        [RelayCommand]
        private void FitToScreen()
        {

            // Calculate fit-to-screen zoom (assuming viewport is approximately 800x600)
            double viewportWidth = 800;
            double viewportHeight = 600;
            double scaleX = viewportWidth / CanvasWidth;
            double scaleY = viewportHeight / CanvasHeight;
            ZoomLevel = Math.Min(scaleX, scaleY) * 0.9; // 90% to leave some margin
            
        }

        /// <summary>
        /// ═══════════════════════════════════════════════════════════════════
        /// CRITICAL FIX: Initialize user-friendly tray options
        /// Filters raw PaperSourceKind enum values to practical options
        /// ═══════════════════════════════════════════════════════════════════
        /// </summary>
        private void InitializeTrayOptions()
        {
            AvailableTrayOptions.Clear();
            
            // User-friendly tray labels mapped to PaperSourceKind
            AvailableTrayOptions.Add(new TrayOption("Tray 1", PaperSourceKind.Upper));
            AvailableTrayOptions.Add(new TrayOption("Tray 2", PaperSourceKind.Lower));
            AvailableTrayOptions.Add(new TrayOption("Tray 3", PaperSourceKind.Middle));
            AvailableTrayOptions.Add(new TrayOption("Manual Feed", PaperSourceKind.Manual));
        }
        
        private void InitializeFonts()
        {

            AvailableFonts.Clear();
            var fonts = System.Windows.Media.Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(f => f)
                .ToList();
            
            foreach (var font in fonts)
            {
                AvailableFonts.Add(font);
            }
        }

        private void InitializeGridLines()
        {

            GridLines.Clear();
            
            // 0.5 cm spacing = ~18.9 pixels at 96 DPI
            double spacing = 18.9;
            
            // Vertical lines
            for (double x = 0; x <= CanvasWidth; x += spacing)
            {
                GridLines.Add(new GridLine
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = CanvasHeight
                });
            }
            
            // Horizontal lines
            for (double y = 0; y <= CanvasHeight; y += spacing)
            {
                GridLines.Add(new GridLine
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = CanvasWidth,
                    Y2 = y
                });
            }
            
        }

        partial void OnCanvasWidthChanged(double value)
        {
            // Recalculate grid lines when canvas size changes
            InitializeGridLines();
        }

        partial void OnCanvasHeightChanged(double value)
        {
            // Recalculate grid lines when canvas size changes
            InitializeGridLines();
        }

        /// <summary>
        /// Calculates preview number for a slot based on numbering mode
        /// For preview, we show the first page (pageIndex = 0)
        /// </summary>
        private string CalculatePreviewNumber(long startNumber, int slotIndex)
        {
            long previewNum;
            
            // Get actual slot count (use at least 1 to avoid division by zero)
            int slotCount = Math.Max(Slots?.Count ?? 1, 1);
            
            if (IsLinearMode)
            {
                // Linear mode: sequential numbers (1, 2, 3, 4...)
                // Page 1: [1, 2, 3, 4]
                previewNum = startNumber + slotIndex;
            }
            else if (IsImposedMode)
            {
                // ═══════════════════════════════════════════════════════════════════
                // Imposed mode (cutting): numbers that stack correctly after cutting
                // Formula: value = start + sheetIndex + (slotIndex * totalSheets)
                // For preview (first sheet), sheetIndex = 0
                // 
                // Example with 6 slots, 1000 numbers (167 sheets):
                // Sheet 0: [1, 168, 335, 502, 669, 836]
                // Sheet 1: [2, 169, 336, 503, 670, 837]
                // After cutting: Slot0 pile = 1,2,3..., Slot1 pile = 168,169,170...
                // ═══════════════════════════════════════════════════════════════════
                long totalSheets = (long)Math.Ceiling((double)TotalNumbers / slotCount);
                long sheetIndex = 0; // Preview shows first sheet
                previewNum = startNumber + sheetIndex + (slotIndex * totalSheets);
                
                // Ensure preview number doesn't exceed the valid range
                if (previewNum >= startNumber + TotalNumbers)
                {
                    previewNum = -1; // Mark as empty
                }
            }
            else
            {
                // Default: sequential
                previewNum = startNumber + slotIndex;
            }
            
            // Format: return empty string for invalid numbers
            if (previewNum < 0)
            {
                return "----";
            }
            
            return previewNum.ToString("D4");
        }

        // ══════════════════════════════════════════════════════════════
        //  PROGRESS DISPLAY  (شريط التقدم الموسّع)
        // ══════════════════════════════════════════════════════════════

        private System.Threading.Timer? _elapsedTimer;
        private DateTime _printStartTime;

        /// <summary>الوقت المنقضي منذ بدء الطباعة</summary>
        [ObservableProperty]
        private string _elapsedTimeDisplay = "";

        /// <summary>الصفحات المطبوعة / الإجمالي</summary>
        [ObservableProperty]
        private string _printedPagesDisplay = "";

        /// <summary>السرعة التقديرية بالصفحة/دقيقة</summary>
        [ObservableProperty]
        private string _printSpeedDisplay = "";

        partial void OnIsPrintingChanged(bool value)
        {
            if (value)
            {
                _printStartTime = DateTime.Now;
                _elapsedTimer = new System.Threading.Timer(
                    _ => UpdateElapsedAndSpeed(),
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1));
                UpdatePagesDisplay();
            }
            else
            {
                _elapsedTimer?.Dispose();
                _elapsedTimer = null;
                ElapsedTimeDisplay   = "";
                PrintedPagesDisplay  = "";
                PrintSpeedDisplay    = "";
            }
        }

        partial void OnPrintProgressChanged(double value)
        {
            UpdatePagesDisplay();
        }

        private void UpdatePagesDisplay()
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (TotalPagesComputed <= 0) return;
                var printed = (long)(PrintProgress / 100.0 * TotalPagesComputed);
                PrintedPagesDisplay = $"{printed:N0} / {TotalPagesComputed:N0} صفحة";
            });
        }

        private void UpdateElapsedAndSpeed()
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var elapsed = DateTime.Now - _printStartTime;

                // Elapsed
                ElapsedTimeDisplay = elapsed.TotalSeconds < 60
                    ? $"{(int)elapsed.TotalSeconds:D2}ث"
                    : $"{(int)elapsed.TotalMinutes}د {elapsed.Seconds:D2}ث";

                // Speed
                if (elapsed.TotalMinutes > 0 && PrintProgress > 0 && TotalPagesComputed > 0)
                {
                    var printed        = PrintProgress / 100.0 * TotalPagesComputed;
                    var pagesPerMin    = printed / elapsed.TotalMinutes;
                    var remaining      = TotalPagesComputed - printed;
                    var etaMinutes     = pagesPerMin > 0 ? remaining / pagesPerMin : 0;
                    PrintSpeedDisplay  = etaMinutes > 1
                        ? $"متبقٍ ~{(int)etaMinutes}د"
                        : etaMinutes > 0
                            ? "متبقٍ < دقيقة"
                            : "";
                }
            });
        }

        /// <summary>إلغاء الطباعة الجارية</summary>
        [RelayCommand]
        private void CancelPrint()
        {
            if (_cts == null) return;
            try
            {
                _cts.Cancel();
                PrintStatus = "جارٍ الإيقاف...";
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════
        //  LIVE PREVIEW  (معاينة فورية تلقائية)
        // ══════════════════════════════════════════════════════════════

        [ObservableProperty] private BitmapSource? _livePreviewImage;
        [ObservableProperty] private bool _isGeneratingPreview = false;
        [ObservableProperty] private bool _autoPreviewEnabled = true;

        private System.Threading.Timer? _previewDebounceTimer;
        private const int PreviewDebounceMs = 800;

        private void SchedulePreviewUpdate()
        {
            if (!AutoPreviewEnabled) return;
            if (string.IsNullOrEmpty(TemplatePath) || !File.Exists(TemplatePath)) return;
            if (Slots.Count == 0) return;

            _previewDebounceTimer?.Dispose();
            _previewDebounceTimer = new System.Threading.Timer(
                state => { var t = GenerateLivePreviewAsync(); },
                null,
                PreviewDebounceMs,
                Timeout.Infinite);
        }

        private async Task GenerateLivePreviewAsync()
        {
            if (string.IsNullOrEmpty(TemplatePath) || !File.Exists(TemplatePath)) return;
            if (IsGeneratingPreview) return;

            await Application.Current?.Dispatcher.InvokeAsync(() => IsGeneratingPreview = true);
            try
            {
                var slots = Slots.Select(s => s.ToSlotSpec()).ToList();
                if (slots.Count == 0) return;

                var isPdf = TemplatePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                var format = isPdf ? TemplateFormat.Pdf : TemplateFormat.Image;

                List<SkiaSharp.SKImage> pages;
                using (var stream = File.OpenRead(TemplatePath))
                {
                    pages = _numberingService.GeneratePreviewPages(stream, slots, StartNumber, 1, format);
                }

                if (pages.Count > 0)
                {
                    var bmp = SKImageExtensions.ToBitmapSource(pages[0]);
                    await Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        LivePreviewImage = bmp;
                        IsGeneratingPreview = false;
                    });
                }
            }
            catch
            {
                await Application.Current?.Dispatcher.InvokeAsync(() => IsGeneratingPreview = false);
            }
        }

        [RelayCommand]
        private void ToggleAutoPreview()
        {
            AutoPreviewEnabled = !AutoPreviewEnabled;
            if (AutoPreviewEnabled) SchedulePreviewUpdate();
            else LivePreviewImage = null;
        }

        [RelayCommand]
        private async Task RefreshPreviewNow()
        {
            _previewDebounceTimer?.Dispose();
            await GenerateLivePreviewAsync();
        }

        // ══════════════════════════════════════════════════════════════
        //  SAVE / LOAD PROJECT  (حفظ وتحميل إعدادات المشروع .apex-job)
        // ══════════════════════════════════════════════════════════════

        [ObservableProperty] private string _currentProjectPath = "";

        [RelayCommand]
        private void SaveProject()
        {
            var dlg = new SaveFileDialog
            {
                Title = "حفظ مشروع الترقيم",
                Filter = "Apex Job|*.apex-job",
                DefaultExt = ".apex-job",
                FileName = "numbering-project"
            };
            if (dlg.ShowDialog() != true) return;

            var project = new NumberingProjectFile
            {
                TemplatePath         = TemplatePath ?? "",
                StartNumber          = StartNumber,
                TotalNumbers         = TotalNumbers,
                NumberOfCopies       = NumberOfCopies,
                IsLinearMode         = IsLinearMode,
                IsImposedMode        = IsImposedMode,
                UseSmartTrayPrinting = UseSmartTrayPrinting,
                SelectedPrinter      = SelectedPrinter ?? "",
                Notes                = ProjectNotes,
                Slots                = Slots.Select(s => new NumberingSlotData
                {
                    Id         = s.Id,
                    X          = s.X,
                    Y          = s.Y,
                    Width      = s.Width,
                    Height     = s.Height,
                    FontFamily = s.FontFamily,
                    FontSize   = s.FontSize,
                    FontColor  = s.FontColor,
                    IsBold     = s.IsBold,
                    Rotation   = s.Rotation,
                    Alignment  = s.Alignment,
                    Opacity    = s.Opacity
                }).ToList()
            };

            var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            CurrentProjectPath = dlg.FileName;
            AddToRecentProjects(dlg.FileName);
            PrintStatus = $"✅ تم حفظ المشروع: {Path.GetFileName(dlg.FileName)}";
        }

        [RelayCommand]
        private void LoadProject()
        {
            var dlg = new OpenFileDialog
            {
                Title = "تحميل مشروع الترقيم",
                Filter = "Apex Job|*.apex-job|All Files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json    = File.ReadAllText(dlg.FileName);
                var project = JsonSerializer.Deserialize<NumberingProjectFile>(json);
                if (project == null) throw new Exception("ملف المشروع تالف أو فارغ");

                TemplatePath         = project.TemplatePath;
                StartNumber          = project.StartNumber;
                TotalNumbers         = project.TotalNumbers;
                NumberOfCopies       = project.NumberOfCopies;
                IsLinearMode         = project.IsLinearMode;
                IsImposedMode        = project.IsImposedMode;
                UseSmartTrayPrinting = project.UseSmartTrayPrinting;

                if (!string.IsNullOrEmpty(project.SelectedPrinter) &&
                    AvailablePrinters.Contains(project.SelectedPrinter))
                    SelectedPrinter = project.SelectedPrinter;

                Slots.Clear();
                foreach (var sd in project.Slots)
                {
                    Slots.Add(new NumberSlot
                    {
                        Id         = sd.Id,
                        X          = sd.X,
                        Y          = sd.Y,
                        Width      = sd.Width,
                        Height     = sd.Height,
                        FontFamily = sd.FontFamily,
                        FontSize   = sd.FontSize,
                        FontColor  = sd.FontColor,
                        IsBold     = sd.IsBold,
                        Rotation   = sd.Rotation,
                        Alignment  = sd.Alignment,
                        Opacity    = sd.Opacity
                    });
                }

                ProjectNotes       = project.Notes ?? "";
                CurrentProjectPath = dlg.FileName;
                AddToRecentProjects(dlg.FileName);
                RefreshAllPreviewNumbers();
                UpdateComputedValues();
                SchedulePreviewUpdate();
                PrintStatus = $"✅ تم تحميل المشروع: {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل المشروع:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  EXPORT / IMPORT DESIGN SETTINGS (.apexnr)
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void ExportDesignSettings()
        {
            if (Slots == null || Slots.Count == 0)
            {
                MessageBox.Show("لا توجد عناصر للتصدير. أضف عناصر في وضع التصميم أولاً.",
                    "تصدير الإعدادات", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title       = "تصدير إعدادات التصميم",
                Filter      = "Apex Numbering Design|*.apexnr",
                DefaultExt  = ".apexnr",
                FileName    = "numbering-design"
            };
            if (dlg.ShowDialog() != true) return;

            var design = new NumberingDesignSettings
            {
                ExportedAt = DateTime.Now,
                Version    = "1.0",
                SlotCount  = Slots.Count,
                Slots      = Slots.Select(s => new NumberingSlotData
                {
                    Id         = s.Id,
                    X          = s.X,
                    Y          = s.Y,
                    Width      = s.Width,
                    Height     = s.Height,
                    FontFamily = s.FontFamily,
                    FontSize   = s.FontSize,
                    FontColor  = s.FontColor,
                    IsBold     = s.IsBold,
                    Rotation   = s.Rotation,
                    Alignment  = s.Alignment,
                    Opacity    = s.Opacity
                }).ToList()
            };

            var json = JsonSerializer.Serialize(design, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            PrintStatus = $"✅ تم تصدير التصميم ({Slots.Count} عنصر): {Path.GetFileName(dlg.FileName)}";
        }

        [RelayCommand]
        private void ImportDesignSettings()
        {
            var dlg = new OpenFileDialog
            {
                Title  = "استيراد إعدادات التصميم",
                Filter = "Apex Numbering Design|*.apexnr|All Files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json   = File.ReadAllText(dlg.FileName);
                var design = JsonSerializer.Deserialize<NumberingDesignSettings>(json);
                if (design?.Slots == null || design.Slots.Count == 0)
                    throw new Exception("ملف الإعدادات تالف أو لا يحتوي على عناصر");

                SaveUndoState(); // Allow undo of the import

                Slots.Clear();
                foreach (var sd in design.Slots)
                {
                    Slots.Add(new NumberSlot
                    {
                        Id         = sd.Id,
                        X          = sd.X,
                        Y          = sd.Y,
                        Width      = sd.Width,
                        Height     = sd.Height,
                        FontFamily = sd.FontFamily,
                        FontSize   = sd.FontSize,
                        FontColor  = sd.FontColor,
                        IsBold     = sd.IsBold,
                        Rotation   = sd.Rotation,
                        Alignment  = sd.Alignment,
                        Opacity    = sd.Opacity
                    });
                }

                SelectedSlot = Slots.FirstOrDefault();
                RefreshAllPreviewNumbers();
                SchedulePreviewUpdate();
                OnPropertyChanged(nameof(Slots));
                PrintStatus = $"✅ تم استيراد {design.Slots.Count} عنصر من: {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في استيراد الإعدادات:\n{ex.Message}",
                    "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  UNDO / REDO  (تراجع / إعادة)  Ctrl+Z / Ctrl+Y
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Save a snapshot of the current slot layout before making a change.
        /// Call this at the START of any destructive design operation.
        /// </summary>
        public void SaveUndoState()
        {
            _undoStack.Push(SnapshotSlots());
            _redoStack.Clear();

            // Cap history size
            while (_undoStack.Count > MaxUndoSteps)
                _undoStack.TryPop(out _);

            CanUndo = _undoStack.Count > 0;
            CanRedo = false;
        }

        [RelayCommand]
        private void Undo()
        {
            if (_undoStack.Count == 0)
            {
                PrintStatus = "↩ لا توجد إجراءات للتراجع عنها";
                return;
            }

            _redoStack.Push(SnapshotSlots());
            var previous = _undoStack.Pop();
            RestoreSlots(previous);

            CanUndo = _undoStack.Count > 0;
            CanRedo = _redoStack.Count > 0;
            PrintStatus = $"↩ تم التراجع (متبقٍّ: {_undoStack.Count} خطوة)";
        }

        [RelayCommand]
        private void Redo()
        {
            if (_redoStack.Count == 0)
            {
                PrintStatus = "↪ لا توجد إجراءات للإعادة";
                return;
            }

            _undoStack.Push(SnapshotSlots());
            var next = _redoStack.Pop();
            RestoreSlots(next);

            CanUndo = _undoStack.Count > 0;
            CanRedo = _redoStack.Count > 0;
            PrintStatus = $"↪ تم الإعادة (متبقٍّ: {_redoStack.Count} خطوة)";
        }

        private List<NumberingSlotData> SnapshotSlots()
        {
            return (Slots ?? Enumerable.Empty<NumberSlot>())
                .Select(s => new NumberingSlotData
                {
                    Id         = s.Id,
                    X          = s.X,
                    Y          = s.Y,
                    Width      = s.Width,
                    Height     = s.Height,
                    FontFamily = s.FontFamily,
                    FontSize   = s.FontSize,
                    FontColor  = s.FontColor,
                    IsBold     = s.IsBold,
                    Rotation   = s.Rotation,
                    Alignment  = s.Alignment,
                    Opacity    = s.Opacity
                }).ToList();
        }

        private void RestoreSlots(List<NumberingSlotData> snapshot)
        {
            Slots.Clear();
            foreach (var sd in snapshot)
            {
                Slots.Add(new NumberSlot
                {
                    Id         = sd.Id,
                    X          = sd.X,
                    Y          = sd.Y,
                    Width      = sd.Width,
                    Height     = sd.Height,
                    FontFamily = sd.FontFamily,
                    FontSize   = sd.FontSize,
                    FontColor  = sd.FontColor,
                    IsBold     = sd.IsBold,
                    Rotation   = sd.Rotation,
                    Alignment  = sd.Alignment,
                    Opacity    = sd.Opacity
                });
            }
            SelectedSlot = Slots.FirstOrDefault();
            RefreshAllPreviewNumbers();
            SchedulePreviewUpdate();
            OnPropertyChanged(nameof(Slots));
        }

        // ══════════════════════════════════════════════════════════════
        //  RESUME FROM CHECKPOINT  (استمرار من نقطة إيقاف)
        // ══════════════════════════════════════════════════════════════

        [ObservableProperty]
        private ObservableCollection<CheckpointRecord> _checkpointStates = new();

        [ObservableProperty]
        private CheckpointRecord? _selectedCheckpoint;

        [RelayCommand]
        private async Task LoadCheckpoints()
        {
            var records = await _checkpointManager.GetPendingCheckpointsAsync();
            CheckpointStates.Clear();
            foreach (var r in records.OrderByDescending(r => r.Timestamp))
                CheckpointStates.Add(r);

            if (CheckpointStates.Count == 0)
                PrintStatus = "لا توجد نقاط إيقاف محفوظة";
            else
                PrintStatus = $"تم العثور على {CheckpointStates.Count} نقطة إيقاف";
        }

        [RelayCommand]
        private void ResumeFromCheckpoint()
        {
            if (SelectedCheckpoint == null) return;
            // Resume = set StartNumber to the number AFTER the last printed
            StartNumber = SelectedCheckpoint.LastPrintedNumber + 1;
            PrintStatus = $"✅ سيتم الاستمرار من الرقم {StartNumber:N0}";
        }

        [RelayCommand]
        private void DeleteCheckpoint()
        {
            if (SelectedCheckpoint == null) return;
            _checkpointManager.DeleteCheckpoint(SelectedCheckpoint.JobId);
            CheckpointStates.Remove(SelectedCheckpoint);
            SelectedCheckpoint = null;
            PrintStatus = "تم حذف نقطة الإيقاف";
        }

        // ══════════════════════════════════════════════════════════════
        //  MEDIUM PRIORITY ①  —  معاينة متعددة الصفحات
        // ══════════════════════════════════════════════════════════════

        [ObservableProperty] private ObservableCollection<BitmapSource> _multiPreviewPages = new();
        [ObservableProperty] private bool _isMultiPreviewOpen = false;
        [ObservableProperty] private bool _isGeneratingMultiPreview = false;

        [RelayCommand]
        private async Task ShowMultiPreview()
        {
            if (string.IsNullOrEmpty(TemplatePath) || !File.Exists(TemplatePath))
            {
                MessageBox.Show("يرجى تحميل ملف تصميم أولاً", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (Slots.Count == 0)
            {
                MessageBox.Show("يرجى إضافة حقل ترقيم على الأقل", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsGeneratingMultiPreview = true;
            IsMultiPreviewOpen = true;
            MultiPreviewPages.Clear();

            try
            {
                var slots  = Slots.Select(s => s.ToSlotSpec()).ToList();
                var isPdf  = TemplatePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                var format = isPdf ? TemplateFormat.Pdf : TemplateFormat.Image;
                var count  = (int)Math.Min(TotalNumbers, 4);

                List<SkiaSharp.SKImage> pages;
                using (var stream = File.OpenRead(TemplatePath))
                    pages = _numberingService.GeneratePreviewPages(stream, slots, StartNumber, count, format);

                foreach (var page in pages)
                {
                    var bmp = SKImageExtensions.ToBitmapSource(page);
                    await Application.Current?.Dispatcher.InvokeAsync(() => MultiPreviewPages.Add(bmp));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في توليد المعاينة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGeneratingMultiPreview = false;
            }
        }

        [RelayCommand]
        private void CloseMultiPreview() => IsMultiPreviewOpen = false;

        // ══════════════════════════════════════════════════════════════
        //  MEDIUM PRIORITY ②  —  إحصائيات الطباعة
        // ══════════════════════════════════════════════════════════════

        /// <summary>عدد الأوراق اللازمة للطباعة (مقدّر)</summary>
        public long PaperSheetsNeeded => TotalPagesComputed > 0 ? TotalPagesComputed : 0;

        /// <summary>وقت الطباعة المقدّر (بافتراض 30 ورقة/دقيقة)</summary>
        public string EstimatedPrintTime
        {
            get
            {
                if (TotalPagesComputed <= 0) return "—";
                const double pagesPerMinute = 30.0;
                var minutes = TotalPagesComputed / pagesPerMinute;
                if (minutes < 1) return "أقل من دقيقة";
                if (minutes < 60) return $"~{(int)Math.Ceiling(minutes)} دقيقة";
                return $"~{(int)(minutes / 60)} ساعة {(int)(minutes % 60)} دقيقة";
            }
        }

        partial void OnTotalPagesComputedChanged(long value)
        {
            OnPropertyChanged(nameof(PaperSheetsNeeded));
            OnPropertyChanged(nameof(EstimatedPrintTime));
        }

        // ══════════════════════════════════════════════════════════════
        //  MEDIUM PRIORITY ③  —  المشاريع الأخيرة
        // ══════════════════════════════════════════════════════════════

        private static readonly string RecentProjectsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "Apex", "recent-numbering-projects.json");

        [ObservableProperty]
        private ObservableCollection<string> _recentProjects = new();

        private void LoadRecentProjects()
        {
            try
            {
                if (!File.Exists(RecentProjectsPath)) return;
                var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentProjectsPath));
                if (list == null) return;
                RecentProjects.Clear();
                foreach (var p in list.Where(File.Exists).Take(8))
                    RecentProjects.Add(p);
            }
            catch { }
        }

        private void AddToRecentProjects(string path)
        {
            try
            {
                var list = RecentProjects.ToList();
                list.Remove(path);
                list.Insert(0, path);
                list = list.Take(8).ToList();
                RecentProjects.Clear();
                foreach (var p in list) RecentProjects.Add(p);

                Directory.CreateDirectory(Path.GetDirectoryName(RecentProjectsPath)!);
                File.WriteAllText(RecentProjectsPath, JsonSerializer.Serialize(list));
            }
            catch { }
        }

        [RelayCommand]
        private void OpenRecentProject(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                RecentProjects.Remove(path ?? "");
                return;
            }
            try
            {
                var json    = File.ReadAllText(path);
                var project = JsonSerializer.Deserialize<NumberingProjectFile>(json);
                if (project == null) return;

                TemplatePath         = project.TemplatePath;
                StartNumber          = project.StartNumber;
                TotalNumbers         = project.TotalNumbers;
                NumberOfCopies       = project.NumberOfCopies;
                IsLinearMode         = project.IsLinearMode;
                IsImposedMode        = project.IsImposedMode;
                UseSmartTrayPrinting = project.UseSmartTrayPrinting;
                ProjectNotes         = project.Notes ?? "";

                if (!string.IsNullOrEmpty(project.SelectedPrinter) &&
                    AvailablePrinters.Contains(project.SelectedPrinter))
                    SelectedPrinter = project.SelectedPrinter;

                Slots.Clear();
                foreach (var sd in project.Slots)
                    Slots.Add(new NumberSlot
                    {
                        Id         = sd.Id,
                        X          = sd.X,
                        Y          = sd.Y,
                        Width      = sd.Width,
                        Height     = sd.Height,
                        FontFamily = sd.FontFamily,
                        FontSize   = sd.FontSize,
                        FontColor  = sd.FontColor,
                        IsBold     = sd.IsBold,
                        Rotation   = sd.Rotation,
                        Alignment  = sd.Alignment,
                        Opacity    = sd.Opacity
                    });

                CurrentProjectPath = path;
                AddToRecentProjects(path);
                RefreshAllPreviewNumbers();
                UpdateComputedValues();
                SchedulePreviewUpdate();
                PrintStatus = $"✅ تم تحميل: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  MEDIUM PRIORITY ④  —  ملاحظات المشروع
        // ══════════════════════════════════════════════════════════════

        [ObservableProperty] private string _projectNotes = "";
        [ObservableProperty] private bool _isNotesOpen = false;

        [RelayCommand]
        private void ToggleNotes() => IsNotesOpen = !IsNotesOpen;

        // ══════════════════════════════════════════════════════════════
        //  LOW PRIORITY ①  —  تكرار الـ Slot (Duplicate Slot)
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void DuplicateSlot()
        {
            if (SelectedSlot == null) return;
            var src = SelectedSlot;
            _slotCounter++;
            var clone = new NumberSlot
            {
                Id         = $"Slot {_slotCounter}",
                X          = Math.Min(src.X + 0.03f, 0.85f),
                Y          = Math.Min(src.Y + 0.03f, 0.85f),
                Width      = src.Width,
                Height     = src.Height,
                FontFamily = src.FontFamily,
                FontSize   = src.FontSize,
                FontColor  = src.FontColor,
                IsBold     = src.IsBold,
                Rotation   = src.Rotation,
                Alignment  = src.Alignment,
                Opacity    = src.Opacity
            };
            Slots.Add(clone);
            SelectedSlot = clone;
            RefreshAllPreviewNumbers();
            SchedulePreviewUpdate();
        }

        // ══════════════════════════════════════════════════════════════
        //  LOW PRIORITY ②  —  سجل الطباعة (Print History)
        // ══════════════════════════════════════════════════════════════

        private static readonly string PrintHistoryPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "Apex", "print-history.json");

        [ObservableProperty]
        private ObservableCollection<PrintHistoryRecord> _printHistory = new();

        [ObservableProperty] private bool _isPrintHistoryOpen = false;

        [RelayCommand]
        private void TogglePrintHistory()
        {
            if (!IsPrintHistoryOpen) LoadPrintHistory();
            IsPrintHistoryOpen = !IsPrintHistoryOpen;
        }

        private void LoadPrintHistory()
        {
            try
            {
                if (!File.Exists(PrintHistoryPath)) return;
                var list = JsonSerializer.Deserialize<List<PrintHistoryRecord>>(
                    File.ReadAllText(PrintHistoryPath));
                if (list == null) return;
                PrintHistory.Clear();
                foreach (var r in list.OrderByDescending(r => r.Timestamp).Take(50))
                    PrintHistory.Add(r);
            }
            catch { }
        }

        internal void RecordPrintHistory(long pagesCount, string status)
        {
            try
            {
                var record = new PrintHistoryRecord
                {
                    Timestamp    = DateTime.Now,
                    StartNumber  = StartNumber,
                    TotalNumbers = TotalNumbers,
                    PagesCount   = pagesCount,
                    Printer      = SelectedPrinter ?? "",
                    Status       = status,
                    ProjectName  = string.IsNullOrEmpty(CurrentProjectPath)
                                    ? "بدون اسم"
                                    : Path.GetFileNameWithoutExtension(CurrentProjectPath)
                };

                // Load existing
                var list = new List<PrintHistoryRecord>();
                if (File.Exists(PrintHistoryPath))
                {
                    try { list = JsonSerializer.Deserialize<List<PrintHistoryRecord>>(File.ReadAllText(PrintHistoryPath)) ?? list; }
                    catch { }
                }
                list.Insert(0, record);
                list = list.Take(100).ToList();

                Directory.CreateDirectory(Path.GetDirectoryName(PrintHistoryPath)!);
                File.WriteAllText(PrintHistoryPath, JsonSerializer.Serialize(list,
                    new JsonSerializerOptions { WriteIndented = true }));

                // Update live if open
                if (IsPrintHistoryOpen)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        PrintHistory.Insert(0, record);
                        if (PrintHistory.Count > 50) PrintHistory.RemoveAt(50);
                    });
                }
            }
            catch { }
        }

        [RelayCommand]
        private void ClearPrintHistory()
        {
            try
            {
                if (File.Exists(PrintHistoryPath)) File.Delete(PrintHistoryPath);
                PrintHistory.Clear();
                PrintStatus = "تم مسح السجل";
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════
        //  LOW PRIORITY ③  —  تصدير المعاينة كـ PNG
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void ExportPreview()
        {
            var image = LivePreviewImage ?? TemplateImage;
            if (image == null)
            {
                MessageBox.Show("لا توجد معاينة لتصديرها — حمّل قالباً أولاً",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title      = "تصدير المعاينة",
                Filter     = "PNG Image|*.png|JPEG Image|*.jpg",
                DefaultExt = ".png",
                FileName   = $"preview-{StartNumber:D6}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var encoder = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? (System.Windows.Media.Imaging.BitmapEncoder)new System.Windows.Media.Imaging.JpegBitmapEncoder()
                    : new System.Windows.Media.Imaging.PngBitmapEncoder();

                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var fs = File.OpenWrite(dlg.FileName);
                encoder.Save(fs);
                PrintStatus = $"✅ تم تصدير المعاينة: {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في التصدير:\n{ex.Message}", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  LOW PRIORITY ④  —  زوم عجلة الماوس (handled in code-behind)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// يُستدعى من code-behind عند Ctrl+Wheel على الـ Canvas
        /// </summary>
        public void ApplyWheelZoom(int delta)
        {
            double step = delta > 0 ? 0.1 : -0.1;
            ZoomLevel = Math.Round(Math.Max(0.1, Math.Min(3.0, ZoomLevel + step)), 1);
        }

        // ══════════════════════════════════════════════════════════════
        //  LOW PRIORITY ⑤  —  اختصارات لوحة المفاتيح (XAML InputBindings)
        // ══════════════════════════════════════════════════════════════
        // Ctrl+S → SaveProjectCommand   (declared above)
        // Ctrl+O → LoadProjectCommand   (declared above)
        // Ctrl+D → DuplicateSlotCommand (declared above)
        // Ctrl+P → StartPrintCommand    (declared above)
        // F5     → RefreshPreviewNowCommand (declared above)
        // Delete → DeleteSelectedSlotCommand (declared above)

        // ══════════════════════════════════════════════════════════════
        //  LOW PRIORITY ⑥  —  قائمة المشاريع الأخيرة (XAML panel)
        // ══════════════════════════════════════════════════════════════
        [ObservableProperty] private bool _isRecentProjectsOpen = false;

        [RelayCommand]
        private void ToggleRecentProjects() => IsRecentProjectsOpen = !IsRecentProjectsOpen;

    }
}

// ══════════════════════════════════════════════════════════════
//  PROJECT FILE MODELS
// ══════════════════════════════════════════════════════════════
namespace Apex.UI.ViewModels
{
    public class NumberingProjectFile
    {
        public string TemplatePath         { get; set; } = "";
        public long   StartNumber          { get; set; } = 1;
        public long   TotalNumbers         { get; set; } = 100;
        public int    NumberOfCopies       { get; set; } = 1;
        public bool   IsLinearMode         { get; set; } = true;
        public bool   IsImposedMode        { get; set; } = false;
        public bool   UseSmartTrayPrinting { get; set; } = false;
        public string SelectedPrinter      { get; set; } = "";
        public string? Notes               { get; set; }
        public List<NumberingSlotData> Slots { get; set; } = new();
    }

    public class NumberingSlotData
    {
        public string Id         { get; set; } = "";
        public float  X          { get; set; }
        public float  Y          { get; set; }
        public float  Width      { get; set; } = 0.1f;
        public float  Height     { get; set; } = 0.05f;
        public string FontFamily { get; set; } = "Arial";
        public float  FontSize   { get; set; } = 24;
        public string FontColor  { get; set; } = "#000000";
        public bool   IsBold     { get; set; }
        public double Rotation   { get; set; }
        public string Alignment  { get; set; } = "Left";
        public double Opacity    { get; set; } = 1.0;
    }

    /// <summary>
    /// Lightweight export format for slot design only (.apexnr).
    /// Does NOT include template path or print settings — just the visual layout.
    /// </summary>
    public class NumberingDesignSettings
    {
        public string   Version    { get; set; } = "1.0";
        public DateTime ExportedAt { get; set; } = DateTime.Now;
        public int      SlotCount  { get; set; }
        public List<NumberingSlotData> Slots { get; set; } = new();
    }

    public class PrintHistoryRecord
    {
        public DateTime Timestamp    { get; set; } = DateTime.Now;
        public long     StartNumber  { get; set; }
        public long     TotalNumbers { get; set; }
        public long     PagesCount   { get; set; }
        public string   Printer      { get; set; } = "";
        public string   Status       { get; set; } = "";
        public string   ProjectName  { get; set; } = "";
    }
}

