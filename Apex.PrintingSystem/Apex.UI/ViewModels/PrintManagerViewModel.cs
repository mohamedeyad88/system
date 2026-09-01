using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using Apex.Services.Printing;
using System.Printing;
using Microsoft.Win32;
using System.IO;
using Apex.Core.Interfaces;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;

namespace Apex.UI.ViewModels
{
    public partial class PrintManagerViewModel : ViewModelBase
    {
        private readonly BatchPrintJobManager _batchPrintJobManager;
        private readonly IPrinterDiscoveryService _printerService;

        // ── Printers ───────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<string> _availablePrinters = new();
        [ObservableProperty] private string? _selectedPrinter;

        // ── Multi-printer mode ────────────────────────────────────────────────
        // The file queue already lived here; printing to SEVERAL printers lived in
        // the separate operations screen. Bringing them together is the whole point
        // of the merge: many files × many printers in one place.
        [ObservableProperty] private bool _isMultiPrinterMode;

        /// <summary>Printers ticked for a multi-printer run.</summary>
        public ObservableCollection<SelectablePrinter> PrinterChoices { get; } = new();

        /// <summary>0 = spread the queue (each file once), 1 = same files on every printer.</summary>
        [ObservableProperty] private int _distributionModeIndex;

        // Resolved on every read, not captured once at construction: a stored array
        // keeps the strings from the language that was active when the ViewModel was
        // built, so the list stayed English after switching to Arabic.
        public string[] AvailableDistributionModes =>
            new[] { L("PM_ModeLoadBalance"), L("PM_ModeDuplicate") };

        private Apex.Services.Printing.PrintDistributionMode DistributionMode =>
            DistributionModeIndex == 1
                ? Apex.Services.Printing.PrintDistributionMode.Duplicate
                : Apex.Services.Printing.PrintDistributionMode.LoadBalance;

        /// <summary>Printers the next run will actually use.</summary>
        private List<string> TargetPrinters =>
            IsMultiPrinterMode
                ? PrinterChoices.Where(p => p.IsSelected).Select(p => p.Name).ToList()
                : new List<string> { SelectedPrinter ?? "" };

        public int SelectedPrinterCount => TargetPrinters.Count(p => !string.IsNullOrWhiteSpace(p));

        /// <summary>The single-printer picker is meaningless while several printers are
        /// ticked — leaving both on screen invites "so which one wins?".</summary>
        public bool IsSinglePrinterMode => !IsMultiPrinterMode;

        /// <summary>Start is only a real action with something to print and somewhere to
        /// print it; otherwise the click used to land on a message box.</summary>
        public bool CanStartPrinting =>
            !IsPrinting && IngestedFiles.Count > 0 && SelectedPrinterCount > 0;

        /// <summary>Header summary, e.g. "12 files · 3 printers · 12 jobs".</summary>
        public string RoutingSummary
        {
            get
            {
                int files = IngestedFiles.Count;
                int printers = SelectedPrinterCount;
                if (files == 0 || printers == 0) return "";

                // Duplicate prints every file on every printer; load balance prints each once.
                int jobs = DistributionMode == Apex.Services.Printing.PrintDistributionMode.Duplicate
                    ? files * printers
                    : files;
                return Lf("PM_RoutingSummary", files, printers, jobs);
            }
        }

        partial void OnIsMultiPrinterModeChanged(bool value)
        {
            if (value) SyncPrinterChoices();
            OnPropertyChanged(nameof(IsSinglePrinterMode));
            RefreshRouting();
        }

        partial void OnDistributionModeIndexChanged(int value) => RefreshRouting();
        partial void OnSelectedPrinterChanged(string? value) => RefreshRouting();
        partial void OnIsPrintingChanged(bool value) => OnPropertyChanged(nameof(CanStartPrinting));

        /// <summary>Colours cycled per printer so each keeps the same swatch in the queue.</summary>
        private static readonly string[] PrinterColors =
            { "#3B82F6", "#22C55E", "#A855F7", "#F59E0B", "#EC4899", "#14B8A6" };

        /// <summary>
        /// Recomputes which printer each queued file goes to, using the SAME
        /// <see cref="Apex.Services.Printing.BatchPrintJobManager.TargetsFor"/> the
        /// print run uses — so the preview cannot disagree with what actually happens.
        /// </summary>
        public void RefreshRouting()
        {
            var printers = TargetPrinters.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

            for (int i = 0; i < IngestedFiles.Count; i++)
            {
                var file = IngestedFiles[i];
                if (printers.Count == 0)
                {
                    file.TargetPrinter = "";
                    continue;
                }

                var targets = Apex.Services.Printing.BatchPrintJobManager.TargetsFor(
                    printers, DistributionMode, i);

                file.TargetPrinter = targets.Count > 1
                    ? Lf("PM_AllPrinters", targets.Count)
                    : targets[0];

                // Colour follows the printer's position in the list, not the row,
                // so the same device always shows the same swatch.
                int colorIndex = targets.Count > 1 ? 0 : printers.IndexOf(targets[0]);
                file.TargetPrinterColor = PrinterColors[Math.Max(0, colorIndex) % PrinterColors.Length];
            }

            OnPropertyChanged(nameof(SelectedPrinterCount));
            OnPropertyChanged(nameof(SelectedPrintersSummary));
            OnPropertyChanged(nameof(RoutingSummary));
            OnPropertyChanged(nameof(CanStartPrinting));
        }

        /// <summary>Mirrors the discovered printers into the tickable list, keeping ticks.</summary>
        private void SyncPrinterChoices()
        {
            var ticked = PrinterChoices.Where(p => p.IsSelected).Select(p => p.Name).ToHashSet();

            foreach (var old in PrinterChoices) old.PropertyChanged -= OnPrinterTicked;
            PrinterChoices.Clear();

            foreach (var name in AvailablePrinters)
            {
                var choice = new SelectablePrinter(name)
                {
                    // Keep previous ticks; otherwise default to the single-mode choice.
                    IsSelected = ticked.Contains(name) || name == SelectedPrinter,
                };
                // Ticking a printer re-routes the queue immediately.
                choice.PropertyChanged += OnPrinterTicked;
                PrinterChoices.Add(choice);
            }
            ApplyPrinterFilter();
            RefreshRouting();
        }

        private void OnPrinterTicked(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectablePrinter.IsSelected)) RefreshRouting();
        }

        // ── Printer picker ────────────────────────────────────────────────────
        // A shop with fifty printers cannot pick three out of a 120px box of
        // checkboxes wedged into the header — and the header grew with the list,
        // pushing the queue toolbar off screen. Selection lives in a popup of
        // fixed height with a search box; the header shows only the outcome.

        /// <summary>What the popup currently lists — all printers, or those matching the search.</summary>
        public ObservableCollection<SelectablePrinter> FilteredPrinterChoices { get; } = new();

        [ObservableProperty] private string _printerFilter = "";
        [ObservableProperty] private bool _isPrinterPickerOpen;

        partial void OnPrinterFilterChanged(string value) => ApplyPrinterFilter();

        private void ApplyPrinterFilter()
        {
            var q = (PrinterFilter ?? "").Trim();

            FilteredPrinterChoices.Clear();
            foreach (var p in PrinterChoices)
            {
                if (q.Length == 0 ||
                    p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                {
                    FilteredPrinterChoices.Add(p);
                }
            }
            OnPropertyChanged(nameof(HasNoPrinterMatches));
        }

        public bool HasNoPrinterMatches =>
            PrinterChoices.Count > 0 && FilteredPrinterChoices.Count == 0;

        /// <summary>What the collapsed picker says, e.g. "3 printers selected".</summary>
        public string SelectedPrintersSummary
        {
            get
            {
                int n = PrinterChoices.Count(p => p.IsSelected);
                return n == 0 ? L("PM_NoPrintersPicked") : Lf("PM_NPrintersPicked", n);
            }
        }

        /// <summary>Ticks everything the search currently shows — "all HP" in one click.</summary>
        [RelayCommand]
        private void SelectAllPrinters()
        {
            foreach (var p in FilteredPrinterChoices) p.IsSelected = true;
        }

        [RelayCommand]
        private void ClearPrinterSelection()
        {
            foreach (var p in PrinterChoices) p.IsSelected = false;
        }

        // ── Files ──────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<string> _filesToPrint = new();
        [ObservableProperty] private ObservableCollection<IngestedFileItem> _ingestedFiles = new();

        // ── Status / Progress ──────────────────────────────────────────
        [ObservableProperty] private string _statusMessage = L("PM_DocListEmpty");
        [ObservableProperty] private int _progressValue;
        [ObservableProperty] private bool _isPrinting;
        [ObservableProperty] private string _currentPrintingFile = "";

        // ── Settings ───────────────────────────────────────────────────
        [ObservableProperty] private bool _isSettingsOpen;
        [ObservableProperty] private bool _isDuplexEnabled = false;
        [ObservableProperty] private bool _isColorEnabled = true;
        [ObservableProperty] private string _printQuality = "Normal";
        [ObservableProperty] private string _orientation = "Portrait";
        [ObservableProperty] private int _defaultCopies = 1;
        [ObservableProperty] private string _paperSize = "A4";

        // ── Presets ────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<PrintPreset> _savedPresets = new();
        [ObservableProperty] private PrintPreset? _selectedPreset;
        [ObservableProperty] private string _newPresetName = "";
        [ObservableProperty] private bool _isPresetPanelOpen;

        // Collections. Quality and orientation are shown translated but stored
        // invariant; paper sizes are international designations and stay as-is.
        public ObservableCollection<LocalizedOption> PrintQualities { get; } = new()
        {
            new("Draft",  "PM_QualityDraft"),
            new("Normal", "PM_QualityNormal"),
            new("High",   "PM_QualityHigh"),
            new("Best",   "PM_QualityBest"),
        };

        public ObservableCollection<LocalizedOption> Orientations { get; } = new()
        {
            new("Portrait",  "PM_OrientPortrait"),
            new("Landscape", "PM_OrientLandscape"),
        };

        public ObservableCollection<string> PaperSizes { get; } = new() { "A4", "A3", "A5", "Letter", "Legal" };

        private int _duplicatesSkipped;

        public PrintManagerViewModel(BatchPrintJobManager batchPrintJobManager,
                                     IPrinterDiscoveryService printerService,
                                     Apex.Services.PrinterMonitoringService? monitoring = null)
        {
            _batchPrintJobManager = batchPrintJobManager;
            _printerService = printerService;
            LoadPresetsFromFile();

            // The alert hangs off the monitor rather than the batch, so a cover
            // opened in the middle of a document is announced at once instead of
            // when the run next reaches that station.
            if (monitoring != null)
                monitoring.PrinterStatusChanged += OnPrinterStatusChanged;

            _batchPrintJobManager.OnPrinterHeld += (_, e) => NoteHeld(e.PrinterName, e.Fault);
            _batchPrintJobManager.OnPrinterResumed += (_, printer) => ClearHeld(printer);

            // Re-route whenever the queue changes, so the printer column and the
            // header summary always describe the CURRENT queue.
            IngestedFiles.CollectionChanged += (_, _) => RefreshRouting();

            // The routing strings are composed here, not bound to a DynamicResource,
            // so switching language has to make us rebuild them or the header summary
            // and the printer chips stay in the previous language.
            Apex.UI.Services.LocalizationService.Instance.PropertyChanged += OnLanguageChanged;
        }

        // ══════════════════════════════════════════════════════════════
        //  PRINTERS THAT NEED SOMEBODY
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// The banner text, or empty when every station is fine.
        ///
        /// A banner rather than a dialog: with eight or nine printers, faults arrive
        /// together, and stacked modal dialogs would bury the screen and train the
        /// operator to dismiss them unread.
        /// </summary>
        [ObservableProperty] private string _alertMessage = "";

        public bool HasAlert => !string.IsNullOrEmpty(AlertMessage);

        partial void OnAlertMessageChanged(string value) => OnPropertyChanged(nameof(HasAlert));

        /// <summary>Fault per printer, so the banner can name several at once.</summary>
        private readonly Dictionary<string, string> _heldPrinters =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The monitor re-announces every printer on each sweep, so only a CHANGE is
        /// worth a sound — otherwise a single open cover would beep every few seconds.
        /// </summary>
        private readonly Dictionary<string, string> _lastCondition =
            new(StringComparer.OrdinalIgnoreCase);

        private void OnPrinterStatusChanged(object? sender, Apex.Services.PrinterStatusEventArgs e)
        {
            // Only stations this run is actually using; a broken printer in another
            // room is not this operator's problem right now.
            if (TargetPrinters == null || !TargetPrinters.Contains(e.PrinterName, StringComparer.OrdinalIgnoreCase))
                return;

            string condition = e.RequiresIntervention ? e.Status : "";
            if (_lastCondition.TryGetValue(e.PrinterName, out var previous) && previous == condition)
                return;

            _lastCondition[e.PrinterName] = condition;

            if (e.RequiresIntervention) NoteHeld(e.PrinterName, e.Status);
            else ClearHeld(e.PrinterName);
        }

        private void NoteHeld(string printer, string fault)
        {
            bool isNew = !_heldPrinters.ContainsKey(printer) || _heldPrinters[printer] != fault;
            _heldPrinters[printer] = fault;
            RebuildAlert();

            // The operator is usually at a machine, not at the screen.
            if (isNew) System.Media.SystemSounds.Exclamation.Play();
        }

        private void ClearHeld(string printer)
        {
            if (_heldPrinters.Remove(printer)) RebuildAlert();
        }

        private void RebuildAlert()
        {
            if (_heldPrinters.Count == 0)
            {
                AlertMessage = "";
                return;
            }

            AlertMessage = string.Join("  ·  ",
                _heldPrinters.Select(p => $"{p.Key} — {LocalizedFault(p.Value)}"));
        }

        /// <summary>
        /// The monitor reports faults in the invariant WMI vocabulary; the operator
        /// reads the screen in their own language.
        /// </summary>
        private string LocalizedFault(string fault) => fault switch
        {
            "Out of Paper"    => L("PM_FaultOutOfPaper"),
            "Out of Toner"    => L("PM_FaultOutOfToner"),
            "Door Open"       => L("PM_FaultDoorOpen"),
            "Paper Jam"       => L("PM_FaultPaperJam"),
            "Output Bin Full" => L("PM_FaultBinFull"),
            "Paper Problem"   => L("PM_FaultPaperProblem"),
            "Offline"         => L("PM_FaultOffline"),
            "Queue Stuck"     => L("PM_FaultQueueStuck"),
            _                 => L("PM_FaultNeedsAttention")
        };

        /// <summary>Stop waiting on a station the operator has taken out of service.</summary>
        [RelayCommand]
        private void AbandonHeldPrinters()
        {
            foreach (var printer in _heldPrinters.Keys.ToList())
                _batchPrintJobManager.AbandonPrinter(printer);

            _heldPrinters.Clear();
            RebuildAlert();
        }

        /// <summary>
        /// Clears the Windows spooler queue for every held printer — the explicit way
        /// out of a pile-up (e.g. a stack of jobs stuck on an unplugged/absent printer,
        /// the reported "83 jobs that never printed"). Cancels those jobs after a
        /// confirmation and stops waiting on the station. This is a deliberate operator
        /// action on stuck work, not the silent skipping the shop's policy forbids.
        /// </summary>
        [RelayCommand]
        private void PurgeHeldPrinters()
        {
            var printers = _heldPrinters.Keys.ToList();
            if (printers.Count == 0) return;

            if (System.Windows.MessageBox.Show(
                    Lf("PM_PurgeConfirm", string.Join("، ", printers)),
                    L("PM_PurgeTitle"),
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
                return;

            foreach (var printer in printers)
            {
                try
                {
                    using var server = new LocalPrintServer();
                    using var queue = server.GetPrintQueue(printer);
                    queue.Purge();
                }
                catch { /* best-effort: a queue we can't open is one we can't clear */ }
                _batchPrintJobManager.AbandonPrinter(printer);
            }

            _heldPrinters.Clear();
            RebuildAlert();
        }

        private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Apex.UI.Services.LocalizationService.CurrentCulture)) return;

            // Re-reading the mode list resets the ComboBox selection, so restore it.
            int mode = DistributionModeIndex;
            OnPropertyChanged(nameof(AvailableDistributionModes));
            DistributionModeIndex = mode;

            // These keep their identity across the switch (only Label changes), so the
            // selection survives on its own.
            foreach (var o in Orientations) o.RefreshLabel();
            foreach (var o in PrintQualities) o.RefreshLabel();

            if (_statusComposer != null) StatusMessage = _statusComposer();

            // Rows still showing the default "all" follow the language; a range the
            // operator actually typed is their text and must survive untouched.
            foreach (var f in IngestedFiles)
                if (NormalizePageRange(f.PageRange) == "All") f.PageRange = L("Des_All");

            RefreshRouting();
        }

        // ══════════════════════════════════════════════════════════════
        //  INIT
        // ══════════════════════════════════════════════════════════════

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await LoadPrintersAsync();
        }

        private async Task LoadPrintersAsync()
        {
            try
            {
                var printers = await _printerService.ScanAsync();
                var printerList = printers.ToList();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AvailablePrinters.Clear();
                    foreach (var p in printerList) AvailablePrinters.Add(p.Name);

                    if (AvailablePrinters.Count > 0 && string.IsNullOrEmpty(SelectedPrinter))
                    {
                        var def = printerList.FirstOrDefault(p => p.IsDefault);
                        SelectedPrinter = def?.Name ?? AvailablePrinters[0];
                    }

                    SetStatus(() => AvailablePrinters.Count > 0
                        ? Lf("PM_PrintersAvailable", AvailablePrinters.Count)
                        : L("PM_NoPrinters"));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadPrinters error: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    StatusMessage = L("PM_PrinterLoadError"));
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  FILES
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void BrowseFiles()
        {
            _duplicatesSkipped = 0;
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = L("PM_FileFilter")
            };
            if (dialog.ShowDialog() != true) return;

            foreach (var f in dialog.FileNames) AddFileToList(f);
            UpdateStatus();
            WarnDuplicates();
        }

        [RelayCommand]
        private void BrowseFolder()
        {
            _duplicatesSkipped = 0;
            var dialog = new OpenFileDialog
            {
                Title = L("PM_ChooseAnyInFolder"),
                Filter = L("PM_AllFilesFilter"),
                Multiselect = true
            };
            if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;

            var folder = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrEmpty(folder)) return;

            foreach (var f in Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly))
                AddFileToList(f);

            UpdateStatus();
            WarnDuplicates();
        }

        [RelayCommand]
        private void DropFiles(string[] files)
        {
            _duplicatesSkipped = 0;
            foreach (var f in files) AddFileToList(f);
            UpdateStatus();
            WarnDuplicates();
        }

        /// <summary>
        /// Adds a file produced by another section (Page Tools / Imposition) to the
        /// print queue — the receiving end of the "send to printing" hand-off.
        /// </summary>
        public void IngestExternalFile(string filePath)
        {
            AddFileToList(filePath);
            UpdateStatus();
            OnPropertyChanged(nameof(CanStartPrinting));
        }

        private void AddFileToList(string filePath)
        {
            if (IngestedFiles.Any(f =>
                string.Equals(f.FullPath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                _duplicatesSkipped++;
                return;
            }

            var fi = new FileInfo(filePath);
            var item = new IngestedFileItem
            {
                Index = IngestedFiles.Count + 1,
                OriginalName = fi.Name,
                FullPath = filePath,
                FolderPath = fi.DirectoryName ?? "",
                SizeBytes = fi.Length,
                SizeDisplay = FormatFileSize(fi.Length),
                FileType = fi.Extension.TrimStart('.').ToUpper(),
                ModifiedDate = fi.LastWriteTime,
                Copies = DefaultCopies,
                PageRange = L("Des_All")
            };
            IngestedFiles.Add(item);
            FilesToPrint.Add(filePath);
        }

        private void WarnDuplicates()
        {
            if (_duplicatesSkipped > 0)
            {
                StatusMessage = Lf("PM_DupSkipped", _duplicatesSkipped);
                _duplicatesSkipped = 0;
            }
        }

        // ── Per-file reorder ───────────────────────────────────────────

        [RelayCommand]
        private void MoveUp(IngestedFileItem? item)
        {
            if (item == null) return;
            int i = IngestedFiles.IndexOf(item);
            if (i <= 0) return;
            IngestedFiles.Move(i, i - 1);
            RefreshIndexes();
        }

        [RelayCommand]
        private void MoveDown(IngestedFileItem? item)
        {
            if (item == null) return;
            int i = IngestedFiles.IndexOf(item);
            if (i < 0 || i >= IngestedFiles.Count - 1) return;
            IngestedFiles.Move(i, i + 1);
            RefreshIndexes();
        }

        [RelayCommand]
        private void RemoveItem(IngestedFileItem? item)
        {
            if (item == null) return;
            FilesToPrint.Remove(item.FullPath);
            IngestedFiles.Remove(item);
            RefreshIndexes();
            UpdateStatus();
        }

        [RelayCommand]
        private void PreviewFile(IngestedFileItem? item)
        {
            if (item == null || !File.Exists(item.FullPath)) return;
            try { Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true }); }
            catch (Exception ex) { StatusMessage = Lf("PM_PreviewError", ex.Message); }
        }

        private void RefreshIndexes()
        {
            for (int i = 0; i < IngestedFiles.Count; i++)
                IngestedFiles[i].Index = i + 1;
        }

        [RelayCommand]
        private void ClearList()
        {
            IngestedFiles.Clear();
            FilesToPrint.Clear();
            UpdateStatus();
        }

        [RelayCommand]
        private void RemoveFile(string filePath)
        {
            var item = IngestedFiles.FirstOrDefault(f => f.FullPath == filePath);
            if (item != null) IngestedFiles.Remove(item);
            FilesToPrint.Remove(filePath);
            RefreshIndexes();
            UpdateStatus();
        }

        // ══════════════════════════════════════════════════════════════
        //  COPIES
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void IncreaseCopies() => DefaultCopies = Math.Min(DefaultCopies + 1, 999);

        [RelayCommand]
        private void DecreaseCopies() => DefaultCopies = Math.Max(DefaultCopies - 1, 1);

        // ══════════════════════════════════════════════════════════════
        //  PRINT PRESETS
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void TogglePresetPanel() => IsPresetPanelOpen = !IsPresetPanelOpen;

        [RelayCommand]
        private void SavePreset()
        {
            var name = NewPresetName.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var existing = SavedPresets.FirstOrDefault(p => p.Name == name);
            if (existing != null) SavedPresets.Remove(existing);

            SavedPresets.Add(new PrintPreset
            {
                Name = name,
                PaperSize = PaperSize,
                Orientation = Orientation,
                PrintQuality = PrintQuality,
                DefaultCopies = DefaultCopies,
                IsColorEnabled = IsColorEnabled,
                IsDuplexEnabled = IsDuplexEnabled
            });

            PersistPresets();
            NewPresetName = "";
            StatusMessage = Lf("PM_TemplateSaved", name);
        }

        [RelayCommand]
        private void ApplyPreset(PrintPreset? preset)
        {
            if (preset == null) return;
            PaperSize = preset.PaperSize;
            Orientation = preset.Orientation;
            PrintQuality = preset.PrintQuality;
            DefaultCopies = preset.DefaultCopies;
            IsColorEnabled = preset.IsColorEnabled;
            IsDuplexEnabled = preset.IsDuplexEnabled;
            StatusMessage = Lf("PM_Applied", preset.Name);
        }

        [RelayCommand]
        private void DeletePreset(PrintPreset? preset)
        {
            if (preset == null) return;
            SavedPresets.Remove(preset);
            PersistPresets();
            StatusMessage = L("PM_TemplateDeleted");
        }

        private string PresetsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Apex", "print-presets.json");

        private void PersistPresets()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PresetsPath)!);
                File.WriteAllText(PresetsPath, JsonSerializer.Serialize(
                    SavedPresets.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (System.Exception ex) { Apex.Core.Diagnostics.AppDiagnostics.LogWarning("PrintManager.PersistPresets", ex); }
        }

        private void LoadPresetsFromFile()
        {
            try
            {
                if (!File.Exists(PresetsPath)) return;
                var list = JsonSerializer.Deserialize<List<PrintPreset>>(File.ReadAllText(PresetsPath));
                if (list == null) return;
                SavedPresets.Clear();
                foreach (var p in list) SavedPresets.Add(p);
            }
            catch (System.Exception ex) { Apex.Core.Diagnostics.AppDiagnostics.LogWarning("PrintManager.LoadPresets", ex); }
        }

        // ══════════════════════════════════════════════════════════════
        //  PRINTER
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void OpenPrinterProperties()
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show(L("PM_SelectPrinterFirst"), L("PM_PrinterProps"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe")
                {
                    Arguments = $"printui.dll,PrintUIEntry /e /n \"{SelectedPrinter}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lf("PM_PropsOpenError", ex.Message), L("Dlg_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand] private void OpenSettings() => IsSettingsOpen = true;
        [RelayCommand] private void CloseSettings() => IsSettingsOpen = false;

        [RelayCommand]
        private void ApplySettings()
        {
            foreach (var f in IngestedFiles) f.Copies = DefaultCopies;
            IsSettingsOpen = false;
            StatusMessage = L("PM_SettingsAppliedAll");
        }

        [RelayCommand]
        private async Task Refresh() => await LoadPrintersAsync();

        // ══════════════════════════════════════════════════════════════
        //  PRINT
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private async Task CreateJobs()
        {
            // In multi-printer mode the tick list is what counts, not the single combo.
            if (SelectedPrinterCount == 0)
            {
                MessageBox.Show(L("PM_SelectPrinter"), L("Dlg_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (IngestedFiles.Count == 0)
            {
                MessageBox.Show(L("PM_AddFiles"), L("Dlg_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsPrinting = true;
            ProgressValue = 0;

            try
            {
                var batchJobs = IngestedFiles.Select(f => new Apex.Core.Models.BatchJob
                {
                    FilePath = f.FullPath,
                    // A row edited to 3 copies must print 3, not the toolbar default.
                    Copies = f.Copies > 0 ? f.Copies : DefaultCopies,
                    PageRange = NormalizePageRange(f.PageRange),
                    Status = "Pending"
                }).ToList();

                var settings = new Apex.Core.Models.BatchSettings
                {
                    Copies = DefaultCopies,
                    Duplex = IsDuplexEnabled,
                    ColorMode = IsColorEnabled,
                    PaperSize = PaperSize,
                    Orientation = Orientation,
                    Quality = PrintQuality,
                    DelayBetweenJobsMs = 500,
                    StopOnError = false
                };

                // Use named handlers so they can be unsubscribed after printing
                void OnJobStatus(object? s, Apex.Core.Models.BatchJob job) =>
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        CurrentPrintingFile = Path.GetFileName(job.FilePath);
                        StatusMessage = Lf("PM_Processing", CurrentPrintingFile);
                    });

                void OnBatchProgress(object? s, Apex.Services.Printing.BatchProgress p) =>
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ProgressValue = (int)p.PercentComplete;
                        // Show failures as they happen. Reporting only successes let a
                        // run where every job failed look identical to one that had
                        // simply not started yet.
                        StatusMessage = p.FailedJobs > 0
                            ? Lf("PM_JobsDoneWithFailures", p.CompletedJobs, p.TotalJobs, p.FailedJobs)
                            : Lf("PM_JobsDone", p.CompletedJobs, p.TotalJobs);
                    });

                _batchPrintJobManager.OnJobStatusChanged += OnJobStatus;
                _batchPrintJobManager.OnBatchProgressChanged += OnBatchProgress;

                Apex.Services.Printing.BatchResult result;
                try
                {
                    result = await _batchPrintJobManager.ProcessBatchAsync(
                        TargetPrinters, batchJobs, settings, DistributionMode);
                }
                finally
                {
                    // Always unsubscribe to avoid accumulation across multiple Print clicks
                    _batchPrintJobManager.OnJobStatusChanged -= OnJobStatus;
                    _batchPrintJobManager.OnBatchProgressChanged -= OnBatchProgress;
                }

                ShowBatchReport(result);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lf("PM_PrintFailed", ex.Message), L("Dlg_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsPrinting = false;
                CurrentPrintingFile = "";
                // Deliberately NOT resetting StatusMessage/ProgressValue here.
                // Wiping them the instant the run ended erased the only record of what
                // happened, so the operator was left with a blank bar and no outcome.
                // ShowBatchReport leaves the final line in place; the next run resets it.
            }
        }

        /// <summary>
        /// Tells the operator what the run actually did.
        ///
        /// This used to be a single "printing finished" dialog shown whether every job
        /// printed or none did — with the counts computed by the batch manager and then
        /// thrown away. A print shop cannot act on "done": it needs to know how many
        /// came out, and which files did not, before it walks to the machine.
        /// </summary>
        private void ShowBatchReport(Apex.Services.Printing.BatchResult r)
        {
            ProgressValue = r.TotalJobs > 0
                ? (int)Math.Round(100.0 * (r.Succeeded + r.Failed) / r.TotalJobs)
                : 0;

            if (r.WasCancelled)
            {
                StatusMessage = Lf("PM_ReportCancelled", r.Succeeded, r.TotalJobs);
                MessageBox.Show(StatusMessage, L("Dlg_Warning"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (r.AllSucceeded)
            {
                StatusMessage = Lf("PM_ReportAllOk", r.Succeeded);
                MessageBox.Show(StatusMessage, L("Dlg_Success"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Something failed. Lead with COPIES, because that is what the operator can
            // verify at the machine: a run where one of nine printers refused every
            // file still put eight copies of each in someone's hands, and reporting
            // "0 of 3 succeeded" sent them looking for a fault that was not there.
            var lines = new System.Text.StringBuilder();
            lines.AppendLine(Lf("PM_ReportCopies", r.CopiesPrinted, r.CopiesTotal));
            lines.AppendLine(Lf("PM_ReportFilesComplete", r.Succeeded, r.TotalJobs));

            if (r.Failures.Count > 0)
            {
                lines.AppendLine();

                // Grouped by PRINTER, not by file.
                //
                // A run sent to nine devices where one of them cannot print silently
                // fails every job — and a flat list of file names makes that look like
                // nine separate problems instead of one bad printer. Grouping puts the
                // device the operator has to change at the top of each block.
                var byPrinter = r.Failures
                    .GroupBy(f => f.Printer ?? L("PM_ReportNoPrinter"))
                    .OrderByDescending(g => g.Count())
                    .ToList();

                const int MaxPrinters = 6;
                const int MaxFilesPerPrinter = 4;

                foreach (var group in byPrinter.Take(MaxPrinters))
                {
                    lines.AppendLine(Lf("PM_ReportPrinterFailed", group.Key, group.Count()));

                    // The reason is nearly always identical within a printer; show it once.
                    var reason = group.First().Reason;
                    lines.AppendLine($"    {reason}");

                    foreach (var f in group.Take(MaxFilesPerPrinter))
                        lines.AppendLine($"      • {f.FileName}");

                    if (group.Count() > MaxFilesPerPrinter)
                        lines.AppendLine(Lf("PM_ReportAndMore", group.Count() - MaxFilesPerPrinter));

                    lines.AppendLine();
                }

                if (byPrinter.Count > MaxPrinters)
                    lines.AppendLine(Lf("PM_ReportAndMorePrinters", byPrinter.Count - MaxPrinters));
            }

            // Held copies are listed apart from failures. Nothing went wrong with
            // them — their station was waiting for a person when the run stopped —
            // and mixing them in would send the operator hunting for a fault when
            // the answer is a tray to fill or a cover to close.
            if (r.HasHeldCopies)
            {
                lines.AppendLine();
                lines.AppendLine(Lf("PM_ReportHeldCopies", r.CopiesHeld));

                foreach (var group in r.HeldCopies
                             .GroupBy(h => h.Printer ?? L("PM_ReportNoPrinter"))
                             .OrderByDescending(g => g.Count())
                             .Take(6))
                {
                    lines.AppendLine($"  • {group.Key} — {group.First().Reason} ({group.Count()})");
                }
            }

            StatusMessage = Lf("PM_ReportCopies", r.CopiesPrinted, r.CopiesTotal);

            MessageBox.Show(lines.ToString(), L("Dlg_Warning"),
                MessageBoxButton.OK,
                // Partly printed is a warning; nothing at all is an error. Treating
                // both as errors trains the operator to dismiss the dialog unread.
                r.NothingPrinted ? MessageBoxImage.Error : MessageBoxImage.Warning);
        }

        [RelayCommand]
        private Task StopPrinting()
        {
            _batchPrintJobManager.CancelBatch();
            StatusMessage = L("PM_Stopping");
            return Task.CompletedTask;
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        /// <summary>How the current resting status line is built. Kept as a function
        /// rather than a finished string so a language switch can rebuild it — the
        /// text was composed once and stayed in the startup language otherwise.</summary>
        private Func<string>? _statusComposer;

        private void SetStatus(Func<string> composer)
        {
            _statusComposer = composer;
            StatusMessage = composer();
        }

        private void UpdateStatus() =>
            SetStatus(() => IngestedFiles.Count > 0
                ? Lf("PM_DocsReady", IngestedFiles.Count)
                : L("PM_DocListEmpty"));

        /// <summary>
        /// Turns the queue's page-range text into what the pipeline understands.
        /// The cell is seeded with the LOCALIZED word for "all", so a queue built in
        /// Arabic would hand the driver "الكل" and a range of one page could come out
        /// as the whole document.
        /// </summary>
        private static string NormalizePageRange(string? text)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0) return "All";

            // Every spelling of "all" we can produce, in either language.
            if (string.Equals(t, "All", StringComparison.OrdinalIgnoreCase)) return "All";
            if (string.Equals(t, L("Des_All"), StringComparison.OrdinalIgnoreCase)) return "All";
            if (t == "الكل") return "All";

            return t;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  MODELS
    // ══════════════════════════════════════════════════════════════════

    public partial class IngestedFileItem : ObservableObject
    {
        [ObservableProperty] private int _index;
        [ObservableProperty] private string _originalName = "";
        [ObservableProperty] private string _fullPath = "";
        [ObservableProperty] private string _folderPath = "";
        [ObservableProperty] private long _sizeBytes;
        [ObservableProperty] private string _sizeDisplay = "";
        [ObservableProperty] private string _fileType = "";
        [ObservableProperty] private DateTime _modifiedDate;
        [ObservableProperty] private int _copies = 1;
        [ObservableProperty] private string _pageRange = ViewModelBase.L("Des_All");
        [ObservableProperty] private bool _isArchiveMember;
        [ObservableProperty] private int _estimatedPages;

        /// <summary>
        /// The printer(s) this file will actually go to, shown in the queue BEFORE
        /// printing starts. Without it the operator cannot tell where a file will
        /// come out — and a mis-routed run is only discovered at the device.
        /// </summary>
        [ObservableProperty] private string _targetPrinter = "";

        /// <summary>Stable colour per printer so the routing reads at a glance.</summary>
        [ObservableProperty] private string _targetPrinterColor = "#64748B";

        public bool HasTargetPrinter => !string.IsNullOrEmpty(TargetPrinter);

        partial void OnTargetPrinterChanged(string value) => OnPropertyChanged(nameof(HasTargetPrinter));
    }

    public class PrintPreset
    {
        public string Name { get; set; } = "";
        public string PaperSize { get; set; } = "A4";
        public string Orientation { get; set; } = "Portrait";
        public string PrintQuality { get; set; } = "Normal";
        public int DefaultCopies { get; set; } = 1;
        public bool IsColorEnabled { get; set; } = true;
        public bool IsDuplexEnabled { get; set; } = false;
        // Extended for PrintOperations
        public int Copies { get; set; } = 1;
        public bool IsSingleSided { get; set; } = true;
        public bool IsDoubleSided { get; set; } = false;
        public bool IsColorPrint { get; set; } = true;
        public bool IsPortrait { get; set; } = true;
        public bool Collate { get; set; } = true;
        public string FitMode { get; set; } = "Fit";
        public int FitCustomPercent { get; set; } = 100;
        public string PrintOrder { get; set; } = "FirstToLast";
        public int NUpMode { get; set; } = 1;
        public string JobPriority { get; set; } = "Normal";
    }

    /// <summary>A printer the operator can tick for a multi-printer run.</summary>
    public partial class SelectablePrinter : ObservableObject
    {
        public string Name { get; }

        [ObservableProperty] private bool _isSelected;

        public SelectablePrinter(string name) => Name = name;
    }

    /// <summary>A dropdown entry whose stored value stays invariant while its label
    /// follows the UI language. The print pipeline matches on <see cref="Value"/>
    /// ("Portrait", "Normal", …), so translating the list must never translate what
    /// gets saved into a preset or handed to the driver.</summary>
    public class LocalizedOption : ObservableObject
    {
        private readonly string _resourceKey;

        public string Value { get; }
        public string Label => ViewModelBase.L(_resourceKey);

        public LocalizedOption(string value, string resourceKey)
        {
            Value = value;
            _resourceKey = resourceKey;
        }

        public void RefreshLabel() => OnPropertyChanged(nameof(Label));
    }
}
