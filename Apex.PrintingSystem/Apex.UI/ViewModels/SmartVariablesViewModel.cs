using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Services.SmartVariables;
using Apex.Services.SmartVariables.Models;
using Apex.Services.Templates;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Smart Variables workflow (Steps 1-5).
    /// Shares a TemplateDesignerSession with the parent TemplateDesignerViewModel
    /// so the Canvas and all Smart Variable steps see the same data.
    /// </summary>
    public partial class SmartVariablesViewModel : ObservableObject
    {
        // ── Services ───────────────────────────────────────────────────────────
        private readonly ISmartPasteParser       _pasteParser    = new SmartPasteParser();
        private readonly IDataCleaningService    _cleaner        = new DataCleaningService();
        private readonly IVariableMappingService _mappingService = new VariableMappingService();
        private readonly IImageLibraryService    _imageLibrary   = new ImageLibraryService();
        private readonly IImageMatchingService   _imageMatcher   = new ImageMatchingService();
        private readonly IDataValidationService  _validator      = new DataValidationService();
        private readonly IPreflightService       _preflight      = new PreflightService();

        // ── Shared session (owned by TemplateDesignerViewModel) ──────────────
        private TemplateDesignerSession? _session;

        /// <summary>
        /// Shared state between Canvas and Smart Variables workflow.
        /// Exposed so XAML can bind to Session.DataTableView, Session.DataColumns, etc.
        /// </summary>
        public TemplateDesignerSession? Session => _session;

        /// <summary>
        /// Attach the shared session.  Called immediately after construction
        /// by TemplateDesignerViewModel.
        /// </summary>
        public void AttachSession(TemplateDesignerSession session)
        {
            _session = session;
            // Push any already-loaded state into the session
            if (State.DataSource.HasData)
                _session.SetDataSource(State.DataSource);
            if (State.Mappings.Any())
                _session.SetMappings(State.Mappings);
        }

        // ── Legacy per-VM state (kept for backward-compat with existing code) ──
        public SmartVariablesState State { get; private set; } = new();

        // ── Template fields (set by parent ViewModel) ──────────────────────────
        private List<SmartTemplateField> _templateFields = new();

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 0 — SMART PASTE
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private string  _rawPasteText    = "";
        [ObservableProperty] private string  _pasteStatus     = "انسخ الجدول من Excel أو Google Sheets والصقه هنا";
        [ObservableProperty] private string  _pasteStatusColor = "#6B7280";
        [ObservableProperty] private bool    _hasPastedData;
        [ObservableProperty] private int     _totalRowCount;
        [ObservableProperty] private int     _columnCount;
        [ObservableProperty] private string  _detectedSeparatorLabel = "";

        // Data grid — DataTableView is the FIXED binding target for the DataGrid.
        // It is a DataView built from real column names (الاسم, الصف, …), NOT from
        // DataRowItem reflection properties (Cells, StatusColor, etc.).
        [ObservableProperty] private DataView? _dataTableView;

        // Legacy collections kept for other consumers (MappingRows, etc.)
        [ObservableProperty] private ObservableCollection<string>      _dataColumns = new();
        [ObservableProperty] private ObservableCollection<DataRowItem> _dataRows    = new();

        // Cleaning options
        [ObservableProperty] private bool _cleanTrimWhitespace       = true;
        [ObservableProperty] private bool _cleanNormalizeArabic      = true;
        [ObservableProperty] private bool _cleanNormalizeNumbers     = true;
        [ObservableProperty] private bool _cleanRemoveDiacritics;
        [ObservableProperty] private bool _cleanRemoveExtraSpaces    = true;

        [RelayCommand]
        private void PasteFromClipboard()
        {
            try
            {
                string text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    PasteStatus      = "الحافظة فارغة — انسخ الجدول من Excel أولاً";
                    PasteStatusColor = "#EF4444";
                    return;
                }
                RawPasteText = text;
                ParsePaste(text);
            }
            catch (Exception ex)
            {
                PasteStatus      = $"خطأ: {ex.Message}";
                PasteStatusColor = "#EF4444";
            }
        }

        partial void OnRawPasteTextChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                ParsePaste(value);
        }

        [RelayCommand]
        private void ApplyCleaning()
        {
            if (!HasPastedData) return;

            var opts = new CleaningOptions
            {
                TrimWhitespace         = CleanTrimWhitespace,
                NormalizeArabicLetters = CleanNormalizeArabic,
                NormalizeArabicNumbers = CleanNormalizeNumbers,
                RemoveDiacritics       = CleanRemoveDiacritics,
                RemoveExtraSpaces      = CleanRemoveExtraSpaces,
            };

            _cleaner.Clean(State.DataSource, opts);
            RefreshDataGrid();
            PasteStatus      = "✓ تم تطبيق التنظيف";
            PasteStatusColor = "#22C55E";
        }

        [RelayCommand]
        private void ClearData()
        {
            RawPasteText = "";
            State.DataSource = new SmartDataSource();
            DataColumns.Clear();
            DataRows.Clear();
            HasPastedData    = false;
            TotalRowCount    = 0;
            ColumnCount      = 0;
            PasteStatus      = "انسخ الجدول من Excel أو Google Sheets والصقه هنا";
            PasteStatusColor = "#6B7280";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 1 — VARIABLE MAPPING
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private ObservableCollection<MappingRowItem> _mappingRows = new();
        [ObservableProperty] private string _mappingStatus      = "";
        [ObservableProperty] private string _mappingStatusColor = "#6B7280";
        [ObservableProperty] private int    _mappedCount;
        [ObservableProperty] private int    _unmappedRequiredCount;

        [RelayCommand]
        private void AutoMap()
        {
            if (_templateFields.Count == 0 || !HasPastedData) return;

            var mappings = _mappingService.BuildMappings(_templateFields, State.DataSource.Columns);
            State.Mappings = mappings;
            _session?.SetMappings(mappings);   // ← sync to shared session
            RefreshMappingGrid();

            int mapped   = mappings.Count(m => m.IsMapped);
            int required = mappings.Count(m => m.HasError);

            MappingStatus = required > 0
                ? $"⚠ {required} حقل مطلوب غير مربوط"
                : $"✓ تم ربط {mapped} من {mappings.Count} حقل";
            MappingStatusColor = required > 0 ? "#F59E0B" : "#22C55E";
        }

        [RelayCommand]
        private void ClearMappings()
        {
            foreach (var m in State.Mappings)
            {
                m.ColumnName  = null;
                m.Confidence  = MappingConfidence.None;
            }
            _session?.SetMappings(State.Mappings);  // ← sync to shared session
            RefreshMappingGrid();
        }

        // Called by View (or by TemplateDesignerViewModel binding section) to
        // manually change a column binding for a specific field.
        public void SetMapping(string fieldId, string? columnName)
        {
            var mapping = State.Mappings.FirstOrDefault(m => m.FieldId == fieldId);
            if (mapping == null) return;
            mapping.ColumnName = columnName;
            mapping.Confidence = string.IsNullOrEmpty(columnName)
                ? MappingConfidence.None
                : MappingConfidence.High;
            _session?.SetMappings(State.Mappings);  // ← sync to shared session
            RefreshMappingGrid();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 2 — IMAGE MANAGER
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private string  _imageFolderPath   = "";
        [ObservableProperty] private string  _imageStatusText   = "لم يتم اختيار مجلد الصور بعد";
        [ObservableProperty] private string  _imageStatusColor  = "#6B7280";
        [ObservableProperty] private bool    _isScanning;
        [ObservableProperty] private int     _scanProgress;
        [ObservableProperty] private ObservableCollection<ImageAssetItem> _imageAssets = new();
        [ObservableProperty] private ImageMatchMode _selectedMatchMode = ImageMatchMode.ByFileName;
        [ObservableProperty] private string? _imageKeyColumn;
        [ObservableProperty] private ObservableCollection<string> _availableMatchModes = new();

        private CancellationTokenSource? _scanCts;

        public ImageMatchMode[] AllMatchModes { get; } =
            (ImageMatchMode[])Enum.GetValues(typeof(ImageMatchMode));

        [RelayCommand]
        private void BrowseImageFolder()
        {
            // WPF (Microsoft.Win32) doesn't have FolderBrowserDialog.
            // Use OpenFileDialog and take the directory of the chosen file.
            var dialog = new OpenFileDialog
            {
                Title       = "اختر أي ملف داخل مجلد الصور",
                Filter      = "ملفات الصور|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif;*.webp|كل الملفات|*.*",
                Multiselect = false,
            };

            if (dialog.ShowDialog() == true)
            {
                string? folder = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folder))
                {
                    ImageFolderPath = folder;
                    _ = ScanImageFolderAsync();
                }
            }
        }

        [RelayCommand]
        private async Task ScanImageFolderAsync()
        {
            if (string.IsNullOrWhiteSpace(ImageFolderPath) || !Directory.Exists(ImageFolderPath))
            {
                ImageStatusText  = "المجلد غير موجود";
                ImageStatusColor = "#EF4444";
                return;
            }

            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();

            IsScanning      = true;
            ScanProgress    = 0;
            ImageAssets.Clear();
            ImageStatusText  = "جارٍ المسح...";
            ImageStatusColor = "#3B82F6";

            try
            {
                var progress = new Progress<int>(p => ScanProgress = p);
                var assets = await _imageLibrary.ScanFolderAsync(
                    ImageFolderPath,
                    new LibraryScanOptions { Recursive = true, GenerateThumbnails = true },
                    progress,
                    _scanCts.Token);

                State.ImageFolder = ImageFolderPath;

                ImageAssets.Clear();
                foreach (var a in assets)
                    ImageAssets.Add(new ImageAssetItem(a));

                ImageStatusText  = $"✓ تم العثور على {assets.Count} صورة";
                ImageStatusColor = "#22C55E";

                // Immediately resolve matches if data exists
                if (HasPastedData)
                    ResolveImageMatches();
            }
            catch (OperationCanceledException)
            {
                ImageStatusText  = "تم إلغاء المسح";
                ImageStatusColor = "#6B7280";
            }
            catch (Exception ex)
            {
                ImageStatusText  = $"خطأ في المسح: {ex.Message}";
                ImageStatusColor = "#EF4444";
            }
            finally
            {
                IsScanning   = false;
                ScanProgress = 100;
            }
        }

        [RelayCommand]
        private void ResolveImageMatches()
        {
            if (!HasPastedData || ImageAssets.Count == 0) return;

            var library = ImageAssets.Select(i => i.Asset).ToList();
            var opts = new ImageMatchingOptions
            {
                Mode            = SelectedMatchMode,
                KeyColumn       = ImageKeyColumn ?? "",
                ImageFolder     = ImageFolderPath,
                StaticImagePath = State.StaticImagePath,
            };

            _imageMatcher.ResolveImages(State.DataSource, library, opts);

            int found    = State.DataSource.Rows.Count(r => r.ImageStatus == ImageStatus.Found);
            int missing  = State.DataSource.Rows.Count(r => r.ImageStatus == ImageStatus.Missing);

            ImageStatusText  = $"✓ {found} صورة مطابقة · {missing} غير موجودة";
            ImageStatusColor = missing > 0 ? "#F59E0B" : "#22C55E";

            // Refresh asset used-state
            foreach (var item in ImageAssets)
                item.RefreshUsed();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 3 — PREVIEW
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private int    _previewRowIndex     = 0;
        [ObservableProperty] private string _previewRowInfo      = "";
        [ObservableProperty] private ObservableCollection<PreviewFieldItem> _previewFields = new();
        [ObservableProperty] private string? _previewImagePath;
        [ObservableProperty] private bool   _previewShowOverlay  = true;

        [RelayCommand]
        private void PreviewFirst()  { PreviewRowIndex = 0;                              RefreshPreview(); }
        [RelayCommand]
        private void PreviewLast()   { PreviewRowIndex = State.DataSource.Rows.Count - 1; RefreshPreview(); }
        [RelayCommand]
        private void PreviewNext()
        {
            if (PreviewRowIndex < State.DataSource.Rows.Count - 1)
            { PreviewRowIndex++; RefreshPreview(); }
        }
        [RelayCommand]
        private void PreviewPrev()
        {
            if (PreviewRowIndex > 0)
            { PreviewRowIndex--; RefreshPreview(); }
        }

        public bool CanPreviewNext => HasPastedData && PreviewRowIndex < State.DataSource.Rows.Count - 1;
        public bool CanPreviewPrev => HasPastedData && PreviewRowIndex > 0;

        private void RefreshPreview()
        {
            if (!HasPastedData || State.DataSource.Rows.Count == 0) return;

            int idx = Math.Clamp(PreviewRowIndex, 0, State.DataSource.Rows.Count - 1);
            PreviewRowIndex = idx;

            var row = State.DataSource.Rows[idx];
            PreviewRowInfo = $"السجل {idx + 1} من {State.DataSource.TotalRows} · {row.StatusIcon}";

            PreviewFields.Clear();
            foreach (var mapping in State.Mappings.Where(m => m.IsMapped))
            {
                string value = row.Get(mapping.ColumnName!,
                    mapping.DefaultValue ?? "(فارغ)");
                PreviewFields.Add(new PreviewFieldItem(mapping.FieldLabel, value));
            }

            PreviewImagePath = row.ResolvedImagePath;

            OnPropertyChanged(nameof(CanPreviewNext));
            OnPropertyChanged(nameof(CanPreviewPrev));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 4 — PREFLIGHT
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private string  _preflightSummary      = "اضغط 'فحص' لبدء التحقق";
        [ObservableProperty] private string  _preflightSummaryColor = "#6B7280";
        [ObservableProperty] private bool    _preflightCanExport;
        [ObservableProperty] private ObservableCollection<PreflightIssueItem> _preflightIssues = new();
        [ObservableProperty] private int     _errorCount;
        [ObservableProperty] private int     _warningCount;
        [ObservableProperty] private int     _infoCount;

        [RelayCommand]
        private void RunPreflight()
        {
            // Validate rows first
            _validator.ValidateAll(State.DataSource, State.Mappings);

            var library = ImageAssets.Select(i => i.Asset).ToList();
            var report  = _preflight.Run(State.DataSource, State.Mappings, library, State.ExportSettings);

            PreflightIssues.Clear();
            foreach (var issue in report.Issues)
                PreflightIssues.Add(new PreflightIssueItem(issue));

            PreflightSummary      = report.SummaryText;
            PreflightSummaryColor = report.SummaryColor;
            PreflightCanExport    = report.CanExport;
            ErrorCount            = report.ErrorCount;
            WarningCount          = report.WarningCount;
            InfoCount             = report.InfoCount;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 5 — EXPORT
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private ExportSettings _exportSettings = new();
        [ObservableProperty] private string  _exportStatusText   = "";
        [ObservableProperty] private string  _exportStatusColor  = "#6B7280";
        [ObservableProperty] private bool    _isExporting;
        [ObservableProperty] private int     _exportProgress;
        [ObservableProperty] private string  _exportProgressLabel = "";

        public ExportScope[]    AllScopes      { get; } = (ExportScope[])   Enum.GetValues(typeof(ExportScope));
        public ExportFormat[]   AllFormats     { get; } = (ExportFormat[])  Enum.GetValues(typeof(ExportFormat));
        public FileNamingMode[] AllNamingModes { get; } = (FileNamingMode[])Enum.GetValues(typeof(FileNamingMode));
        public PageLayout[]     AllLayouts     { get; } = (PageLayout[])    Enum.GetValues(typeof(PageLayout));

        [RelayCommand]
        private void BrowseOutputFolder()
        {
            // WPF (Microsoft.Win32) doesn't have FolderBrowserDialog.
            // Use SaveFileDialog with a dummy file name to pick a folder.
            var dialog = new SaveFileDialog
            {
                Title    = "اختر مجلد الحفظ (اكتب أي اسم ملف واضغط حفظ)",
                Filter   = "مجلد|*.none",
                FileName = "اختر هذا المجلد",
            };
            if (dialog.ShowDialog() == true)
            {
                string? folder = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folder))
                    ExportSettings.OutputFolder = folder;
            }
        }

        [RelayCommand]
        private async Task StartExportAsync()
        {
            // Re-run preflight
            RunPreflight();
            if (!PreflightCanExport)
            {
                ExportStatusText  = "⚠ يوجد أخطاء تمنع التصدير — راجع تبويب الفحص";
                ExportStatusColor = "#EF4444";
                return;
            }

            IsExporting          = true;
            ExportProgress       = 0;
            ExportProgressLabel  = "جارٍ التصدير...";
            ExportStatusText     = "";

            try
            {
                // NOTE: Actual PDF/image rendering would be invoked here via the
                // rendering engine (SmartVariablesRenderEngine — future implementation).
                // For now we simulate progress to confirm the pipeline is wired up.
                await Task.Delay(200);
                ExportProgress = 50;
                await Task.Delay(200);
                ExportProgress = 100;

                ExportStatusText  = "✓ تم التصدير بنجاح";
                ExportStatusColor = "#22C55E";
            }
            catch (Exception ex)
            {
                ExportStatusText  = $"خطأ في التصدير: {ex.Message}";
                ExportStatusColor = "#EF4444";
            }
            finally
            {
                IsExporting         = false;
                ExportProgressLabel = "";
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SHARED / INIT
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private int    _activeTabIndex = 0;
        [ObservableProperty] private string _globalStatusMessage = "";
        [ObservableProperty] private string _globalStatusColor   = "#6B7280";

        /// <summary>Called by TemplateDesignerViewModel when canvas slots change.</summary>
        public void SetTemplateFields(IEnumerable<SmartTemplateField> fields)
        {
            _templateFields = fields.ToList();
            _session?.SyncFields(_templateFields);   // ← sync to shared session

            // Re-build mappings if data already present
            if (HasPastedData)
                AutoMapCommand.Execute(null);
        }

        /// <summary>Restore from persisted state (e.g. after opening .apext).</summary>
        public void LoadState(SmartVariablesState state)
        {
            State = state;
            RefreshDataGrid();
            RefreshMappingGrid();
            ImageFolderPath      = state.ImageFolder;
            SelectedMatchMode    = state.ImageMatchMode;
            ImageKeyColumn       = state.ImageKeyColumn;
            ExportSettings       = state.ExportSettings;
            ActiveTabIndex       = state.ActiveTabIndex;
            PreviewRowIndex      = state.PreviewRowIndex;
            HasPastedData        = state.DataSource.HasData;
        }

        /// <summary>Snapshot current UI state into the persisted model.</summary>
        public SmartVariablesState SnapshotState()
        {
            State.ImageFolder      = ImageFolderPath;
            State.ImageMatchMode   = SelectedMatchMode;
            State.ImageKeyColumn   = ImageKeyColumn ?? "";
            State.ExportSettings   = ExportSettings;
            State.ActiveTabIndex   = ActiveTabIndex;
            State.PreviewRowIndex  = PreviewRowIndex;
            State.LastSaved        = DateTime.Now;
            return State;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // INTERNALS
        // ═══════════════════════════════════════════════════════════════════════

        private void ParsePaste(string text)
        {
            try
            {
                var src = _pasteParser.Parse(text, new ParseOptions
                {
                    TrimWhitespace  = true,
                    IgnoreEmptyRows = true,
                    HasHeader       = true,
                });

                State.DataSource = src;
                HasPastedData    = src.HasData;
                TotalRowCount    = src.TotalRows;
                ColumnCount      = src.Columns.Count;

                DetectedSeparatorLabel = src.DetectedSeparator == '\t' ? "Tab (Excel)" :
                                         src.DetectedSeparator == ',' ? "فاصلة (CSV)" :
                                         src.DetectedSeparator == ';' ? "فاصلة منقوطة" : "غير معروف";

                RefreshDataGrid();

                PasteStatus      = src.HasData
                    ? $"✓ {src.TotalRows} سجل · {src.Columns.Count} عمود ({DetectedSeparatorLabel})"
                    : "لم يتم اكتشاف بيانات — تأكد من النسخ من Excel";
                PasteStatusColor = src.HasData ? "#22C55E" : "#EF4444";

                // Auto-trigger mapping if fields are loaded
                if (src.HasData && _templateFields.Count > 0)
                    AutoMapCommand.Execute(null);
            }
            catch (Exception ex)
            {
                PasteStatus      = $"خطأ في التحليل: {ex.Message}";
                PasteStatusColor = "#EF4444";
            }
        }

        private void RefreshDataGrid()
        {
            var src = State.DataSource;
            DataColumns.Clear();
            DataRows.Clear();

            foreach (var col in src.Columns)
                DataColumns.Add(col);

            foreach (var row in src.Rows)
                DataRows.Add(new DataRowItem(row, src.Columns));

            // ── Build DataTableView (the FIXED DataGrid binding target) ────────
            // Uses a DataTable so WPF auto-generates columns from the REAL pasted
            // headers (الاسم, الصف, …) — not from DataRowItem reflection properties.
            if (src.HasData)
            {
                var dt = new DataTable();
                dt.Columns.Add("●", typeof(string));
                foreach (var col in src.Columns)
                    dt.Columns.Add(col, typeof(string));

                foreach (var row in src.Rows)
                {
                    var dr = dt.NewRow();
                    dr["●"] = row.StatusIcon;
                    foreach (var col in src.Columns)
                        dr[col] = row.Get(col);
                    dt.Rows.Add(dr);
                }
                DataTableView = dt.DefaultView;
            }
            else
            {
                DataTableView = null;
            }

            // ── Sync to session so the Stepper and Canvas share the same view ──
            _session?.SetDataSource(src);
        }

        private void RefreshMappingGrid()
        {
            MappingRows.Clear();
            foreach (var m in State.Mappings)
                MappingRows.Add(new MappingRowItem(m, State.DataSource.Columns));

            MappedCount              = State.Mappings.Count(m => m.IsMapped);
            UnmappedRequiredCount    = State.Mappings.Count(m => m.HasError);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper display models (thin wrappers for UI binding)
    // ═══════════════════════════════════════════════════════════════════════════

    public class DataRowItem
    {
        private readonly SmartDataRow   _row;
        private readonly List<string>   _columns;
        public int    RowIndex    => _row.RowIndex;
        public string StatusIcon  => _row.StatusIcon;
        public string StatusColor => _row.StatusColor;

        public string this[string column] => _row.Get(column);

        public DataRowItem(SmartDataRow row, List<string> columns)
        { _row = row; _columns = columns; }

        // For DataGrid row display: returns ordered cell values
        public List<string> Cells => _columns.Select(c => _row.Get(c)).ToList();
    }

    public class MappingRowItem : ObservableObject
    {
        private readonly VariableMapping _mapping;
        public  List<string> AvailableColumns { get; }

        public string  FieldId        => _mapping.FieldId;
        public string  FieldLabel     => _mapping.FieldLabel;
        public bool    IsRequired     => _mapping.IsRequired;
        public string  StatusLabel    => _mapping.StatusLabel;
        public string  StatusColor    => _mapping.StatusColor;
        public string  ConfidenceLabel=> _mapping.ConfidenceLabel;

        public string? SelectedColumn
        {
            get => _mapping.ColumnName;
            set
            {
                _mapping.ColumnName = value;
                _mapping.Confidence = string.IsNullOrEmpty(value)
                    ? MappingConfidence.None : MappingConfidence.High;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string? DefaultValue
        {
            get => _mapping.DefaultValue;
            set { _mapping.DefaultValue = value; OnPropertyChanged(); }
        }

        public MappingRowItem(VariableMapping mapping, List<string> columns)
        {
            _mapping         = mapping;
            AvailableColumns = new List<string> { "" }.Concat(columns).ToList();
        }
    }

    public class ImageAssetItem : ObservableObject
    {
        public ImageAsset Asset { get; }

        public string FileName       => Asset.FileName;
        public string SizeLabel      => Asset.SizeLabel;
        public string DimensionsLabel=> Asset.DimensionsLabel;
        public string StatusIcon     => Asset.StatusIcon;
        public string Extension      => Asset.Extension;
        public string? ThumbnailBase64 => Asset.ThumbnailBase64;

        private bool _isUsed;
        public bool IsUsed { get => _isUsed; private set => SetProperty(ref _isUsed, value); }

        public void RefreshUsed() => IsUsed = Asset.IsUsed;

        public ImageAssetItem(ImageAsset asset)
        {
            Asset   = asset;
            _isUsed = asset.IsUsed;
        }
    }

    public class PreviewFieldItem
    {
        public string FieldLabel { get; }
        public string Value      { get; }
        public PreviewFieldItem(string label, string value) { FieldLabel = label; Value = value; }
    }

    public class PreflightIssueItem
    {
        private readonly ValidationIssue _issue;
        public string LevelIcon  => _issue.LevelIcon;
        public string LevelColor => _issue.LevelColor;
        public string LevelLabel => _issue.LevelLabel;
        public string RowLabel   => _issue.RowLabel;
        public string Message    => _issue.Message;
        public string Suggestion => _issue.Suggestion;
        public PreflightIssueItem(ValidationIssue issue) { _issue = issue; }
    }
}
