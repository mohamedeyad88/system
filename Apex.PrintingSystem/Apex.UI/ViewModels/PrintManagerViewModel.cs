using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using Apex.Services.Printing;
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

        // ── Files ──────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<string> _filesToPrint = new();
        [ObservableProperty] private ObservableCollection<IngestedFileItem> _ingestedFiles = new();

        // ── Status / Progress ──────────────────────────────────────────
        [ObservableProperty] private string _statusMessage = "قائمة الوثائق فارغة";
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

        // Collections
        public ObservableCollection<string> PrintQualities { get; } = new() { "Draft", "Normal", "High", "Best" };
        public ObservableCollection<string> Orientations   { get; } = new() { "Portrait", "Landscape" };
        public ObservableCollection<string> PaperSizes     { get; } = new() { "A4", "A3", "A5", "Letter", "Legal" };

        private int _duplicatesSkipped;

        public PrintManagerViewModel(BatchPrintJobManager batchPrintJobManager,
                                     IPrinterDiscoveryService printerService)
        {
            _batchPrintJobManager = batchPrintJobManager;
            _printerService       = printerService;
            LoadPresetsFromFile();
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
                var printers    = await _printerService.ScanAsync();
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

                    StatusMessage = AvailablePrinters.Count > 0
                        ? $"{AvailablePrinters.Count} طابعة متاحة"
                        : "لا توجد طابعات";
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadPrinters error: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    StatusMessage = "خطأ في تحميل الطابعات");
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
                Filter = "كل الملفات|*.*|PDF|*.pdf|صور|*.png;*.jpg;*.jpeg;*.bmp|مستندات|*.docx;*.xlsx;*.pptx"
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
                Title     = "اختر أي ملف داخل المجلد المطلوب",
                Filter    = "كل الملفات|*.*",
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

        private void AddFileToList(string filePath)
        {
            if (IngestedFiles.Any(f =>
                string.Equals(f.FullPath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                _duplicatesSkipped++;
                return;
            }

            var fi   = new FileInfo(filePath);
            var item = new IngestedFileItem
            {
                Index        = IngestedFiles.Count + 1,
                OriginalName = fi.Name,
                FullPath     = filePath,
                FolderPath   = fi.DirectoryName ?? "",
                SizeBytes    = fi.Length,
                SizeDisplay  = FormatFileSize(fi.Length),
                FileType     = fi.Extension.TrimStart('.').ToUpper(),
                ModifiedDate = fi.LastWriteTime,
                Copies       = DefaultCopies,
                PageRange    = "الكل"
            };
            IngestedFiles.Add(item);
            FilesToPrint.Add(filePath);
        }

        private void WarnDuplicates()
        {
            if (_duplicatesSkipped > 0)
            {
                StatusMessage = $"⚠  تم تخطي {_duplicatesSkipped} ملف مكرر";
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
            catch (Exception ex) { StatusMessage = $"خطأ في المعاينة: {ex.Message}"; }
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
                Name            = name,
                PaperSize       = PaperSize,
                Orientation     = Orientation,
                PrintQuality    = PrintQuality,
                DefaultCopies   = DefaultCopies,
                IsColorEnabled  = IsColorEnabled,
                IsDuplexEnabled = IsDuplexEnabled
            });

            PersistPresets();
            NewPresetName = "";
            StatusMessage = $"✓ تم حفظ القالب: {name}";
        }

        [RelayCommand]
        private void ApplyPreset(PrintPreset? preset)
        {
            if (preset == null) return;
            PaperSize       = preset.PaperSize;
            Orientation     = preset.Orientation;
            PrintQuality    = preset.PrintQuality;
            DefaultCopies   = preset.DefaultCopies;
            IsColorEnabled  = preset.IsColorEnabled;
            IsDuplexEnabled = preset.IsDuplexEnabled;
            StatusMessage   = $"✓ تم تطبيق: {preset.Name}";
        }

        [RelayCommand]
        private void DeletePreset(PrintPreset? preset)
        {
            if (preset == null) return;
            SavedPresets.Remove(preset);
            PersistPresets();
            StatusMessage = "تم حذف القالب";
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
            catch { }
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
            catch { }
        }

        // ══════════════════════════════════════════════════════════════
        //  PRINTER
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void OpenPrinterProperties()
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show("الرجاء اختيار طابعة أولاً", "خصائص الطابعة",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe")
                {
                    Arguments      = $"printui.dll,PrintUIEntry /e /n \"{SelectedPrinter}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذّر فتح خصائص الطابعة: {ex.Message}", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand] private void OpenSettings()  => IsSettingsOpen = true;
        [RelayCommand] private void CloseSettings() => IsSettingsOpen = false;

        [RelayCommand]
        private void ApplySettings()
        {
            foreach (var f in IngestedFiles) f.Copies = DefaultCopies;
            IsSettingsOpen = false;
            StatusMessage  = "✓ تم تطبيق الإعدادات على جميع الملفات";
        }

        [RelayCommand]
        private async Task Refresh() => await LoadPrintersAsync();

        // ══════════════════════════════════════════════════════════════
        //  PRINT
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private async Task CreateJobs()
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show("الرجاء اختيار طابعة", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (IngestedFiles.Count == 0)
            {
                MessageBox.Show("الرجاء إضافة ملفات للطباعة", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Quota check (skip in guest mode when user is null)
            var user = Apex.Services.Users.UserSessionManager.Instance.CurrentUser;
            if (user != null)
            {
                int totalPages = IngestedFiles.Count;
                bool isColor   = IsColorEnabled;
                var result = Apex.Services.Users.PrintQuotaManager.Instance.CheckQuota(user.Id, totalPages, isColor);
                if (!result.Allowed)
                {
                    MessageBox.Show(result.ReasonArabic, "تجاوز الحصة",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            IsPrinting    = true;
            ProgressValue = 0;

            try
            {
                var batchJobs = IngestedFiles.Select(f => new Apex.Core.Models.BatchJob
                {
                    FilePath = f.FullPath,
                    Status   = "Pending"
                }).ToList();

                var settings = new Apex.Core.Models.BatchSettings
                {
                    Copies              = DefaultCopies,
                    Duplex              = IsDuplexEnabled,
                    ColorMode           = IsColorEnabled,
                    DelayBetweenJobsMs  = 500,
                    StopOnError         = false
                };

                // Use named handlers so they can be unsubscribed after printing
                void OnJobStatus(object? s, Apex.Core.Models.BatchJob job) =>
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        CurrentPrintingFile = Path.GetFileName(job.FilePath);
                        StatusMessage       = $"جارٍ معالجة: {CurrentPrintingFile}";
                    });

                void OnBatchProgress(object? s, Apex.Services.Printing.BatchProgress p) =>
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ProgressValue = (int)p.PercentComplete;
                        StatusMessage = $"{p.CompletedJobs} / {p.TotalJobs} وظيفة مكتملة";
                    });

                _batchPrintJobManager.OnJobStatusChanged    += OnJobStatus;
                _batchPrintJobManager.OnBatchProgressChanged += OnBatchProgress;

                try
                {
                    await _batchPrintJobManager.ProcessBatchAsync(SelectedPrinter, batchJobs, settings);
                }
                finally
                {
                    // Always unsubscribe to avoid accumulation across multiple Print clicks
                    _batchPrintJobManager.OnJobStatusChanged    -= OnJobStatus;
                    _batchPrintJobManager.OnBatchProgressChanged -= OnBatchProgress;
                }

                // Record quota usage after successful print
                if (user != null)
                {
                    int totalPages = IngestedFiles.Count;
                    bool isColor   = IsColorEnabled;
                    Apex.Services.Users.PrintQuotaManager.Instance.RecordUsage(user.Id, totalPages, isColor);
                }

                MessageBox.Show("اكتملت الطباعة بنجاح ✓", "نجاح",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"فشل الطباعة: {ex.Message}", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsPrinting          = false;
                CurrentPrintingFile = "";
                StatusMessage       = "جاهز";
                ProgressValue       = 0;
            }
        }

        [RelayCommand]
        private Task StopPrinting()
        {
            _batchPrintJobManager.CancelBatch();
            StatusMessage = "جارٍ الإيقاف…";
            return Task.CompletedTask;
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        private void UpdateStatus()
        {
            StatusMessage = IngestedFiles.Count > 0
                ? $"{IngestedFiles.Count} وثيقة جاهزة للطباعة"
                : "قائمة الوثائق فارغة";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024)           return $"{bytes} B";
            if (bytes < 1024 * 1024)   return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  MODELS
    // ══════════════════════════════════════════════════════════════════

    public partial class IngestedFileItem : ObservableObject
    {
        [ObservableProperty] private int      _index;
        [ObservableProperty] private string   _originalName  = "";
        [ObservableProperty] private string   _fullPath      = "";
        [ObservableProperty] private string   _folderPath    = "";
        [ObservableProperty] private long     _sizeBytes;
        [ObservableProperty] private string   _sizeDisplay   = "";
        [ObservableProperty] private string   _fileType      = "";
        [ObservableProperty] private DateTime _modifiedDate;
        [ObservableProperty] private int      _copies        = 1;
        [ObservableProperty] private string   _pageRange     = "الكل";
        [ObservableProperty] private bool     _isArchiveMember;
        [ObservableProperty] private int      _estimatedPages;
    }

    public class PrintPreset
    {
        public string Name            { get; set; } = "";
        public string PaperSize       { get; set; } = "A4";
        public string Orientation     { get; set; } = "Portrait";
        public string PrintQuality    { get; set; } = "Normal";
        public int    DefaultCopies   { get; set; } = 1;
        public bool   IsColorEnabled  { get; set; } = true;
        public bool   IsDuplexEnabled { get; set; } = false;
        // Extended for PrintOperations
        public int    Copies          { get; set; } = 1;
        public bool   IsSingleSided   { get; set; } = true;
        public bool   IsDoubleSided   { get; set; } = false;
        public bool   IsColorPrint    { get; set; } = true;
        public bool   IsPortrait      { get; set; } = true;
        public bool   Collate         { get; set; } = true;
        public string FitMode         { get; set; } = "Fit";
        public int    FitCustomPercent { get; set; } = 100;
        public string PrintOrder      { get; set; } = "FirstToLast";
        public int    NUpMode         { get; set; } = 1;
        public string JobPriority     { get; set; } = "Normal";
    }
}
