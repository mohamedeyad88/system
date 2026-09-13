using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Services.Numbering;
using Apex.Services.Printing;
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
        private readonly PrinterTrayDetectionService _trayDetector = new();
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
            // Refresh the tray choices to the newly selected printer's real trays.
            InitializeTrayOptions();
        }

        [ObservableProperty]
        private long _startNumber = 1;

        partial void OnStartNumberChanged(long value)
        {
            // Moving the start shifts the range, so the derived count changes too.
            SyncTotalFromRange();

            // Update PreviewNumber for all slots when StartNumber changes
            // For preview, show different numbers for each slot based on numbering mode
            for (int i = 0; i < Slots.Count; i++)
            {
                Slots[i].PreviewNumber = CalculatePreviewNumber(value, i);
            }
            SchedulePreviewUpdate();
            RefreshImposedRanges();
        }

        // The operator enters a RANGE — "from StartNumber to EndNumber" — the way a
        // press quotes a job ("من ٥٠٠ إلى ٦٠٠"). TotalNumbers (the count the engine
        // actually consumes) is DERIVED, never typed. This box used to be bound to
        // TotalNumbers while labelled "إلى", so "من ٥٠٠ إلى ٦٠٠" silently produced 600
        // numbers (500..1099) instead of 101 — the reported source of user errors.
        [ObservableProperty]
        private long _endNumber = 100;

        partial void OnEndNumberChanged(long value)
        {
            SyncTotalFromRange();
            SchedulePreviewUpdate();
        }

        /// <summary>True when EndNumber &lt; StartNumber — an impossible range; blocks printing.</summary>
        [ObservableProperty]
        private bool _rangeInvalid;

        private bool _syncingRange;

        /// <summary>Derives TotalNumbers (the count) from the [Start, End] range.</summary>
        private void SyncTotalFromRange()
        {
            if (_syncingRange) return;
            _syncingRange = true;
            long count = EndNumber - StartNumber + 1;
            RangeInvalid = count < 1;
            TotalNumbers = count < 1 ? 1 : count; // keep the engine safe; UI blocks an invalid range
            _syncingRange = false;
        }

        [ObservableProperty]
        private long _totalNumbers = 100;

        partial void OnTotalNumbersChanged(long value)
        {
            // TotalNumbers set programmatically (loading a saved project/preset) → keep
            // the End box in step so the range shown matches the loaded count.
            if (!_syncingRange)
            {
                _syncingRange = true;
                EndNumber = StartNumber + (value < 1 ? 1 : value) - 1;
                RangeInvalid = false;
                _syncingRange = false;
            }

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

            // Redraw the rendered overlay too, not just the field boxes.
            //
            // This is called when a field is added, removed or renumbered, and it used to
            // update only the boxes on the canvas. The numbers the operator actually sees
            // come from LivePreviewImage — a picture of the sheet rendered underneath them —
            // so deleting a field left its number sitting there in that picture. Reported as
            // a numbering field that "got stuck and cannot be deleted": the field was long
            // gone, its portrait was not.
            SchedulePreviewUpdate();
            RefreshImposedRanges();
        }

        /// <summary>
        /// In cutting mode, what each cut piece will actually carry — one line per field,
        /// e.g. "القطعة ١: ٠٠٠٠٠١ → ٠٠٠٠٣٤ (٣٤ رقم)".
        ///
        /// <para>A press puts four A5 books on one A3, prints the sheet, then cuts it into
        /// four piles — and each pile is a separate book. The operator could see the four
        /// numbers on the sheet but nothing about what each PILE would end up holding, and
        /// that is the only thing that matters: if the ranges are wrong you find out after
        /// the guillotine, with twenty thousand sheets already cut and nothing to salvage.
        /// Reported as "the sheet is an A3 split into four A5 books and the numbering lands
        /// on them — that is not visible in the preview".</para>
        ///
        /// <para>It also exposes the uneven tail. Splitting 100 numbers across 3 fields
        /// gives 34 sheets, so the first two piles hold 34 numbers and the last holds 32 —
        /// worth knowing before the run, not after.</para>
        /// </summary>
        /// <param name="Piece">1-based position of the cut piece on the sheet.</param>
        /// <param name="From">First number in this pile, 0 when the pile gets nothing.</param>
        /// <param name="To">Last number in this pile, 0 when the pile gets nothing.</param>
        /// <param name="Count">How many numbers this pile holds.</param>
        /// <param name="Text">The same thing said in the operator's language, for the UI.</param>
        public record CutPiece(int Piece, long From, long To, long Count, string Text);

        [ObservableProperty]
        private ObservableCollection<CutPiece> _imposedRanges = new();

        /// <summary>True when there is a cut-piece breakdown worth showing.</summary>
        public bool HasImposedRanges => ImposedRanges.Count > 0;

        private void RefreshImposedRanges()
        {
            ImposedRanges.Clear();

            if (!IsImposedMode || Slots == null || Slots.Count == 0 || TotalNumbers < 1)
            {
                OnPropertyChanged(nameof(HasImposedRanges));
                return;
            }

            int slotCount = Slots.Count;
            long sheets = (long)Math.Ceiling((double)TotalNumbers / slotCount);
            long lastNumber = StartNumber + TotalNumbers - 1;
            var format = CurrentNumberFormat;

            for (int i = 0; i < slotCount; i++)
            {
                long from = StartNumber + (i * sheets);

                if (from > lastNumber)
                {
                    // More fields than the range can fill: this piece prints nothing.
                    ImposedRanges.Add(new CutPiece(i + 1, 0, 0, 0, Lf("Num_CutPieceEmpty", i + 1)));
                    continue;
                }

                long to = Math.Min(from + sheets - 1, lastNumber);
                var text = Lf("Num_CutPieceRange",
                    i + 1,
                    Apex.NumberedBooksEngine.Core.NumberFormatter.Format(from, format),
                    Apex.NumberedBooksEngine.Core.NumberFormatter.Format(to, format),
                    to - from + 1);

                ImposedRanges.Add(new CutPiece(i + 1, from, to, to - from + 1, text));
            }

            OnPropertyChanged(nameof(HasImposedRanges));
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

        /// <summary>
        /// One drawer the operator can choose. <c>Value</c> is the printer's own source id
        /// (<c>PaperSource.RawKind</c>): Windows reports most vendor drawers as
        /// <c>PaperSourceKind.Custom</c>, so keying the picker by kind gave a two-drawer
        /// machine two entries with the same name and the same value — nothing to choose
        /// between, and every copy ended up on the first drawer.
        /// </summary>
        public record TrayOption(string DisplayName, int Value);

        [ObservableProperty]
        private ObservableCollection<TrayOption> _availableTrayOptions = new();

        [ObservableProperty]
        private int? _copy1Tray;

        [ObservableProperty]
        private int? _copy2Tray;

        [ObservableProperty]
        private int? _copy3Tray;

        [ObservableProperty]
        private int _originalTray = (int)PaperSourceKind.Upper;  // Tray for Original (Copy 0)

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

        /// <summary>
        /// True when a stopped run can actually be resumed. The "استمرار من إيقاف" group
        /// is hidden otherwise: it used to occupy a full slot on the toolbar permanently
        /// just to say "no checkpoints", which is the dead space operators read as clutter.
        /// </summary>
        public bool HasCheckpoints => PendingStates.Count > 0;

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
            Slots.CollectionChanged += OnSlotsCollectionChanged;

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
            OriginalTray = (int)PaperSourceKind.Upper;

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

                    // Fit the sheet to the window the moment it loads.
                    //
                    // The canvas is laid out at the design's OWN pixel size, and zoom was
                    // left at whatever it happened to be — 1.0 on a fresh start. A 300 dpi
                    // A4 scan is 2480x3508, so it opened at 2480 screen pixels inside a
                    // viewport a few hundred wide: the operator saw a corner of the sheet
                    // blown up and reported "the design opens far too big". Nothing was
                    // wrong with the design; it simply was never fitted to the window.
                    FitToScreen();

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

        /// <summary>
        /// Takes the loaded design off the canvas and clears the fields placed on it.
        ///
        /// Until now the only way to get a design out of the numbering screen was to load
        /// a different one over it — so whatever was last opened stayed on screen
        /// indefinitely. That matters beyond tidiness: shops scan customer paperwork, and
        /// a document nobody meant to leave up sat there for anyone at the machine to see.
        /// The numbering fields go with it because their positions are relative to the
        /// sheet they were placed on, and the confirmation says so before anything is lost.
        /// </summary>
        [RelayCommand]
        private void ClearTemplate()
        {
            if (TemplateImage == null && string.IsNullOrWhiteSpace(TemplatePath) && Slots.Count == 0)
                return;

            var answer = MessageBox.Show(
                L("Num_ConfirmClearDesign"), L("Dlg_Confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            TemplatePath = null;   // clears TemplateImage and the live preview
            CanvasWidth = 0;
            CanvasHeight = 0;

            Slots.Clear();
            SelectedSlot = null;
            _slotCounter = 0;
            _undoStack.Clear();
            _redoStack.Clear();

            WorkflowMode = WorkflowMode.Prepare;
            PrintStatus = L("Num_DesignCleared");
        }

        /// <summary>True when there is a design on the canvas to remove.</summary>
        public bool HasTemplate => TemplateImage != null || !string.IsNullOrWhiteSpace(TemplatePath);

        partial void OnTemplateImageChanged(BitmapSource? value) => OnPropertyChanged(nameof(HasTemplate));

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

            // Asking for a second copy is the moment it needs a drawer of its own.
            AssignDefaultCopyTrays();
            OnPropertyChanged(nameof(TraySeparationUnavailable));
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

        /// <summary>
        /// The furthest number the engine reported reaching on the current job. Reset when
        /// a job starts, raised by every progress report.
        /// </summary>
        private long _highestNumberReached;

        /// <summary>
        /// Tells the operator when this exact job was already started and stopped part-way,
        /// and lets them call it off.
        ///
        /// <para>The engine never acts on a checkpoint by itself. Only the operator knows
        /// whether the half-printed stack is still on the table — in which case reprinting
        /// the whole range wastes the paper already run — or went in the bin, in which case
        /// starting over is right. Returns false if they choose to stop.</para>
        /// </summary>
        private async Task<bool> ConfirmUnfinishedJobAsync()
        {
            try
            {
                var slots = Slots.Select(s => s.ToSlotSpec()).ToList();
                var unfinished = await _numberingService.FindUnfinishedJobAsync(
                    TemplatePath ?? "", slots, StartNumber, TotalNumbers, CopiesCount, CurrentNumberFormat);

                if (unfinished == null) return true;

                var reached = Apex.NumberedBooksEngine.Core.NumberFormatter.Format(
                    unfinished.LastPrintedNumber, CurrentNumberFormat);

                var answer = MessageBox.Show(
                    Lf("Num_UnfinishedJob", reached, unfinished.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm")),
                    L("Dlg_Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes) return false;

                // Starting over is a decision; the old attempt stops being pending.
                _numberingService.ForgetUnfinishedJob(
                    TemplatePath ?? "", slots, StartNumber, TotalNumbers, CopiesCount, CurrentNumberFormat);
                return true;
            }
            catch (Exception ex)
            {
                // Never block a job over the recovery check itself.
                Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.CheckUnfinished", ex);
                return true;
            }
        }

        /// <summary>
        /// Writes what actually reached paper into the register — whether the job finished,
        /// was cancelled, or died.
        ///
        /// <para>This only ran at 99.9% before, so an interrupted job recorded NOTHING. Ten
        /// thousand invoice numbers could be lying in the output tray with the register
        /// insisting they had never been issued, and the duplicate check — which reads that
        /// register — would then wave the operator straight through a reprint of the same
        /// range. A duplicated invoice number is a breach for the press and for its
        /// customer; that was the one failure this register exists to prevent.</para>
        ///
        /// <para>When the count is uncertain the range is rounded UP to the last reporting
        /// interval, deliberately. An over-recorded range leaves a gap, and a gap can be
        /// explained to an auditor; an under-recorded one hands the same number out twice,
        /// and that cannot.</para>
        /// </summary>
        private void RecordIssuedNumbers(bool completed)
        {
            try
            {
                long issued = completed
                    ? TotalNumbers
                    : (_highestNumberReached >= StartNumber ? _highestNumberReached - StartNumber + 1 : 0);

                if (issued <= 0) return;
                if (issued > TotalNumbers) issued = TotalNumbers;

                _numberRegistry.Record(
                    NumberPrefix, StartNumber, issued,
                    SelectedPrinter ?? "",
                    notes: completed
                        ? (CurrentProjectPath ?? "")
                        : Lf("Num_RegisterInterrupted", CurrentProjectPath ?? ""));
            }
            catch (Exception ex)
            {
                Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.RecordRange", ex);
            }
        }

        [RelayCommand]
        private async Task StartPrint()
        {

            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show(L("Num_SelectPrinter"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Checked at the moment the job is sent, not on the way to this screen. Without
            // these the press would have run the whole job on blank sheets.
            if (string.IsNullOrWhiteSpace(TemplatePath))
            {
                MessageBox.Show(L("Num_SelectTemplatePathFirst"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Slots == null || Slots.Count == 0)
            {
                MessageBox.Show(L("Num_AddOneSlot"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (RangeInvalid)
            {
                MessageBox.Show(L("Num_RangeInvalid"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TotalNumbers <= 0)
            {
                MessageBox.Show(L("Num_EnterValidCount"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Pre-flight: every chosen tray must exist on THIS printer. With the tray
            // options now read from the printer this is belt-and-braces (a loaded project
            // could still carry a tray the current printer lacks), and it fails with a
            // clear Arabic message instead of the engine's technical English one.
            if (!string.IsNullOrWhiteSpace(SelectedPrinter))
            {
                var availableTrays = _trayDetector.GetAvailableTrays(SelectedPrinter)
                    .Select(t => t.RawKind).ToHashSet();
                if (availableTrays.Count > 0)
                {
                    var chosen = new List<int> { OriginalTray };
                    if (UseCopy1 && Copy1Tray.HasValue) chosen.Add(Copy1Tray.Value);
                    if (UseCopy2 && Copy2Tray.HasValue) chosen.Add(Copy2Tray.Value);
                    if (UseCopy3 && Copy3Tray.HasValue) chosen.Add(Copy3Tray.Value);
                    if (chosen.Any(k => !availableTrays.Contains(k)))
                    {
                        MessageBox.Show(L("Num_TrayMissing"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
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

            // An earlier attempt at this exact job may have stopped part-way. Say so before
            // the press starts, because the operator is the only one who knows whether the
            // half-printed stack is still on the table or already in the bin.
            if (!await ConfirmUnfinishedJobAsync()) return;

            IsPrinting = true;
            PrintStatus = L("Num_Preparing");
            PrintProgress = 0;
            _highestNumberReached = 0;

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
                bool completed = PrintProgress >= 99.9;
                if (completed) RecordPrintHistory(TotalPagesComputed, L("Num_Complete"));

                RecordIssuedNumbers(completed);

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
                    // Remember how far the press actually got. If the job is cancelled or
                    // dies, this is what tells the register which numbers really reached
                    // paper — see the note on _highestNumberReached.
                    if (info.LastNumber > _highestNumberReached) _highestNumberReached = info.LastNumber;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PrintProgress = info.Percent;
                        CurrentNumberDisplay = Apex.NumberedBooksEngine.Core.NumberFormatter.Format(info.LastNumber, CurrentNumberFormat);
                        PrintStatus = Lf("Num_PrintingPct", info.Percent);
                    });
                });

                var trayMapping = BuildTrayMapping();

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

            var trayMapping = BuildTrayMapping();

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
                OnPropertyChanged(nameof(HasCheckpoints));
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

        /// <summary>
        /// Opens the print stage. Looking at the print settings is not the same as printing,
        /// so nothing is demanded here.
        ///
        /// <para>This used to refuse to open at all without a printer, a design AND at least
        /// one numbering field, which is how an operator got told to "add at least one field"
        /// while still on the way to the stage where fields are placed. Those conditions
        /// matter when the job is actually sent, and that is where they are checked now —
        /// see <see cref="StartPrint"/>.</para>
        /// </summary>
        [RelayCommand]
        private void NavigateToExecute()
        {
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

            // The canvas is sized by the design image, so with no design loaded a new
            // field is added to a zero-sized surface and simply never appears — the
            // operator clicks "+" and nothing happens. Say why instead of failing mute.
            if (TemplateImage == null && string.IsNullOrWhiteSpace(TemplatePath))
            {
                MessageBox.Show(L("Num_InsertDesignFirst"), L("Dlg_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveUndoState(); // snapshot before adding
            _slotCounter++;

            // Position each new slot slightly offset from the previous
            float yOffset = 0.1f + ((_slotCounter - 1) * 0.08f) % 0.6f;

            const float defaultHeight = 0.08f;

            // A new field copies the look of the one before it.
            //
            // Every field on a sheet is the same number in the same type — that is what a
            // numbered book IS. Starting each one at Arial 24 black meant the operator set
            // the colour and size, added the second field, and had to set them all over
            // again, for every field on the sheet. Only the position differs, so only the
            // position is new here. With nothing to copy from, the old defaults still apply.
            var style = SelectedSlot ?? Slots?.LastOrDefault();

            var newSlot = new NumberSlot
            {
                Id = $"Slot {_slotCounter}",
                X = style?.X ?? 0.1f,
                Y = yOffset,
                Width = style?.Width ?? 0.3f,
                Height = style?.Height ?? defaultHeight,
                FontFamily = style?.FontFamily ?? "Arial",
                FontSize = style?.FontSize ?? DefaultSlotFontSize(defaultHeight),
                FontColor = style?.FontColor ?? "#000000",
                PreviewNumber = CalculatePreviewNumber(StartNumber, Slots?.Count ?? 0),  // Use actual slot index (after adding this slot)
                IsSelected = true,  // Select new slot by default so it's visible
                IsBold = style?.IsBold ?? false,
                Rotation = style?.Rotation ?? 0,
                Opacity = style?.Opacity ?? 1.0,
                Alignment = style?.Alignment ?? "Center",
                SlotKind = style?.SlotKind ?? "Text",
                BarcodeType = style?.BarcodeType ?? "CODE128"
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

        /// <summary>
        /// Default type size for a freshly added field.
        ///
        /// This is NOT canvas pixels. FontSize is stored in 96-dpi design units — the print
        /// engine multiplies it by the template's own resolution — and every saved project
        /// and .apext file already on a customer's machine was written against that. So the
        /// default stays 24, which lands at roughly 6&#160;mm of printed type on a 300&#160;dpi
        /// A4: a normal numbering stamp. Sizing it off the canvas instead (an earlier attempt
        /// at the "my field does not show up" report) would have been scaled a second time by
        /// the engine and printed the number three times too large. The real cause of that
        /// report was the DESIGNER under-drawing the number — see SlotFontScaleConverter.
        /// </summary>
        private float DefaultSlotFontSize(float slotHeight) => 24f;

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

            // Prefer the SELECTED printer's REAL trays, with the printer's own names
            // ("Cassette 1", "Bypass Tray", "Tray 2", …). Generic "Tray 1/2/3" labels
            // that don't match the hardware are the reported source of tray mix-ups:
            // the operator couldn't tell which drawer held which paper, and could pick a
            // tray the printer doesn't even have. Fall back to generic labels only when
            // no printer is chosen yet or its trays can't be read.
            List<PrinterTray> trays = new();
            if (!string.IsNullOrWhiteSpace(SelectedPrinter))
            {
                try { trays = _trayDetector.GetAvailableTrays(SelectedPrinter!); }
                catch { /* fall back to generic below */ }
            }

            if (trays.Count > 0)
            {
                foreach (var t in trays)
                    AvailableTrayOptions.Add(new TrayOption(t.Name, t.RawKind));
            }
            else
            {
                AvailableTrayOptions.Add(new TrayOption("Tray 1", (int)PaperSourceKind.Upper));
                AvailableTrayOptions.Add(new TrayOption("Tray 2", (int)PaperSourceKind.Lower));
                AvailableTrayOptions.Add(new TrayOption("Tray 3", (int)PaperSourceKind.Middle));
                AvailableTrayOptions.Add(new TrayOption("Manual Feed", (int)PaperSourceKind.Manual));
            }

            ReconcileTraySelections();
        }

        /// <summary>
        /// Keeps the per-copy tray selections valid after the option set changes (e.g.
        /// the operator switched to a printer that lacks a previously-chosen tray): the
        /// Original falls back to the first real tray, and any copy pointing at a tray
        /// that no longer exists is cleared rather than silently printing on the default.
        /// </summary>
        private void ReconcileTraySelections()
        {
            var kinds = AvailableTrayOptions.Select(o => o.Value).ToHashSet();
            if (kinds.Count == 0) return;

            if (!kinds.Contains(OriginalTray))
                OriginalTray = AvailableTrayOptions[0].Value;
            if (Copy1Tray.HasValue && !kinds.Contains(Copy1Tray.Value)) Copy1Tray = null;
            if (Copy2Tray.HasValue && !kinds.Contains(Copy2Tray.Value)) Copy2Tray = null;
            if (Copy3Tray.HasValue && !kinds.Contains(Copy3Tray.Value)) Copy3Tray = null;

            AssignDefaultCopyTrays();

            // Re-announce the selections after the option list has been rebuilt. Clearing
            // an ObservableCollection under a bound ComboBox drops its SelectedItem, and the
            // control does not pick the value up again on its own once the items return —
            // the tray box sat empty even though a tray was selected.
            OnPropertyChanged(nameof(OriginalTray));
            OnPropertyChanged(nameof(Copy1Tray));
            OnPropertyChanged(nameof(Copy2Tray));
            OnPropertyChanged(nameof(Copy3Tray));
            OnPropertyChanged(nameof(TraySeparationUnavailable));
        }

        /// <summary>
        /// Gives each copy its own drawer when the operator has asked for more than one.
        ///
        /// <para>Reported from the floor as "multi-tray printing does not work". It never
        /// could: the per-copy tray started as null and only the Original was ever put in
        /// the mapping, so unless the operator found and set every copy dropdown by hand,
        /// the copies went out with no paper source at all and the printer pulled them from
        /// its default drawer — the same one as the original. Asking for two copies and
        /// getting two identical sheets from one tray looks exactly like a broken feature.</para>
        ///
        /// <para>The Original is also pulled off "Auto Select" here. Auto lets the printer
        /// choose the drawer, which defeats the entire point of separating copies — and it
        /// is the first source most printers report, so it is what the Original was being
        /// reconciled onto.</para>
        ///
        /// <para>Only ever fills in blanks: a tray the operator picked is never overwritten.</para>
        /// </summary>
        private void AssignDefaultCopyTrays()
        {
            if (NumberOfCopies < 2) return;

            // "Auto" is not a drawer, so it cannot take part in separating copies.
            var drawers = AvailableTrayOptions
                .Where(o => o.Value != (int)PaperSourceKind.AutomaticFeed)
                .Select(o => o.Value)
                .Distinct()
                .ToList();

            if (drawers.Count == 0) return;

            // Copies the operator has already assigned are claimed first, so filling in the
            // blanks never lands the Original on a drawer a copy is using.
            var taken = new HashSet<int>();
            if (Copy1Tray.HasValue) taken.Add(Copy1Tray.Value);
            if (Copy2Tray.HasValue) taken.Add(Copy2Tray.Value);

            if (OriginalTray == (int)PaperSourceKind.AutomaticFeed)
            {
                var freeForOriginal = drawers.Where(d => !taken.Contains(d)).ToList();
                if (freeForOriginal.Count > 0) OriginalTray = freeForOriginal[0];
            }

            taken.Add(OriginalTray);

            if (NumberOfCopies >= 2 && !Copy1Tray.HasValue)
            {
                var free = drawers.Where(d => !taken.Contains(d)).ToList();
                if (free.Count > 0)
                {
                    Copy1Tray = free[0];
                    taken.Add(free[0]);
                }
            }

            if (NumberOfCopies >= 3 && !Copy2Tray.HasValue)
            {
                var free = drawers.Where(d => !taken.Contains(d)).ToList();
                if (free.Count > 0)
                {
                    Copy2Tray = free[0];
                    taken.Add(free[0]);
                }
            }
        }

        /// <summary>
        /// True when copies were requested but the printer does not report enough separate
        /// drawers to give each one its own — the copies will come off the same paper. Said
        /// out loud in the UI, because silently printing them all from one tray is what the
        /// "multi-tray does not work" report was about.
        /// </summary>
        public bool TraySeparationUnavailable
        {
            get
            {
                if (NumberOfCopies < 2) return false;
                int drawers = AvailableTrayOptions
                    .Where(o => o.Value != (int)PaperSourceKind.AutomaticFeed)
                    .Select(o => o.Value)
                    .Distinct()
                    .Count();
                return drawers < NumberOfCopies;
            }
        }

        /// <summary>
        /// The copy-to-tray map handed to the print engine.
        ///
        /// This used to be written out inline in both print paths — two copies of the same
        /// logic to keep in step, which is how one of them ends up wrong.
        /// </summary>
        private Dictionary<int, int> BuildTrayMapping()
        {
            var mapping = new Dictionary<int, int> { [0] = OriginalTray };

            if (UseCopy1 && Copy1Tray.HasValue) mapping[1] = Copy1Tray.Value;
            if (UseCopy2 && Copy2Tray.HasValue) mapping[2] = Copy2Tray.Value;
            if (UseCopy3 && Copy3Tray.HasValue) mapping[3] = Copy3Tray.Value;

            return mapping;
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
            if (Slots.Count == 0)
            {
                // Deleting the last field used to return here with the old picture still up,
                // so its number stayed on the sheet with nothing left to delete.
                LivePreviewImage = null;
                return;
            }

            _previewDebounceTimer?.Dispose();
            _previewDebounceTimer = new System.Threading.Timer(
                state => { var t = GenerateLivePreviewAsync(); },
                null,
                PreviewDebounceMs,
                Timeout.Infinite);
        }

        /// <summary>Set when a change arrives while a preview is already rendering.</summary>
        private volatile bool _previewPending;

        private readonly HashSet<NumberSlot> _watchedSlots = new();

        /// <summary>
        /// Slot properties that change what the rendered sheet looks like. PreviewNumber and
        /// IsSelected are left out on purpose: the renderer sets the first and selection
        /// does not change the print, and listening to them would redraw in a loop.
        /// </summary>
        private static readonly HashSet<string> SlotLookProperties = new()
        {
            nameof(NumberSlot.X), nameof(NumberSlot.Y),
            nameof(NumberSlot.Width), nameof(NumberSlot.Height),
            nameof(NumberSlot.FontFamily), nameof(NumberSlot.FontSize),
            nameof(NumberSlot.FontColor), nameof(NumberSlot.IsBold),
            nameof(NumberSlot.Rotation), nameof(NumberSlot.Opacity),
            nameof(NumberSlot.Alignment), nameof(NumberSlot.SlotKind),
            nameof(NumberSlot.BarcodeType),
        };

        partial void OnSlotsChanged(ObservableCollection<NumberSlot>? oldValue, ObservableCollection<NumberSlot> newValue)
        {
            if (oldValue != null) oldValue.CollectionChanged -= OnSlotsCollectionChanged;
            foreach (var s in _watchedSlots) s.PropertyChanged -= OnSlotPropertyChanged;
            _watchedSlots.Clear();
            if (newValue != null)
            {
                newValue.CollectionChanged += OnSlotsCollectionChanged;
                OnSlotsCollectionChanged(newValue, null!);
            }
        }

        private void OnSlotsCollectionChanged(object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Clear() raises Reset without the old items, so re-sync against the list.
            foreach (var old in _watchedSlots.Where(s => !Slots.Contains(s)).ToList())
            {
                old.PropertyChanged -= OnSlotPropertyChanged;
                _watchedSlots.Remove(old);
            }
            foreach (var slot in Slots)
            {
                if (_watchedSlots.Add(slot)) slot.PropertyChanged += OnSlotPropertyChanged;
            }
        }

        /// <summary>
        /// Keeps the rendered sheet in step with the fields on it.
        ///
        /// <para>Nothing listened to a field's own properties, so dragging a field or
        /// changing its size or colour left the rendered picture underneath showing the
        /// number where the field USED to be. With the field's live box now elsewhere, the
        /// sheet showed that number twice — reported from the floor as "the first number
        /// repeats". The stale picture is dropped at once so no ghost survives a drag, and
        /// redrawn once the edit settles.</para>
        /// </summary>
        private void OnSlotPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null || !SlotLookProperties.Contains(e.PropertyName)) return;

            LivePreviewImage = null;
            SchedulePreviewUpdate();
        }

        private async Task GenerateLivePreviewAsync()
        {
            if (string.IsNullOrEmpty(TemplatePath) || !File.Exists(TemplatePath)) return;

            // A change that lands mid-render used to be dropped on the floor, leaving the
            // picture one edit behind. Remember it and render again when this one finishes.
            if (IsGeneratingPreview) { _previewPending = true; return; }

            await Application.Current?.Dispatcher.InvokeAsync(() => IsGeneratingPreview = true);
            try
            {
                _previewPending = false;
                var slots = Slots.Select(s => s.ToSlotSpec()).ToList();
                if (slots.Count == 0)
                {
                    await Application.Current?.Dispatcher.InvokeAsync(() => LivePreviewImage = null);
                    return;
                }

                var isPdf = TemplatePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                var format = isPdf ? TemplateFormat.Pdf : TemplateFormat.Image;

                List<SkiaSharp.SKImage> pages;
                using (var stream = File.OpenRead(TemplatePath))
                {
                    pages = _numberingService.GeneratePreviewPages(
                        stream, slots, StartNumber, 1, format,
                        totalNumbers: TotalNumbers,
                        mode: IsImposedMode ? NumberingMode.Imposed : (IsLinearMode ? NumberingMode.Linear : NumberingMode.Auto),
                        numberFormat: CurrentNumberFormat,
                        useArabicDigits: UseArabicDigits);
                }

                if (pages.Count > 0)
                {
                    var bmp = SKImageExtensions.ToBitmapSource(pages[0]);
                    await Application.Current?.Dispatcher.InvokeAsync(() => LivePreviewImage = bmp);
                }
            }
            catch (Exception ex)
            {
                Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.LivePreview", ex);
            }
            finally
            {
                // Always released. The early return above (no fields) and an empty render
                // both used to leave this stuck at true, after which every later preview
                // was silently refused and the canvas kept showing an old picture for good.
                await Application.Current?.Dispatcher.InvokeAsync(() => IsGeneratingPreview = false);
            }

            if (_previewPending) SchedulePreviewUpdate();
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

        /// <summary>
        /// One sheet in the four-page preview, carrying its own caption.
        ///
        /// <para>The caption used to be derived in XAML from the item's position in the
        /// ItemsControl. That breaks the moment the item template gains a templated control
        /// of its own — wrapping each page in a button to make it clickable was enough for
        /// every sheet to come out labelled "صفحة ١". A page knows which page it is; it
        /// should not depend on where its caption sits in the visual tree.</para>
        /// </summary>
        public record PreviewPage(BitmapSource Image, string Label);

        [ObservableProperty] private ObservableCollection<PreviewPage> _multiPreviewPages = new();
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
                    pages = _numberingService.GeneratePreviewPages(
                        stream, slots, StartNumber, 4, format,
                        totalNumbers: TotalNumbers,
                        mode: IsImposedMode ? NumberingMode.Imposed : (IsLinearMode ? NumberingMode.Linear : NumberingMode.Auto),
                        numberFormat: CurrentNumberFormat,
                        useArabicDigits: UseArabicDigits);

                var labelFormat = L("Num_PreviewPageLabel");
                int pageNumber = 1;
                foreach (var page in pages)
                {
                    var bmp = SKImageExtensions.ToBitmapSource(page);
                    var label = string.Format(System.Globalization.CultureInfo.CurrentCulture, labelFormat, pageNumber++);
                    await Application.Current?.Dispatcher.InvokeAsync(
                        () => MultiPreviewPages.Add(new PreviewPage(bmp, label)));
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
        private void CloseMultiPreview()
        {
            OpenedPreviewPage = null;
            IsMultiPreviewOpen = false;
        }

        /// <summary>
        /// The page the operator asked to look at properly, or null while the four
        /// thumbnails are showing.
        ///
        /// <para>Four A4 sheets side by side in one dialog leaves each about 200&#160;px
        /// wide, and a numbering stamp at that scale is a few unreadable pixels. The point
        /// of this preview is to check the numbers before committing the press to twenty
        /// thousand sheets, and it could not be used for that — reported as "the preview is
        /// no use, I cannot open the page and see what is on it". Clicking a page now opens
        /// it at full size.</para>
        /// </summary>
        [ObservableProperty] private BitmapSource? _openedPreviewPage;

        /// <summary>True while a single page is open, so the dialog can swap what it shows.</summary>
        public bool IsPreviewPageOpen => OpenedPreviewPage != null;

        partial void OnOpenedPreviewPageChanged(BitmapSource? value)
            => OnPropertyChanged(nameof(IsPreviewPageOpen));

        [RelayCommand]
        private void OpenPreviewPage(PreviewPage? page)
        {
            if (page != null) OpenedPreviewPage = page.Image;
        }

        [RelayCommand]
        private void ClosePreviewPage() => OpenedPreviewPage = null;

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

                SaveRecentProjects();
            }
            catch (System.Exception ex) { Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.AddRecentProject", ex); }
        }

        /// <summary>
        /// Takes one design off the recent list. The list stores nothing but a path, so
        /// this removes the app's only reference to it — the operator's own file on disk is
        /// deliberately left alone.
        ///
        /// A shop reported putting an invoice design into the program and then being unable
        /// to get rid of it: the panel could open a project but never forget one, so the only
        /// way out was to delete the file in Windows and restart. That is not something a
        /// press operator should have to work out.
        /// </summary>
        [RelayCommand]
        private void RemoveRecentProject(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;

            RecentProjects.Remove(path);
            SaveRecentProjects();
        }

        /// <summary>Empties the recent list. Confirmed, because it is not undoable.</summary>
        [RelayCommand]
        private void ClearRecentProjects()
        {
            if (RecentProjects.Count == 0) return;

            var answer = MessageBox.Show(
                L("Num_ConfirmClearRecent"), L("Dlg_Confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            RecentProjects.Clear();
            SaveRecentProjects();
        }

        private void SaveRecentProjects()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentProjectsPath)!);
                File.WriteAllText(RecentProjectsPath, JsonSerializer.Serialize(RecentProjects.ToList()));
            }
            catch (System.Exception ex) { Apex.Core.Diagnostics.AppDiagnostics.LogWarning("Numbering.SaveRecentProjects", ex); }
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

