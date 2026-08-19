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
                    return L("Num_SmartDesc");
                }
                else
                {
                    return L("Num_TraditionalDesc");
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
                return UseSmartTrayPrinting ? L("Num_SmartMode") : L("Num_TraditionalMode");
            }
        }

        partial void OnUseSmartTrayPrintingChanged(bool value)
        {
            OnPropertyChanged(nameof(PrintingModeDescription));
            OnPropertyChanged(nameof(PrintingModeName));
        }

        [ObservableProperty]
        private string _printStatus = L("Num_Ready");

        [ObservableProperty]
        private double _printProgress = 0;

        // The serial coming off the press right now — the hero readout during a run,
        // fed live from ProgressInfo.LastNumber so an operator can glance from across
        // the shop and know exactly where the job is.
        [ObservableProperty]
        private string _currentNumberDisplay = "";

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

        /// <summary>Register of already-issued numbers (duplicate prevention + audit).</summary>
        private readonly Apex.NumberedBooksEngine.Core.NumberRegistry _numberRegistry = new();

        /// <summary>Next free number in the current series — offered to the operator.</summary>
        public long SuggestedNextNumber => _numberRegistry.NextAvailable(NumberPrefix);

        [RelayCommand]
        private void UseNextAvailableNumber() => StartNumber = SuggestedNextNumber;

        // ── Issued-numbers register (audit view) ─────────────────────────────

        /// <summary>Ranges already issued in the current series, newest first.</summary>
        public ObservableCollection<string> IssuedRanges { get; } = new();

        /// <summary>Unused stretches an auditor would ask about.</summary>
        public ObservableCollection<string> NumberGaps { get; } = new();

        [ObservableProperty] private string _registrySummary = "";
        [ObservableProperty] private bool _hasRegistryEntries;

        /// <summary>
        /// Loads the register for the current series so the operator can see what was
        /// already printed — and answer "why is 250–300 missing?" before being asked.
        /// </summary>
        [RelayCommand]
        private void RefreshRegistry()
        {
            IssuedRanges.Clear();
            NumberGaps.Clear();

            var fmt = new Apex.NumberedBooksEngine.Core.NumberFormatOptions(
                PadDigits: NumberPadDigits,
                Prefix: NumberPrefix?.Trim() ?? "",
                Suffix: NumberSuffix?.Trim() ?? "",
                UseArabicDigits: UseArabicDigits);

            var records = _numberRegistry.GetSeries(NumberPrefix);
            foreach (var r in records.OrderByDescending(r => r.PrintedUtc))
                IssuedRanges.Add(Apex.NumberedBooksEngine.Core.NumberRegistry.Describe(r, fmt));

            foreach (var (from, to) in _numberRegistry.FindGaps(NumberPrefix))
                NumberGaps.Add($"{Apex.NumberedBooksEngine.Core.NumberFormatter.Format(from, fmt)}" +
                               $" – {Apex.NumberedBooksEngine.Core.NumberFormatter.Format(to, fmt)}" +
                               $"  ({to - from + 1})");

            HasRegistryEntries = IssuedRanges.Count > 0;
            RegistrySummary = HasRegistryEntries
                ? Lf("Num_RegistrySummary", records.Count, NumberGaps.Count)
                : L("Num_RegistryEmpty");

            OnPropertyChanged(nameof(SuggestedNextNumber));
        }

        // ── Number format ────────────────────────────────────────────────────
        // Official books rarely print a bare integer: they carry a series prefix and
        // often a year suffix, e.g. INV-000123/2026.
        [ObservableProperty] private int _numberPadDigits = 6;
        [ObservableProperty] private string _numberPrefix = "";
        [ObservableProperty] private string _numberSuffix = "";

        /// <summary>
        /// The format this job prints with — the single source for every place that
        /// shows a number, so the ribbon example, the number drawn on the design and
        /// the paper cannot disagree.
        /// </summary>
        public Apex.NumberedBooksEngine.Core.NumberFormatOptions CurrentNumberFormat =>
            new(PadDigits: NumberPadDigits,
                Prefix: NumberPrefix?.Trim() ?? "",
                Suffix: NumberSuffix?.Trim() ?? "",
                UseArabicDigits: UseArabicDigits);

        /// <summary>Live example of how a number will actually print.</summary>
        public string NumberFormatPreview =>
            Apex.NumberedBooksEngine.Core.NumberFormatter.Format(
                StartNumber <= 0 ? 1 : StartNumber, CurrentNumberFormat);

        /// <summary>
        /// Anything that changes the printed shape of a number has to redraw the
        /// numbers on the design too. They used to be rendered as a bare four-digit
        /// value, so an operator placing a slot judged its width against "0001" while
        /// the press produced "INV-000123/2026".
        /// </summary>
        private void OnNumberFormatChanged()
        {
            OnPropertyChanged(nameof(CurrentNumberFormat));
            OnPropertyChanged(nameof(NumberFormatPreview));
            RefreshAllPreviewNumbers();
        }

        partial void OnNumberPadDigitsChanged(int value) => OnNumberFormatChanged();
        partial void OnNumberPrefixChanged(string value) => OnNumberFormatChanged();
        partial void OnNumberSuffixChanged(string value) => OnNumberFormatChanged();

        // Digit style: Arabic-Indic (٠١٢...) or Western (012...)
        [ObservableProperty]
        private bool _useArabicDigits = false;   // false = Western (0123), true = Arabic-Indic (٠١٢٣)

        [ObservableProperty]
        private bool _useWesternDigits = true;   // mirror for RadioButton binding

        partial void OnUseArabicDigitsChanged(bool value)
        {
            _useWesternDigits = !value;
            OnPropertyChanged(nameof(UseWesternDigits));
            OnNumberFormatChanged();
        }

        partial void OnUseWesternDigitsChanged(bool value)
        {
            _useArabicDigits = !value;
            OnPropertyChanged(nameof(UseArabicDigits));
            OnNumberFormatChanged();
        }

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
                    PrintStatus = L("Num_DesignSelected");
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
                    MessageBox.Show(Lf("Num_DesignLoadError", ex.Message), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(L("Num_SelectPrinter"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TotalNumbers <= 0)
            {
                MessageBox.Show(L("Num_EnterValidCount"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Compliance gate: never reissue numbers that were already printed.
            // A duplicated invoice/receipt number is a breach for the press and the
            // customer, so this asks for an explicit decision rather than silently
            // proceeding.
            var check = _numberRegistry.Check(NumberPrefix, StartNumber, TotalNumbers);
            if (check.Status == Apex.NumberedBooksEngine.Core.RangeCheckStatus.Invalid)
            {
                MessageBox.Show(check.Message, L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!check.IsAvailable)
            {
                var proceed = MessageBox.Show(
                    Lf("Num_DuplicateWarning", check.Message),
                    L("Num_DuplicateTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);          // default is to STOP
                if (proceed != MessageBoxResult.Yes) return;
            }

            IsPrinting = true;
            PrintStatus = L("Num_Preparing");
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
                PrintStatus = L("Num_PrintCancelled");
                RecordPrintHistory((long)(PrintProgress / 100.0 * TotalPagesComputed), L("Num_Cancelled"));
            }
            catch (Exception ex)
            {
                // Show user-friendly error message
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                    errorMessage += Lf("Num_ErrorDetails", ex.InnerException.Message);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(Lf("Num_PrintErrorDuring", errorMessage), L("Num_PrintErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                });

                PrintStatus = Lf("Num_ErrorColon", ex.Message);
                RecordPrintHistory((long)(PrintProgress / 100.0 * TotalPagesComputed), L("Dlg_Error"));
            }
            finally
            {
                // Record successful completion
                if (PrintProgress >= 99.9)
                {
                    RecordPrintHistory(TotalPagesComputed, L("Num_Complete"));

                    // Burn the range in the register only once the job really printed —
                    // reserving up front would consume numbers on a cancelled job and
                    // create a gap the operator cannot explain.
                    try
                    {
                        _numberRegistry.Record(
                            NumberPrefix, StartNumber, TotalNumbers,
                            SelectedPrinter ?? "",
                            notes: CurrentProjectPath ?? "");
                    }
                    catch (Exception ex)
                    {
                        Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.RecordRange", ex);
                    }
                }

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
                MessageBox.Show(L("Num_SelectPrinter"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(TemplatePath))
            {
                MessageBox.Show(L("Num_SelectTemplatePath"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var slots = Slots.Select(s => s.ToSlotSpec()).ToList();
            if (slots.Count == 0)
            {
                MessageBox.Show(L("Num_AddOneSlot"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PrintStatus = L("Num_Printing");
            PrintProgress = 0;
            CurrentNumberDisplay = Apex.NumberedBooksEngine.Core.NumberFormatter.Format(StartNumber, CurrentNumberFormat);

            try
            {
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                var progress = new Progress<Apex.NumberedBooksEngine.Models.ProgressInfo>(info =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PrintProgress = info.Percent;
                        CurrentNumberDisplay = Apex.NumberedBooksEngine.Core.NumberFormatter.Format(info.LastNumber, CurrentNumberFormat);
                        PrintStatus = Lf("Num_PrintingPct", info.Percent);
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
                    useSmartPrinting: UseSmartTrayPrinting,
                    useArabicDigits:  UseArabicDigits,
                    numberFormat: CurrentNumberFormat);

                if (result.Success)
                {
                    PrintStatus = L("Num_PrintDoneStatus");
                    PrintProgress = 100;
                    MessageBox.Show(L("Num_PrintDoneMsg"), L("Dlg_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var errorText = (result.Errors != null && result.Errors.Count > 0)
                        ? string.Join("; ", result.Errors)
                        : L("Num_UnknownReason");
                    PrintStatus = Lf("Num_PrintErrorText", errorText);
                    MessageBox.Show(Lf("Num_PrintErrorText", errorText), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                PrintStatus = L("Num_PrintCancelled");
            }
            catch (Exception ex)
            {
                PrintStatus = Lf("Num_ErrorColon", ex.Message);
                MessageBox.Show(Lf("Num_PrintErrorDuring", ex.Message), L("Num_PrintErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(L("Num_AddOneSlot"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(TemplatePath))
            {
                MessageBox.Show(L("Num_SelectTemplatePath"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Start cycle printing
            PrintStatus = L("Num_StartingCyclic");
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
                    PrintStatus = Lf("Num_PrintingPct", info.Percent);
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
                    PrintStatus = L("Num_PrintDoneStatus");
                    PrintProgress = 100;
                    MessageBox.Show(Lf("Num_CyclicDoneMsg", result.Completed),
                        L("Dlg_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    PrintStatus = Lf("Num_DoneWithErrorsStatus", result.Failed);
                    MessageBox.Show(Lf("Num_DoneWithErrorsMsg", result.Completed, result.Failed),
                        L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                PrintStatus = L("Num_PrintCancelled");
                MessageBox.Show(L("Num_PrintCancelled"), L("Dlg_Cancel"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                PrintStatus = Lf("Num_ErrorColon", ex.Message);
                MessageBox.Show(Lf("Num_CyclicPrintErrorDuring", ex.Message), L("Num_PrintErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                PrintStatus = L("Num_Resuming");
            }
            else
            {
                _numberingService.PauseCyclePrinting();
                PrintStatus = L("Num_Paused");
            }
        }

        [RelayCommand]
        private async Task RetryCycle()
        {
            if (SelectedFailedCycle == null)
            {
                MessageBox.Show(L("Num_SelectFailedRetry"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var success = _numberingService.RetryCycle(SelectedFailedCycle.JobId);
            if (success)
            {
                PrintStatus = Lf("Num_RetryingCycle", SelectedFailedCycle.CycleNumber);
                MessageBox.Show(Lf("Num_RetryStarted", SelectedFailedCycle.CycleNumber),
                    L("Dlg_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(L("Num_RetryFailed"),
                    L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }

            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task SkipCycle()
        {
            if (SelectedFailedCycle == null)
            {
                MessageBox.Show(L("Num_SelectFailedSkip"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                Lf("Num_SkipConfirm", SelectedFailedCycle.CycleNumber),
                L("Num_SkipConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var success = _numberingService.SkipCycle(SelectedFailedCycle.JobId, L("Num_SkippedByUser"));
                if (success)
                {
                    PrintStatus = Lf("Num_CycleSkipped", SelectedFailedCycle.CycleNumber);
                    MessageBox.Show(Lf("Num_CycleSkipped", SelectedFailedCycle.CycleNumber),
                        L("Dlg_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(L("Num_SkipFailed"),
                        L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            await Task.CompletedTask;
        }

        [RelayCommand]
        private void PauseCyclePrinting()
        {
            _numberingService.PauseCyclePrinting();
            PrintStatus = L("Num_Paused");
        }

        [RelayCommand]
        private void ResumeCyclePrinting()
        {
            _numberingService.ResumeCyclePrinting();
            PrintStatus = L("Num_Resuming");
        }

        [RelayCommand]
        private async Task ResumeFromState()
        {
            if (SelectedPendingState == null)
            {
                MessageBox.Show(L("Num_SelectSavedResume"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                Lf("Num_ResumeConfirm", SelectedPendingState.PrinterName, SelectedPendingState.StartNumber, SelectedPendingState.TotalNumbers),
                L("Num_ResumeConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                IsPrinting = true;
                PrintStatus = L("Num_ResumingFromSaved");
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
                            PrintStatus = Lf("Num_ResumingPct", info.Percent);
                        });
                    }));

                    if (resumeResult.Success)
                    {
                        PrintStatus = L("Num_ResumeDoneStatus");
                        PrintProgress = 100;
                        MessageBox.Show(Lf("Num_ResumeDoneMsg", resumeResult.Completed),
                            L("Dlg_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        PrintStatus = Lf("Num_ResumeErrorsStatus", resumeResult.Failed);
                        MessageBox.Show(Lf("Num_ResumeErrorsMsg", resumeResult.Completed, resumeResult.Failed),
                            L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (OperationCanceledException)
                {
                    PrintStatus = L("Num_ResumeCancelled");
                    MessageBox.Show(L("Num_ResumeCancelled"), L("Dlg_Cancel"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    PrintStatus = Lf("Num_ErrorColon", ex.Message);
                    MessageBox.Show(Lf("Num_ResumeError", ex.Message), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(Lf("Num_LoadSavedError", ex.Message), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(L("Num_InsertDesignFirst"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(L("Num_InsertDesignFirst"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(L("Num_SelectPrinter"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(TemplatePath))
            {
                MessageBox.Show(L("Num_SelectTemplatePathFirst"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Slots == null || Slots.Count == 0)
            {
                MessageBox.Show(L("Num_AddOneSlot"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(L("Num_SwitchDesignMode"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // 0.25 step / 3.0 max to match the Template Designer so the two canvases feel
        // the same. The minimum stays low (0.1, not 0.25) because this is an ABSOLUTE
        // scale on a fixed-size sheet — a large sheet may need to shrink below 0.25 to
        // fit, unlike the Designer whose Viewbox fits first and boosts relative to that.
        public const double ZoomMin = 0.1;
        public const double ZoomMax = 3.0;
        public const double ZoomStep = 0.25;

        internal static double ClampZoom(double z) => Math.Round(Math.Max(ZoomMin, Math.Min(ZoomMax, z)), 2);

        [RelayCommand]
        private void ZoomIn() => ZoomLevel = ClampZoom(ZoomLevel + ZoomStep);

        [RelayCommand]
        private void ZoomOut() => ZoomLevel = ClampZoom(ZoomLevel - ZoomStep);

        // The real canvas viewport, pushed in from the view on every resize. Defaults
        // are a fallback for the first fit before the ScrollViewer has measured.
        public double CanvasViewportWidth { get; set; } = 800;
        public double CanvasViewportHeight { get; set; } = 600;

        [RelayCommand]
        private void FitToScreen()
        {
            if (CanvasWidth <= 0 || CanvasHeight <= 0) return;

            // Fit to the ACTUAL viewport (was hardcoded 800x600, so "fit" was wrong on
            // any other window size). 90% leaves a margin; clamp to the zoom range.
            double scaleX = CanvasViewportWidth / CanvasWidth;
            double scaleY = CanvasViewportHeight / CanvasHeight;
            double fit = Math.Min(scaleX, scaleY) * 0.9;
            ZoomLevel = Math.Round(Math.Max(0.1, Math.Min(3.0, fit)), 2);
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

            // Format exactly as the press will. This used to be ToString("D4") — a
            // bare four digits with no prefix, no suffix and always Western — so the
            // design showed something the printed sheet never carried.
            return Apex.NumberedBooksEngine.Core.NumberFormatter.Format(
                previewNum, CurrentNumberFormat);
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
                ElapsedTimeDisplay = "";
                PrintedPagesDisplay = "";
                PrintSpeedDisplay = "";
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
                PrintedPagesDisplay = Lf("Num_PagesProgress", printed, TotalPagesComputed);
            });
        }

        private void UpdateElapsedAndSpeed()
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var elapsed = DateTime.Now - _printStartTime;

                // Elapsed
                ElapsedTimeDisplay = elapsed.TotalSeconds < 60
                    ? Lf("Num_ElapsedSec", (int)elapsed.TotalSeconds)
                    : Lf("Num_ElapsedMin", (int)elapsed.TotalMinutes, elapsed.Seconds);

                // Speed
                if (elapsed.TotalMinutes > 0 && PrintProgress > 0 && TotalPagesComputed > 0)
                {
                    var printed = PrintProgress / 100.0 * TotalPagesComputed;
                    var pagesPerMin = printed / elapsed.TotalMinutes;
                    var remaining = TotalPagesComputed - printed;
                    var etaMinutes = pagesPerMin > 0 ? remaining / pagesPerMin : 0;
                    PrintSpeedDisplay = etaMinutes > 1
                        ? Lf("Num_EtaMin", (int)etaMinutes)
                        : etaMinutes > 0
                            ? L("Num_EtaSubMin")
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
                PrintStatus = L("Num_Stopping");
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
                Title = L("Num_SaveProject"),
                Filter = "Apex Job|*.apex-job",
                DefaultExt = ".apex-job",
                FileName = "numbering-project"
            };
            if (dlg.ShowDialog() != true) return;

            var project = new NumberingProjectFile
            {
                TemplatePath = TemplatePath ?? "",
                StartNumber = StartNumber,
                TotalNumbers = TotalNumbers,
                NumberOfCopies = NumberOfCopies,
                IsLinearMode = IsLinearMode,
                IsImposedMode = IsImposedMode,
                UseSmartTrayPrinting = UseSmartTrayPrinting,
                SelectedPrinter = SelectedPrinter ?? "",
                Notes = ProjectNotes,
                Slots = Slots.Select(s => new NumberingSlotData
                {
                    Id = s.Id,
                    X = s.X,
                    Y = s.Y,
                    Width = s.Width,
                    Height = s.Height,
                    FontFamily = s.FontFamily,
                    FontSize = s.FontSize,
                    FontColor = s.FontColor,
                    IsBold = s.IsBold,
                    Rotation = s.Rotation,
                    Alignment = s.Alignment,
                    Opacity = s.Opacity
                }).ToList()
            };

            var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            CurrentProjectPath = dlg.FileName;
            AddToRecentProjects(dlg.FileName);
            PrintStatus = Lf("Num_ProjectSaved", Path.GetFileName(dlg.FileName));
        }

        [RelayCommand]
        private void LoadProject()
        {
            var dlg = new OpenFileDialog
            {
                Title = L("Num_LoadProject"),
                Filter = "Apex Job|*.apex-job|All Files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var project = JsonSerializer.Deserialize<NumberingProjectFile>(json);
                if (project == null) throw new Exception(L("Num_ProjectCorrupt"));

                TemplatePath = project.TemplatePath;
                StartNumber = project.StartNumber;
                TotalNumbers = project.TotalNumbers;
                NumberOfCopies = project.NumberOfCopies;
                IsLinearMode = project.IsLinearMode;
                IsImposedMode = project.IsImposedMode;
                UseSmartTrayPrinting = project.UseSmartTrayPrinting;

                if (!string.IsNullOrEmpty(project.SelectedPrinter) &&
                    AvailablePrinters.Contains(project.SelectedPrinter))
                    SelectedPrinter = project.SelectedPrinter;

                Slots.Clear();
                foreach (var sd in project.Slots)
                {
                    Slots.Add(new NumberSlot
                    {
                        Id = sd.Id,
                        X = sd.X,
                        Y = sd.Y,
                        Width = sd.Width,
                        Height = sd.Height,
                        FontFamily = sd.FontFamily,
                        FontSize = sd.FontSize,
                        FontColor = sd.FontColor,
                        IsBold = sd.IsBold,
                        Rotation = sd.Rotation,
                        Alignment = sd.Alignment,
                        Opacity = sd.Opacity
                    });
                }

                ProjectNotes = project.Notes ?? "";
                CurrentProjectPath = dlg.FileName;
                AddToRecentProjects(dlg.FileName);
                RefreshAllPreviewNumbers();
                UpdateComputedValues();
                SchedulePreviewUpdate();
                PrintStatus = Lf("Num_ProjectLoaded", Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lf("Num_ProjectLoadError", ex.Message), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(L("Num_NoItemsExport"),
                    L("Num_ExportSettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = L("Num_ExportDesignSettings"),
                Filter = "Apex Numbering Design|*.apexnr",
                DefaultExt = ".apexnr",
                FileName = "numbering-design"
            };
            if (dlg.ShowDialog() != true) return;

            var design = new NumberingDesignSettings
            {
                ExportedAt = DateTime.Now,
                Version = "1.0",
                SlotCount = Slots.Count,
                Slots = Slots.Select(s => new NumberingSlotData
                {
                    Id = s.Id,
                    X = s.X,
                    Y = s.Y,
                    Width = s.Width,
                    Height = s.Height,
                    FontFamily = s.FontFamily,
                    FontSize = s.FontSize,
                    FontColor = s.FontColor,
                    IsBold = s.IsBold,
                    Rotation = s.Rotation,
                    Alignment = s.Alignment,
                    Opacity = s.Opacity
                }).ToList()
            };

            var json = JsonSerializer.Serialize(design, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            PrintStatus = Lf("Num_DesignExported", Slots.Count, Path.GetFileName(dlg.FileName));
        }

        [RelayCommand]
        private void ImportDesignSettings()
        {
            var dlg = new OpenFileDialog
            {
                Title = L("Num_ImportDesignSettings"),
                Filter = "Apex Numbering Design|*.apexnr|All Files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var design = JsonSerializer.Deserialize<NumberingDesignSettings>(json);
                if (design?.Slots == null || design.Slots.Count == 0)
                    throw new Exception(L("Num_SettingsCorrupt"));

                SaveUndoState(); // Allow undo of the import

                Slots.Clear();
                foreach (var sd in design.Slots)
                {
                    Slots.Add(new NumberSlot
                    {
                        Id = sd.Id,
                        X = sd.X,
                        Y = sd.Y,
                        Width = sd.Width,
                        Height = sd.Height,
                        FontFamily = sd.FontFamily,
                        FontSize = sd.FontSize,
                        FontColor = sd.FontColor,
                        IsBold = sd.IsBold,
                        Rotation = sd.Rotation,
                        Alignment = sd.Alignment,
                        Opacity = sd.Opacity
                    });
                }

                SelectedSlot = Slots.FirstOrDefault();
                RefreshAllPreviewNumbers();
                SchedulePreviewUpdate();
                OnPropertyChanged(nameof(Slots));
                PrintStatus = Lf("Num_DesignImported", design.Slots.Count, Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lf("Num_ImportError", ex.Message),
                    L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                PrintStatus = L("Num_NothingUndo");
                return;
            }

            _redoStack.Push(SnapshotSlots());
            var previous = _undoStack.Pop();
            RestoreSlots(previous);

            CanUndo = _undoStack.Count > 0;
            CanRedo = _redoStack.Count > 0;
            PrintStatus = Lf("Num_Undone", _undoStack.Count);
        }

        [RelayCommand]
        private void Redo()
        {
            if (_redoStack.Count == 0)
            {
                PrintStatus = L("Num_NothingRedo");
                return;
            }

            _undoStack.Push(SnapshotSlots());
            var next = _redoStack.Pop();
            RestoreSlots(next);

            CanUndo = _undoStack.Count > 0;
            CanRedo = _redoStack.Count > 0;
            PrintStatus = Lf("Num_Redone", _redoStack.Count);
        }

        private List<NumberingSlotData> SnapshotSlots()
        {
            return (Slots ?? Enumerable.Empty<NumberSlot>())
                .Select(s => new NumberingSlotData
                {
                    Id = s.Id,
                    X = s.X,
                    Y = s.Y,
                    Width = s.Width,
                    Height = s.Height,
                    FontFamily = s.FontFamily,
                    FontSize = s.FontSize,
                    FontColor = s.FontColor,
                    IsBold = s.IsBold,
                    Rotation = s.Rotation,
                    Alignment = s.Alignment,
                    Opacity = s.Opacity
                }).ToList();
        }

        private void RestoreSlots(List<NumberingSlotData> snapshot)
        {
            Slots.Clear();
            foreach (var sd in snapshot)
            {
                Slots.Add(new NumberSlot
                {
                    Id = sd.Id,
                    X = sd.X,
                    Y = sd.Y,
                    Width = sd.Width,
                    Height = sd.Height,
                    FontFamily = sd.FontFamily,
                    FontSize = sd.FontSize,
                    FontColor = sd.FontColor,
                    IsBold = sd.IsBold,
                    Rotation = sd.Rotation,
                    Alignment = sd.Alignment,
                    Opacity = sd.Opacity
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
                PrintStatus = L("Num_NoCheckpoints");
            else
                PrintStatus = Lf("Num_CheckpointsFound", CheckpointStates.Count);
        }

        [RelayCommand]
        private void ResumeFromCheckpoint()
        {
            if (SelectedCheckpoint == null) return;
            // Resume = set StartNumber to the number AFTER the last printed
            StartNumber = SelectedCheckpoint.LastPrintedNumber + 1;
            PrintStatus = Lf("Num_ContinueFrom", StartNumber);
        }

        [RelayCommand]
        private void DeleteCheckpoint()
        {
            if (SelectedCheckpoint == null) return;
            _checkpointManager.DeleteCheckpoint(SelectedCheckpoint.JobId);
            CheckpointStates.Remove(SelectedCheckpoint);
            SelectedCheckpoint = null;
            PrintStatus = L("Num_CheckpointDeleted");
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
                MessageBox.Show(L("Num_LoadDesignFirst"), L("Dlg_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (Slots.Count == 0)
            {
                MessageBox.Show(L("Num_AddNumberingField"), L("Dlg_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsGeneratingMultiPreview = true;
            IsMultiPreviewOpen = true;
            MultiPreviewPages.Clear();

            try
            {
                var slots = Slots.Select(s => s.ToSlotSpec()).ToList();
                var isPdf = TemplatePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                var format = isPdf ? TemplateFormat.Pdf : TemplateFormat.Image;
                var count = (int)Math.Min(TotalNumbers, 4);

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
                MessageBox.Show(Lf("Num_PreviewError", ex.Message), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (minutes < 1) return L("Num_LessThanMinute");
                if (minutes < 60) return Lf("Num_AboutMinutes", (int)Math.Ceiling(minutes));
                return Lf("Num_AboutHoursMinutes", (int)(minutes / 60), (int)(minutes % 60));
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
            catch (System.Exception ex) { Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.LoadRecentProjects", ex); }
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
            catch (System.Exception ex) { Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.AddRecentProject", ex); }
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
                var json = File.ReadAllText(path);
                var project = JsonSerializer.Deserialize<NumberingProjectFile>(json);
                if (project == null) return;

                TemplatePath = project.TemplatePath;
                StartNumber = project.StartNumber;
                TotalNumbers = project.TotalNumbers;
                NumberOfCopies = project.NumberOfCopies;
                IsLinearMode = project.IsLinearMode;
                IsImposedMode = project.IsImposedMode;
                UseSmartTrayPrinting = project.UseSmartTrayPrinting;
                ProjectNotes = project.Notes ?? "";

                if (!string.IsNullOrEmpty(project.SelectedPrinter) &&
                    AvailablePrinters.Contains(project.SelectedPrinter))
                    SelectedPrinter = project.SelectedPrinter;

                Slots.Clear();
                foreach (var sd in project.Slots)
                    Slots.Add(new NumberSlot
                    {
                        Id = sd.Id,
                        X = sd.X,
                        Y = sd.Y,
                        Width = sd.Width,
                        Height = sd.Height,
                        FontFamily = sd.FontFamily,
                        FontSize = sd.FontSize,
                        FontColor = sd.FontColor,
                        IsBold = sd.IsBold,
                        Rotation = sd.Rotation,
                        Alignment = sd.Alignment,
                        Opacity = sd.Opacity
                    });

                CurrentProjectPath = path;
                AddToRecentProjects(path);
                RefreshAllPreviewNumbers();
                UpdateComputedValues();
                SchedulePreviewUpdate();
                PrintStatus = Lf("Num_LoadedFile", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lf("Num_ErrorNewline", ex.Message), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                Id = $"Slot {_slotCounter}",
                X = Math.Min(src.X + 0.03f, 0.85f),
                Y = Math.Min(src.Y + 0.03f, 0.85f),
                Width = src.Width,
                Height = src.Height,
                FontFamily = src.FontFamily,
                FontSize = src.FontSize,
                FontColor = src.FontColor,
                IsBold = src.IsBold,
                Rotation = src.Rotation,
                Alignment = src.Alignment,
                Opacity = src.Opacity
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
            catch (System.Exception ex) { Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.LoadPrintHistory", ex); }
        }

        internal void RecordPrintHistory(long pagesCount, string status)
        {
            try
            {
                var record = new PrintHistoryRecord
                {
                    Timestamp = DateTime.Now,
                    StartNumber = StartNumber,
                    TotalNumbers = TotalNumbers,
                    PagesCount = pagesCount,
                    Printer = SelectedPrinter ?? "",
                    Status = status,
                    ProjectName = string.IsNullOrEmpty(CurrentProjectPath)
                                    ? L("Num_Untitled")
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
            catch (System.Exception ex) { Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.RecordPrintHistory", ex); }
        }

        [RelayCommand]
        private void ClearPrintHistory()
        {
            try
            {
                if (File.Exists(PrintHistoryPath)) File.Delete(PrintHistoryPath);
                PrintHistory.Clear();
                PrintStatus = L("Num_LogCleared");
            }
            catch (System.Exception ex) { Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.ClearPrintHistory", ex); }
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
                MessageBox.Show(L("Num_NoPreviewExport"),
                    L("Dlg_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = L("Num_ExportPreview"),
                Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                DefaultExt = ".png",
                FileName = $"preview-{StartNumber:D6}"
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
                PrintStatus = Lf("Num_PreviewExported", Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lf("Num_ExportError", ex.Message), L("Dlg_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  LOW PRIORITY ④  —  زوم عجلة الماوس (handled in code-behind)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Fallback wheel zoom (about the canvas centre). The code-behind normally
        /// zooms toward the mouse pointer instead; this keeps a sane result if it
        /// ever calls in without anchoring.
        /// </summary>
        public void ApplyWheelZoom(int delta)
            => ZoomLevel = ClampZoom(ZoomLevel + (delta > 0 ? ZoomStep : -ZoomStep));

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
        public string TemplatePath { get; set; } = "";
        public long StartNumber { get; set; } = 1;
        public long TotalNumbers { get; set; } = 100;
        public int NumberOfCopies { get; set; } = 1;
        public bool IsLinearMode { get; set; } = true;
        public bool IsImposedMode { get; set; } = false;
        public bool UseSmartTrayPrinting { get; set; } = false;
        public string SelectedPrinter { get; set; } = "";
        public string? Notes { get; set; }
        public List<NumberingSlotData> Slots { get; set; } = new();
    }

    public class NumberingSlotData
    {
        public string Id { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; } = 0.1f;
        public float Height { get; set; } = 0.05f;
        public string FontFamily { get; set; } = "Arial";
        public float FontSize { get; set; } = 24;
        public string FontColor { get; set; } = "#000000";
        public bool IsBold { get; set; }
        public double Rotation { get; set; }
        public string Alignment { get; set; } = "Left";
        public double Opacity { get; set; } = 1.0;
    }

    /// <summary>
    /// Lightweight export format for slot design only (.apexnr).
    /// Does NOT include template path or print settings — just the visual layout.
    /// </summary>
    public class NumberingDesignSettings
    {
        public string Version { get; set; } = "1.0";
        public DateTime ExportedAt { get; set; } = DateTime.Now;
        public int SlotCount { get; set; }
        public List<NumberingSlotData> Slots { get; set; } = new();
    }

    public class PrintHistoryRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public long StartNumber { get; set; }
        public long TotalNumbers { get; set; }
        public long PagesCount { get; set; }
        public string Printer { get; set; } = "";
        public string Status { get; set; } = "";
        public string ProjectName { get; set; } = "";
    }
}

