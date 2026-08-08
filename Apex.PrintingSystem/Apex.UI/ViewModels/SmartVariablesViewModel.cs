using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Services.SmartVariables;
using Apex.Services.SmartVariables.Models;
using Apex.Services.Templates;
using Apex.UI.Services;
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
    public partial class SmartVariablesViewModel : ViewModelBase
    {
        // ── Services ───────────────────────────────────────────────────────────
        private readonly ISmartPasteParser _pasteParser = new SmartPasteParser();
        private readonly IDataCleaningService _cleaner = new DataCleaningService();
        private readonly IVariableMappingService _mappingService = new VariableMappingService();
        private readonly IImageLibraryService _imageLibrary = new ImageLibraryService();
        private readonly IImageMatchingService _imageMatcher = new ImageMatchingService();
        private readonly IDataValidationService _validator = new DataValidationService();
        private readonly IPreflightService _preflight = new PreflightService();
        private readonly ITemplateRenderingService _renderingService = new TemplateRenderingService();
        private readonly VectorPdfExporter _vectorPdf = new();

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

        [ObservableProperty] private string _rawPasteText = "";
        [ObservableProperty] private string _pasteStatus = L("SV_PastePrompt");
        [ObservableProperty] private string _pasteStatusColor = "#6B7280";
        [ObservableProperty] private bool _hasPastedData;
        [ObservableProperty] private int _totalRowCount;
        [ObservableProperty] private int _columnCount;
        [ObservableProperty] private string _detectedSeparatorLabel = "";

        // Data grid — DataTableView is the FIXED binding target for the DataGrid.
        // It is a DataView built from real column names (الاسم, الصف, …), NOT from
        // DataRowItem reflection properties (Cells, StatusColor, etc.).
        [ObservableProperty] private DataView? _dataTableView;

        // Legacy collections kept for other consumers (MappingRows, etc.)
        [ObservableProperty] private ObservableCollection<string> _dataColumns = new();
        [ObservableProperty] private ObservableCollection<DataRowItem> _dataRows = new();

        // Cleaning options
        [ObservableProperty] private bool _cleanTrimWhitespace = true;
        [ObservableProperty] private bool _cleanNormalizeArabic = true;
        [ObservableProperty] private bool _cleanNormalizeNumbers = true;
        [ObservableProperty] private bool _cleanRemoveDiacritics;
        [ObservableProperty] private bool _cleanRemoveExtraSpaces = true;

        /// <summary>
        /// Loads a built-in Arabic sample dataset (3 rows × 5 columns) so the
        /// user can immediately explore the workflow without needing real data.
        /// </summary>
        [RelayCommand]
        private void LoadSampleData()
        {
            // Tab-separated demo data. Kept inline (NOT localized): the \t / \r\n
            // structure is what the parser depends on, so it must not be editable
            // by translators or resolved at runtime.
            const string sample =
                "الاسم\tالصف\tرقم الجلوس\tالصورة\tالكود\r\n" +
                "أحمد محمد علي\t3أ\t001\tأحمد.jpg\t00125\r\n" +
                "فاطمة إبراهيم\t3ب\t002\tفاطمة.jpg\t00126\r\n" +
                "محمد عبدالله\t4أ\t003\tمحمد.jpg\t00127";

            RawPasteText = sample;
            // OnRawPasteTextChanged → ParsePaste is called automatically
        }

        [RelayCommand]
        private void PasteFromClipboard()
        {
            try
            {
                string text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    PasteStatus = L("SV_ClipboardEmpty");
                    PasteStatusColor = "#EF4444";
                    return;
                }
                RawPasteText = text;
                ParsePaste(text);
            }
            catch (Exception ex)
            {
                PasteStatus = Lf("Num_ErrorColon", ex.Message);
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
                TrimWhitespace = CleanTrimWhitespace,
                NormalizeArabicLetters = CleanNormalizeArabic,
                NormalizeArabicNumbers = CleanNormalizeNumbers,
                RemoveDiacritics = CleanRemoveDiacritics,
                RemoveExtraSpaces = CleanRemoveExtraSpaces,
            };

            _cleaner.Clean(State.DataSource, opts);
            RefreshDataGrid();
            PasteStatus = L("SV_CleanupApplied");
            PasteStatusColor = "#22C55E";
        }

        [RelayCommand]
        private void ClearData()
        {
            RawPasteText = "";
            State.DataSource = new SmartDataSource();
            // The session holds the copy everything else reads. Clearing only the
            // local copy left the canvas and the preview showing data that no longer
            // exists here, and mappings pointing at columns that were gone.
            _session?.SetDataSource(State.DataSource);
            DataColumns.Clear();
            DataRows.Clear();
            HasPastedData = false;
            TotalRowCount = 0;
            ColumnCount = 0;
            PasteStatus = L("SV_PastePrompt");
            PasteStatusColor = "#6B7280";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 1 — VARIABLE MAPPING
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private ObservableCollection<MappingRowItem> _mappingRows = new();
        [ObservableProperty] private string _mappingStatus = "";
        [ObservableProperty] private string _mappingStatusColor = "#6B7280";
        [ObservableProperty] private int _mappedCount;
        [ObservableProperty] private int _unmappedRequiredCount;

        /// <summary>True when at least one field is mapped to a data column.</summary>
        public bool HasMappings => MappedCount > 0;
        partial void OnMappedCountChanged(int value)
        {
            OnPropertyChanged(nameof(HasMappings));
            // When mappings first become available, seed the preview canvas
            // so it is ready before the user clicks the Preview tab.
            if (value > 0 && HasPastedData)
                RefreshPreview();
        }

        [RelayCommand]
        private void AutoMap()
        {
            if (_templateFields.Count == 0 || !HasPastedData) return;

            var mappings = _mappingService.BuildMappings(_templateFields, State.DataSource.Columns);

            // Never overwrite a column the operator chose by hand.
            //
            // Auto-map replaced the whole list, and it is re-run automatically every
            // time the fields are synced — which happens on every step change AND
            // every tab switch. So a field bound manually on the canvas was silently
            // reverted the moment the user navigated anywhere: the binding "did not
            // work" even though it had been made correctly.
            // Starting over is still available through Clear mappings.
            foreach (var m in mappings)
            {
                var manual = State.Mappings.FirstOrDefault(
                    existing => existing.FieldId == m.FieldId && !string.IsNullOrEmpty(existing.ColumnName));

                if (manual == null) continue;

                // Only keep it if that column still exists in the current data.
                if (State.DataSource.Columns.Contains(manual.ColumnName!, StringComparer.OrdinalIgnoreCase))
                {
                    m.ColumnName = manual.ColumnName;
                    m.Confidence = manual.Confidence == MappingConfidence.None
                        ? MappingConfidence.High
                        : manual.Confidence;
                }
            }

            State.Mappings = mappings;
            _session?.SetMappings(mappings);   // ← sync to shared session
            RefreshMappingGrid();

            int mapped = mappings.Count(m => m.IsMapped);
            int required = mappings.Count(m => m.HasError);

            MappingStatus = required > 0
                ? Lf("SV_RequiredUnmapped", required)
                : Lf("SV_MappedCount", mapped, mappings.Count);
            MappingStatusColor = required > 0 ? "#F59E0B" : "#22C55E";

            // If the user is already viewing the preview tab, refresh it now
            if (ActiveTabIndex == 3)
                RefreshPreview();
        }

        [RelayCommand]
        private void ClearMappings()
        {
            foreach (var m in State.Mappings)
            {
                m.ColumnName = null;
                m.Confidence = MappingConfidence.None;
            }
            _session?.SetMappings(State.Mappings);  // ← sync to shared session
            RefreshMappingGrid();
        }

        // Called by View (or by TemplateDesignerViewModel binding section) to
        // manually change a column binding for a specific field.
        public void SetMapping(string fieldId, string? columnName)
        {
            var mapping = State.Mappings.FirstOrDefault(m => m.FieldId == fieldId);

            // If no mapping exists yet for this field (e.g. a slot added in the
            // designer after auto-map ran), create one so the binding is not lost
            // and stays consistent between State, Session and the mapping grid.
            if (mapping == null)
            {
                var field = _templateFields.FirstOrDefault(f => f.Id == fieldId);
                mapping = new VariableMapping
                {
                    FieldId = fieldId,
                    FieldLabel = field?.Label ?? "",
                    FieldType = field?.FieldType ?? SmartFieldType.TextVariable,
                    IsRequired = field?.IsRequired ?? false,
                };
                State.Mappings.Add(mapping);
            }

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

        [ObservableProperty] private string _imageFolderPath = "";
        partial void OnImageFolderPathChanged(string value)
        {
            // Keep the session in sync so the rendering service always reads the
            // latest folder even if it was set after the initial SyncRenderingContext.
            if (_session != null)
                _session.ImageFolder = value;
        }
        [ObservableProperty] private string _imageStatusText = L("SV_NoImageFolder");
        [ObservableProperty] private string _imageStatusColor = "#6B7280";
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private int _scanProgress;
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
                Title = L("SV_ChooseAnyFile"),
                Filter = L("SV_ImageFilter"),
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
                ImageStatusText = L("SV_FolderMissing");
                ImageStatusColor = "#EF4444";
                return;
            }

            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();

            IsScanning = true;
            ScanProgress = 0;
            ImageAssets.Clear();
            ImageStatusText = L("SV_Scanning");
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

                ImageStatusText = Lf("SV_FoundImages", assets.Count);
                ImageStatusColor = "#22C55E";

                // Immediately resolve matches if data exists
                if (HasPastedData)
                    ResolveImageMatches();
            }
            catch (OperationCanceledException)
            {
                ImageStatusText = L("SV_ScanCancelled");
                ImageStatusColor = "#6B7280";
            }
            catch (Exception ex)
            {
                ImageStatusText = Lf("SV_ScanError", ex.Message);
                ImageStatusColor = "#EF4444";
            }
            finally
            {
                IsScanning = false;
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
                Mode = SelectedMatchMode,
                KeyColumn = ImageKeyColumn ?? "",
                ImageFolder = ImageFolderPath,
                StaticImagePath = State.StaticImagePath,
            };

            _imageMatcher.ResolveImages(State.DataSource, library, opts);

            int found = State.DataSource.Rows.Count(r => r.ImageStatus == ImageStatus.Found);
            int missing = State.DataSource.Rows.Count(r => r.ImageStatus == ImageStatus.Missing);

            ImageStatusText = Lf("SV_MatchSummary", found, missing);
            ImageStatusColor = missing > 0 ? "#F59E0B" : "#22C55E";

            // Refresh asset used-state
            foreach (var item in ImageAssets)
                item.RefreshUsed();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 3 — PREVIEW
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private int _previewRowIndex = 0;
        [ObservableProperty] private string _previewRowInfo = "";
        [ObservableProperty] private ObservableCollection<PreviewFieldItem> _previewFields = new();
        [ObservableProperty] private string? _previewImagePath;

        /// <summary>
        /// The live-rendered template for the current record.
        /// Produced by <see cref="ITemplateRenderingService"/> and bound to the
        /// template canvas in the Preview tab right panel.
        /// </summary>
        [ObservableProperty] private RenderedTemplate? _currentRenderedTemplate;

        /// <summary>Status message shown in the Preview toolbar (export result / error).</summary>
        [ObservableProperty] private string _exportPreviewStatus = "";
        [ObservableProperty] private string _exportPreviewStatusColor = "#6B7280";

        // ── Navigation commands ───────────────────────────────────────────────

        [RelayCommand]
        private void PreviewFirst()
        {
            PreviewRowIndex = 0;
            RefreshPreview();
        }

        [RelayCommand]
        private void PreviewLast()
        {
            PreviewRowIndex = State.DataSource.Rows.Count - 1;
            RefreshPreview();
        }

        [RelayCommand]
        private void PreviewNext()
        {
            if (PreviewRowIndex < State.DataSource.Rows.Count - 1)
            {
                PreviewRowIndex++;
                RefreshPreview();
            }
        }

        [RelayCommand]
        private void PreviewPrev()
        {
            if (PreviewRowIndex > 0)
            {
                PreviewRowIndex--;
                RefreshPreview();
            }
        }

        public bool CanPreviewNext => HasPastedData && PreviewRowIndex < State.DataSource.Rows.Count - 1;
        public bool CanPreviewPrev => HasPastedData && PreviewRowIndex > 0;

        /// <summary>
        /// Public entry point for TemplateDesignerViewModel to force a preview
        /// refresh after syncing the rendering context (v2.0.9).
        /// Safe to call even when not on the preview tab — it checks internally.
        /// </summary>
        public void ForceRefreshPreview() => RefreshPreview();

        /// <summary>
        /// Render the current record onto the template canvas.
        ///
        /// Two outputs are produced together so they always stay in sync:
        /// 1. <see cref="CurrentRenderedTemplate"/> — drives the live canvas (right panel).
        /// 2. <see cref="PreviewFields"/>            — drives the field-value list (left panel).
        /// </summary>
        private void RefreshPreview()
        {
            if (!HasPastedData || State.DataSource.Rows.Count == 0) return;

            int idx = Math.Clamp(PreviewRowIndex, 0, State.DataSource.Rows.Count - 1);
            PreviewRowIndex = idx;

            var row = State.DataSource.Rows[idx];
            PreviewRowInfo = Lf("SV_RecordOf", idx + 1, State.DataSource.TotalRows, row.StatusIcon);

            // ── 1. Live template canvas rendering ──────────────────────────
            // Render whenever CurrentPage is available — even with zero mappings,
            // unmapped fields will show [غير مربوط] via FieldRenderError.NotMapped.
            if (_session?.CurrentPage != null)
            {
                CurrentRenderedTemplate = _renderingService.Render(
                    _session.CurrentPage,
                    State.DataSource,
                    State.Mappings,
                    idx,
                    _session.Assets,
                    ImageFolderPath);
            }
            else
            {
                // CurrentPage not synced yet — will be populated on next SyncFieldsToSession()
                // which fires whenever the user switches tabs or steps.
                CurrentRenderedTemplate = null;
            }

            // ── 2. Field-value list (left panel) ───────────────────────────
            PreviewFields.Clear();
            foreach (var mapping in State.Mappings.Where(m => m.IsMapped))
            {
                string value = row.Get(mapping.ColumnName!, mapping.DefaultValue ?? L("SV_Empty"));
                PreviewFields.Add(new PreviewFieldItem(mapping.FieldLabel, value));
            }

            PreviewImagePath = row.ResolvedImagePath;

            OnPropertyChanged(nameof(CanPreviewNext));
            OnPropertyChanged(nameof(CanPreviewPrev));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 4 — PREFLIGHT
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private string _preflightSummary = L("SV_PressCheck");
        [ObservableProperty] private string _preflightSummaryColor = "#6B7280";
        [ObservableProperty] private bool _preflightCanExport;
        [ObservableProperty] private ObservableCollection<PreflightIssueItem> _preflightIssues = new();
        [ObservableProperty] private int _errorCount;
        [ObservableProperty] private int _warningCount;
        [ObservableProperty] private int _infoCount;

        [RelayCommand]
        private void RunPreflight()
        {
            // Validate rows first
            _validator.ValidateAll(State.DataSource, State.Mappings);

            var library = ImageAssets.Select(i => i.Asset).ToList();
            var report = _preflight.Run(State.DataSource, State.Mappings, library, State.ExportSettings);

            PreflightIssues.Clear();
            foreach (var issue in report.Issues)
                PreflightIssues.Add(new PreflightIssueItem(issue));

            PreflightSummary = report.SummaryText;
            PreflightSummaryColor = report.SummaryColor;
            PreflightCanExport = report.CanExport;
            ErrorCount = report.ErrorCount;
            WarningCount = report.WarningCount;
            InfoCount = report.InfoCount;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 5 — EXPORT
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private ExportSettings _exportSettings = new();
        [ObservableProperty] private string _exportStatusText = "";
        [ObservableProperty] private string _exportStatusColor = "#6B7280";
        [ObservableProperty] private bool _isExporting;
        [ObservableProperty] private int _exportProgress;
        [ObservableProperty] private string _exportProgressLabel = "";

        public ExportScope[] AllScopes { get; } = (ExportScope[])Enum.GetValues(typeof(ExportScope));
        public ExportFormat[] AllFormats { get; } = (ExportFormat[])Enum.GetValues(typeof(ExportFormat));
        public FileNamingMode[] AllNamingModes { get; } = (FileNamingMode[])Enum.GetValues(typeof(FileNamingMode));
        public PageLayout[] AllLayouts { get; } = (PageLayout[])Enum.GetValues(typeof(PageLayout));

        // ── Simplified export commands (used by the simplified Export tab) ──────

        /// <summary>
        /// Export only the currently-previewed record to PNG.
        /// Uses the off-screen DrawingVisual renderer — no UI element required.
        /// Shows a SaveFileDialog to let the user choose the file path.
        /// </summary>
        [RelayCommand]
        private void ExportCurrentRecordPng()
        {
            if (!HasPastedData || State.DataSource.Rows.Count == 0)
            {
                ExportStatusText = L("SV_NoData");
                ExportStatusColor = "#EF4444";
                return;
            }
            if (_session?.CurrentPage == null)
            {
                ExportStatusText = L("SV_NoTemplate");
                ExportStatusColor = "#F59E0B";
                return;
            }

            int idx = Math.Clamp(PreviewRowIndex, 0, State.DataSource.Rows.Count - 1);
            string safe = System.Text.RegularExpressions.Regex.Replace(
                _session.CurrentPage.Id ?? "record", @"[\\/:*?""<>|]", "_");

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = L("SV_SaveRecordPng"),
                Filter = L("SV_PngFilter"),
                FileName = $"{(idx + 1):D4}_{safe}",
                AddExtension = true,
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var rendered = _renderingService.Render(
                    _session.CurrentPage, State.DataSource, State.Mappings,
                    idx, _session.Assets, ImageFolderPath);

                ExportRenderedTemplateToPng(rendered, dlg.FileName);

                ExportStatusText = Lf("SV_Saved", Path.GetFileName(dlg.FileName));
                ExportStatusColor = "#22C55E";
            }
            catch (Exception ex)
            {
                ExportStatusText = Lf("SV_ExportError", ex.Message);
                ExportStatusColor = "#EF4444";
            }
        }

        /// <summary>
        /// Export ALL records as PNG files.
        /// Simplified version: picks a folder if one is not yet set, then exports
        /// without requiring a separate Preflight pass — only hard errors block it.
        /// </summary>
        [RelayCommand]
        private async Task ExportAllRecordsPngAsync()
        {
            if (!HasPastedData || State.DataSource.Rows.Count == 0)
            {
                ExportStatusText = L("SV_NoDataExport");
                ExportStatusColor = "#EF4444";
                return;
            }
            if (_session?.CurrentPage == null)
            {
                ExportStatusText = L("SV_NoTemplate");
                ExportStatusColor = "#F59E0B";
                return;
            }

            // Pick folder if not already chosen
            if (string.IsNullOrWhiteSpace(ExportSettings.OutputFolder) ||
                !Directory.Exists(ExportSettings.OutputFolder))
            {
                BrowseOutputFolderCommand.Execute(null);
            }
            if (string.IsNullOrWhiteSpace(ExportSettings.OutputFolder) ||
                !Directory.Exists(ExportSettings.OutputFolder))
            {
                ExportStatusText = L("SV_NoSaveFolder");
                ExportStatusColor = "#F59E0B";
                return;
            }

            IsExporting = true;
            ExportProgress = 0;
            ExportProgressLabel = L("SV_Exporting");
            ExportStatusText = "";

            try
            {
                int total = State.DataSource.Rows.Count;
                int ok = 0, failed = 0;
                string templateName = System.Text.RegularExpressions.Regex.Replace(
                    _session.CurrentPage.Id ?? "record", @"[\\/:*?""<>|]", "_");
                string outFolder = ExportSettings.OutputFolder;

                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        try
                        {
                            var rendered = _renderingService.Render(
                                _session.CurrentPage, State.DataSource,
                                State.Mappings, i, _session.Assets, ImageFolderPath);

                            string filePath = Path.Combine(outFolder, $"{(i + 1):D4}_{templateName}.png");
                            ExportRenderedTemplateToPng(rendered, filePath);
                            ok++;
                        }
                        catch { failed++; }

                        int pct = (int)(((double)(i + 1) / total) * 100);
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ExportProgress = pct;
                            ExportProgressLabel = $"{i + 1} / {total}";
                        });
                    }
                });

                ExportStatusText = failed == 0
                    ? Lf("SV_ExportedTo", ok, outFolder)
                    : Lf("SV_ExportPartial", ok, failed);
                ExportStatusColor = failed == 0 ? "#22C55E" : "#F59E0B";
            }
            catch (Exception ex)
            {
                ExportStatusText = Lf("SV_ExportError", ex.Message);
                ExportStatusColor = "#EF4444";
            }
            finally
            {
                IsExporting = false;
                ExportProgressLabel = "";
            }
        }

        [RelayCommand]
        private void BrowseOutputFolder()
        {
            // WPF (Microsoft.Win32) doesn't have FolderBrowserDialog.
            // Use SaveFileDialog with a dummy file name to pick a folder.
            var dialog = new SaveFileDialog
            {
                Title = L("SV_ChooseSaveFolder"),
                Filter = L("SV_FolderFilter"),
                FileName = L("SV_ChooseThisFolder"),
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
                ExportStatusText = L("SV_ErrorsBlock");
                ExportStatusColor = "#EF4444";
                return;
            }

            if (_session?.CurrentPage == null)
            {
                ExportStatusText = L("SV_NoTemplateSave");
                ExportStatusColor = "#F59E0B";
                return;
            }

            if (string.IsNullOrWhiteSpace(ExportSettings.OutputFolder) ||
                !Directory.Exists(ExportSettings.OutputFolder))
            {
                ExportStatusText = L("SV_ChooseSaveFirst");
                ExportStatusColor = "#F59E0B";
                return;
            }

            IsExporting = true;
            ExportProgress = 0;
            ExportProgressLabel = L("SV_Exporting");
            ExportStatusText = "";

            try
            {
                // Export all records as PNG using the rendering service.
                // Each file is named: <index>_<templateName>.png
                int total = State.DataSource.Rows.Count;
                int ok = 0;
                int failed = 0;

                string templateName = System.Text.RegularExpressions.Regex
                    .Replace(_session.CurrentPage.Id ?? "record", @"[\\/:*?""<>|]", "_");

                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        try
                        {
                            var rendered = _renderingService.Render(
                                _session.CurrentPage,
                                State.DataSource,
                                State.Mappings,
                                i,
                                _session.Assets,
                                ImageFolderPath);

                            string fileName = $"{(i + 1):D4}_{templateName}.png";
                            string filePath = Path.Combine(ExportSettings.OutputFolder, fileName);

                            // Render to PNG off the UI thread using a DrawingVisual
                            ExportRenderedTemplateToPng(rendered, filePath, dpi: ExportDpi);
                            ok++;
                        }
                        catch { failed++; }

                        // Report progress back on UI thread
                        int progress = (int)(((double)(i + 1) / total) * 100);
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ExportProgress = progress;
                            ExportProgressLabel = $"{i + 1} / {total}";
                        });
                    }
                });

                ExportStatusText = failed == 0
                    ? Lf("SV_ExportedSuccess", ok, ExportSettings.OutputFolder)
                    : Lf("SV_ExportPartial2", ok, failed);
                ExportStatusColor = failed == 0 ? "#22C55E" : "#F59E0B";
            }
            catch (Exception ex)
            {
                ExportStatusText = Lf("SV_ExportError", ex.Message);
                ExportStatusColor = "#EF4444";
            }
            finally
            {
                IsExporting = false;
                ExportProgressLabel = "";
            }
        }

        // ── Export helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Raster export resolution. 300 DPI is the minimum a commercial printer
        /// accepts; the previous 150 DPI left Arabic glyphs visibly ragged because
        /// their thin joins fall below one device pixel.
        ///
        /// This is still a raster path — the real fix is vector output, which is
        /// tracked separately. Raising the DPI is the interim quality floor.
        /// </summary>
        private const int ExportDpi = 300;

        /// <summary>
        /// Renders a <see cref="RenderedTemplate"/> to a WPF RenderTargetBitmap.
        /// MUST be called on the UI thread (or inside Dispatcher.Invoke).
        /// Shared by both PNG and PDF export paths.
        /// </summary>
        private static System.Windows.Media.Imaging.RenderTargetBitmap RenderToRtb(
            RenderedTemplate rt, int dpi = ExportDpi)
        {
            // The visual is drawn in DIPs (1/96"), which is the unit RenderTargetBitmap
            // scales by dpi/96 — so the exported image comes out at its true physical
            // size. (Drawing in PDF points here would silently shrink output to 72/96.)
            int width = Math.Max(1, (int)Math.Round(Apex.Core.Utilities.UnitConverter.MmToPixels(rt.WidthMm, dpi)));
            int height = Math.Max(1, (int)Math.Round(Apex.Core.Utilities.UnitConverter.MmToPixels(rt.HeightMm, dpi)));

            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Same painter the on-screen preview uses → guaranteed WYSIWYG.
                Apex.UI.Rendering.TemplatePainter.Paint(
                    rt, new Apex.UI.Rendering.WpfRenderTarget(dc));
            }

            // RenderTargetBitmap's dpiX/dpiY already scale the 96-DPI DrawingVisual up
            // to the target pixel size, so render the visual directly. (The previous
            // VisualBrush{Stretch=None} wrapper defaulted to centre-alignment, which
            // offset and clipped the exported content.)
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }

        /// <summary>
        /// Render a <see cref="RenderedTemplate"/> to a PNG file using a WPF
        /// DrawingVisual (off-screen, no UI element required).
        /// Called from a background thread during bulk export.
        /// </summary>
        private static void ExportRenderedTemplateToPng(RenderedTemplate rt, string filePath, int dpi = ExportDpi)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var rtb = RenderToRtb(rt, dpi);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var stream = File.Create(filePath);
                encoder.Save(stream);
            });
        }

        /// <summary>
        /// Export the current record to a single-page PDF.
        /// Renders via WPF DrawingVisual → PNG bytes → SkiaSharp PDF page.
        /// </summary>
        [RelayCommand]
        private void ExportCurrentRecordPdf()
        {
            if (!HasPastedData || State.DataSource.Rows.Count == 0)
            {
                ExportStatusText = L("SV_NoData");
                ExportStatusColor = "#EF4444";
                return;
            }
            if (_session?.CurrentPage == null)
            {
                ExportStatusText = L("SV_NoTemplate");
                ExportStatusColor = "#F59E0B";
                return;
            }

            int idx = Math.Clamp(PreviewRowIndex, 0, State.DataSource.Rows.Count - 1);
            string safe = System.Text.RegularExpressions.Regex.Replace(
                _session.CurrentPage.Id ?? "record", @"[\\/:*?""<>|]", "_");

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = L("SV_SaveRecordPdf"),
                Filter = L("Msg_PdfFilter"),
                FileName = $"{(idx + 1):D4}_{safe}",
                AddExtension = true,
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var rt = _renderingService.Render(
                    _session.CurrentPage, State.DataSource, State.Mappings,
                    idx, _session.Assets, ImageFolderPath);

                // TRUE VECTOR output — text as outlines, not a rasterised bitmap.
                _vectorPdf.ExportSingle(rt, dlg.FileName);

                ExportStatusText = Lf("SV_Saved", Path.GetFileName(dlg.FileName));
                ExportStatusColor = "#22C55E";
            }
            catch (Exception ex)
            {
                ExportStatusText = Lf("SV_PdfExportError", ex.Message);
                ExportStatusColor = "#EF4444";
            }
        }

        /// <summary>
        /// Export ALL records to a single multi-page PDF.
        /// Each record → one page. SkiaSharp is used for PDF creation;
        /// WPF DrawingVisual handles the per-record rendering.
        /// </summary>
        [RelayCommand]
        private async Task ExportAllRecordsPdfAsync()
        {
            if (!HasPastedData || State.DataSource.Rows.Count == 0)
            {
                ExportStatusText = L("SV_NoDataExport2");
                ExportStatusColor = "#EF4444";
                return;
            }
            if (_session?.CurrentPage == null)
            {
                ExportStatusText = L("SV_NoTemplateShort");
                ExportStatusColor = "#F59E0B";
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = L("SV_SaveAllPdf"),
                Filter = L("Msg_PdfFilter"),
                FileName = $"all_{State.DataSource.TotalRows}_records",
                AddExtension = true,
            };
            if (dlg.ShowDialog() != true) return;

            IsExporting = true;
            ExportProgress = 0;
            ExportProgressLabel = L("SV_PreparingPdf");
            ExportStatusText = "";

            try
            {
                int total = State.DataSource.Rows.Count;
                var page = _session.CurrentPage;
                var mappings = State.Mappings;
                var assets = _session.Assets;
                var folder = ImageFolderPath;
                var renderer = _renderingService;
                string outPath = dlg.FileName;

                // Text outlines come from WPF, which is UI-thread affine, so the whole
                // vector export runs on the Dispatcher and yields between records to
                // keep the window responsive.
                await Task.Run(() =>
                {
                    var rendered = new List<RenderedTemplate>(total);

                    for (int i = 0; i < total; i++)
                    {
                        try
                        {
                            rendered.Add(renderer.Render(page, State.DataSource, mappings,
                                                         i, assets, folder));
                        }
                        catch { /* skip failed record, continue */ }

                        int pct = (int)(((double)(i + 1) / total) * 100);
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ExportProgress = pct;
                            ExportProgressLabel = $"{i + 1} / {total}";
                        });
                    }

                    // Emitting glyph outlines uses WPF text layout → UI thread.
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ExportProgressLabel = L("SV_PreparingPdf");
                        _vectorPdf.Export(rendered, outPath);
                    });
                });

                ExportStatusText = Lf("SV_PdfExported", total, Path.GetFileName(dlg.FileName));
                ExportStatusColor = "#22C55E";
            }
            catch (Exception ex)
            {
                ExportStatusText = Lf("SV_PdfExportError", ex.Message);
                ExportStatusColor = "#EF4444";
            }
            finally
            {
                IsExporting = false;
                ExportProgressLabel = "";
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SHARED / INIT
        // ═══════════════════════════════════════════════════════════════════════

        [ObservableProperty] private int _activeTabIndex = 0;
        [ObservableProperty] private string _globalStatusMessage = "";
        [ObservableProperty] private string _globalStatusColor = "#6B7280";

        // ── In-workflow tab navigation commands ───────────────────────────────

        /// <summary>Navigate to the Mapping tab.</summary>
        [RelayCommand] private void GoToMapping() => ActiveTabIndex = 1;

        /// <summary>Navigate to the Preview tab.</summary>
        [RelayCommand] private void GoToPreview() => ActiveTabIndex = 3;

        /// <summary>Navigate to the Export tab.</summary>
        [RelayCommand] private void GoToExport() => ActiveTabIndex = 4;

        /// <summary>
        /// Refresh the preview whenever the user navigates to the Preview tab (index 3).
        /// Without this, PreviewFields stays empty until the user manually clicks Next/Prev.
        /// Also refresh on tab 4 (Export) so ExportCurrentRecordPng works with fresh data.
        /// </summary>
        partial void OnActiveTabIndexChanged(int value)
        {
            if ((value == 3 || value == 4) && HasPastedData)
                RefreshPreview();
        }

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
            ImageFolderPath = state.ImageFolder;
            SelectedMatchMode = state.ImageMatchMode;
            ImageKeyColumn = state.ImageKeyColumn;
            ExportSettings = state.ExportSettings;
            ActiveTabIndex = state.ActiveTabIndex;
            PreviewRowIndex = state.PreviewRowIndex;
            HasPastedData = state.DataSource.HasData;
        }

        /// <summary>Snapshot current UI state into the persisted model.</summary>
        public SmartVariablesState SnapshotState()
        {
            State.ImageFolder = ImageFolderPath;
            State.ImageMatchMode = SelectedMatchMode;
            State.ImageKeyColumn = ImageKeyColumn ?? "";
            State.ExportSettings = ExportSettings;
            State.ActiveTabIndex = ActiveTabIndex;
            State.PreviewRowIndex = PreviewRowIndex;
            State.LastSaved = DateTime.Now;
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
                    TrimWhitespace = true,
                    IgnoreEmptyRows = true,
                    HasHeader = true,
                });

                State.DataSource = src;
                HasPastedData = src.HasData;
                TotalRowCount = src.TotalRows;
                ColumnCount = src.Columns.Count;

                DetectedSeparatorLabel = src.DetectedSeparator == '\t' ? "Tab (Excel)" :
                                         src.DetectedSeparator == ',' ? L("SV_SepComma") :
                                         src.DetectedSeparator == ';' ? L("SV_SepSemicolon") : L("SV_Unknown");

                RefreshDataGrid();

                PasteStatus = src.HasData
                    ? Lf("SV_ParsedSummary", src.TotalRows, src.Columns.Count, DetectedSeparatorLabel)
                    : L("SV_NoDataDetected");
                PasteStatusColor = src.HasData ? "#22C55E" : "#EF4444";

                // Auto-trigger mapping if fields are loaded
                if (src.HasData && _templateFields.Count > 0)
                    AutoMapCommand.Execute(null);
            }
            catch (Exception ex)
            {
                PasteStatus = Lf("SV_ParseError", ex.Message);
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
                MappingRows.Add(new MappingRowItem(m, State.DataSource.Columns, OnMappingColumnChanged));

            MappedCount = State.Mappings.Count(m => m.IsMapped);
            UnmappedRequiredCount = State.Mappings.Count(m => m.HasError);
        }

        /// <summary>
        /// Called by every MappingRowItem whenever the user changes a dropdown.
        /// Persists the change to the shared session and refreshes dependent UI.
        /// This is the fix for v2.0.8: column changes were previously lost because
        /// SelectedColumn.set only updated _mapping.ColumnName locally without
        /// propagating back to the session.
        /// </summary>
        private void OnMappingColumnChanged(string fieldId, string? newColumn)
        {
            // State.Mappings already updated via _mapping.ColumnName in the setter
            _session?.SetMappings(State.Mappings);

            MappedCount = State.Mappings.Count(m => m.IsMapped);
            UnmappedRequiredCount = State.Mappings.Count(m => m.HasError);

            int mapped = MappedCount;
            int required = UnmappedRequiredCount;
            MappingStatus = required > 0
                ? Lf("SV_RequiredUnmapped", required)
                : mapped > 0
                    ? Lf("SV_MappedCount", mapped, State.Mappings.Count)
                    : "";
            MappingStatusColor = required > 0 ? "#F59E0B" : "#22C55E";

            // Refresh preview live if user is watching it
            if (ActiveTabIndex == 3 || ActiveTabIndex == 4)
                RefreshPreview();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper display models (thin wrappers for UI binding)
    // ═══════════════════════════════════════════════════════════════════════════

    public class DataRowItem
    {
        private readonly SmartDataRow _row;
        private readonly List<string> _columns;
        public int RowIndex => _row.RowIndex;
        public string StatusIcon => _row.StatusIcon;
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
        private readonly Action<string, string?>? _onChanged;   // (fieldId, newColumn)
        public List<string> AvailableColumns { get; }

        public string FieldId => _mapping.FieldId;
        public string FieldLabel => _mapping.FieldLabel;
        public bool IsRequired => _mapping.IsRequired;
        public string StatusLabel => _mapping.StatusLabel;
        public string StatusColor => _mapping.StatusColor;
        public string ConfidenceLabel => _mapping.ConfidenceLabel;

        public string? SelectedColumn
        {
            get => _mapping.ColumnName;
            set
            {
                if (_mapping.ColumnName == value) return;   // no-op guard
                _mapping.ColumnName = value;
                _mapping.Confidence = string.IsNullOrEmpty(value)
                    ? MappingConfidence.None : MappingConfidence.High;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(ConfidenceLabel));  // v2.0.9: update badge
                // ← v2.0.8 fix: notify the parent VM so the session is updated
                // immediately whenever the user picks a column from the dropdown.
                _onChanged?.Invoke(_mapping.FieldId, value);
            }
        }

        public string? DefaultValue
        {
            get => _mapping.DefaultValue;
            set { _mapping.DefaultValue = value; OnPropertyChanged(); }
        }

        public MappingRowItem(
            VariableMapping mapping,
            List<string> columns,
            Action<string, string?>? onChanged = null)
        {
            _mapping = mapping;
            _onChanged = onChanged;
            AvailableColumns = new List<string> { "" }.Concat(columns).ToList();
        }
    }

    public class ImageAssetItem : ObservableObject
    {
        public ImageAsset Asset { get; }

        public string FileName => Asset.FileName;
        public string SizeLabel => Asset.SizeLabel;
        public string DimensionsLabel => Asset.DimensionsLabel;
        public string StatusIcon => Asset.StatusIcon;
        public string Extension => Asset.Extension;
        public string? ThumbnailBase64 => Asset.ThumbnailBase64;

        private bool _isUsed;
        public bool IsUsed { get => _isUsed; private set => SetProperty(ref _isUsed, value); }

        public void RefreshUsed() => IsUsed = Asset.IsUsed;

        public ImageAssetItem(ImageAsset asset)
        {
            Asset = asset;
            _isUsed = asset.IsUsed;
        }
    }

    public class PreviewFieldItem
    {
        public string FieldLabel { get; }
        public string Value { get; }
        public PreviewFieldItem(string label, string value) { FieldLabel = label; Value = value; }
    }

    public class PreflightIssueItem
    {
        private readonly ValidationIssue _issue;
        public string LevelIcon => _issue.LevelIcon;
        public string LevelColor => _issue.LevelColor;
        public string LevelLabel => _issue.LevelLabel;
        public string RowLabel => _issue.RowLabel;
        public string Message => _issue.Message;
        public string Suggestion => _issue.Suggestion;
        public PreflightIssueItem(ValidationIssue issue) { _issue = issue; }
    }
}
