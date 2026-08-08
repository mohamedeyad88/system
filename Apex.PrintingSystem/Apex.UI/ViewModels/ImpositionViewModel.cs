using Apex.Core.Models.Imposition;
using Apex.Core.Models.PaperCutting;
using Apex.Services.Imposition;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Smart Imposition &amp; Finishing module.
    /// Binds the imposition inputs, runs <see cref="ImpositionService"/>, renders a
    /// sheet preview, and exports a print-ready PDF via <see cref="PdfImpositionEngine"/>.
    /// </summary>
    public partial class ImpositionViewModel : ViewModelBase
    {
        private readonly ImpositionService _planner;
        private readonly PdfOperationsService _pdfOps;
        private readonly PdfImpositionEngine _engine;
        private readonly ImpositionTemplateStore _templates;
        private readonly Apex.Core.Interfaces.IFileDialogService _dialogs;

        private static string PdfFilter => Res("Imp_PdfOpenFilter");
        private static string PdfSaveFilter => Res("Msg_PdfFilter");
        private static string SettingsFilter => Res("Imp_TemplateFilter");

        // Standard sheet sizes (mm). "مخصص" enables the custom width/height inputs.
        private static readonly Dictionary<string, (double W, double H)> SizePresets = new()
        {
            ["A5"] = (148, 210),
            ["A4"] = (210, 297),
            ["A3"] = (297, 420),
            ["SRA3"] = (320, 450),
            ["35×50"] = (350, 500),
            ["50×70"] = (500, 700),
            ["70×100"] = (700, 1000),
            ["مخصص"] = (0, 0),
        };

        // ── Source ─────────────────────────────────────────────────────────
        [ObservableProperty] private string _sourcePath = "";
        [ObservableProperty] private int _sourcePageCount;
        [ObservableProperty] private bool _hasSource;

        // ── Page (trim) size ───────────────────────────────────────────────
        [ObservableProperty] private double _pageWidth = 210;
        [ObservableProperty] private double _pageHeight = 297;

        // ── Sheet size ─────────────────────────────────────────────────────
        [ObservableProperty] private string _selectedSheetPreset = "SRA3";
        [ObservableProperty] private double _sheetWidth = 320;
        [ObservableProperty] private double _sheetHeight = 450;
        [ObservableProperty] private bool _isCustomSheet;

        // ── Scheme ─────────────────────────────────────────────────────────
        [ObservableProperty] private int _selectedTypeIndex;      // index into AvailableTypes
        [ObservableProperty] private int _selectedNUpIndex;       // index into AvailableNUps
        [ObservableProperty] private int _selectedAlignmentIndex = 4;  // AvailableAlignments (4 = Centre)
        [ObservableProperty] private int _leavesPerSignature = 4;
        [ObservableProperty] private int _requiredCopies = 100;
        [ObservableProperty] private bool _allowRotation = true;
        [ObservableProperty] private bool _scaleToFit;
        [ObservableProperty] private bool _mirrorBacks;
        [ObservableProperty] private bool _alignSheetsIndependently;

        /// <summary>The scheme the combo index maps to (the combo order mirrors the enum).</summary>
        public ImpositionType SelectedType => (ImpositionType)SelectedTypeIndex;

        // Visibility helpers for the conditional inputs. Compared against the enum
        // rather than raw indices so adding a scheme cannot silently shift them.
        public bool IsNUpScheme => SelectedType == ImpositionType.NUp;
        public bool IsBookletScheme => SelectedType is ImpositionType.SaddleStitch or ImpositionType.PerfectBinding;
        public bool IsPerfectBinding => SelectedType == ImpositionType.PerfectBinding;

        /// <summary>Schemes driven by a copy count rather than a page range.</summary>
        public bool IsRepeatScheme => SelectedType is ImpositionType.CutStack or ImpositionType.StepRepeat
                                                   or ImpositionType.WorkAndTurn or ImpositionType.WorkAndTumble;

        /// <summary>Plain-language explanation of the selected imposition type (localized).</summary>
        public string SchemeDescription => SelectedType switch
        {
            ImpositionType.NUp => Res("Imp_Desc_NUp"),
            ImpositionType.SaddleStitch => Res("Imp_Desc_Saddle"),
            ImpositionType.PerfectBinding => Res("Imp_Desc_Perfect"),
            ImpositionType.CutStack => Res("Imp_Desc_CutStack"),
            ImpositionType.StepRepeat => Res("Imp_Desc_StepRepeat"),
            ImpositionType.WorkAndTurn => Res("Imp_Desc_WorkTurn"),
            ImpositionType.WorkAndTumble => Res("Imp_Desc_WorkTumble"),
            _ => string.Empty,
        };

        private static string Res(string key) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? string.Empty;

        // ── Bleed / margins ────────────────────────────────────────────────
        [ObservableProperty] private double _bleed = 3;
        [ObservableProperty] private double _sheetMargin = 10;
        [ObservableProperty] private double _gutter = 5;

        /// <summary>Paper caliper (mm) driving creep compensation; 0 disables it.</summary>
        [ObservableProperty] private double _paperThickness;

        // ── Per-edge margins / split gutters ──────────────────────────────────
        // Off by default: the uniform Margin/Gutter above are used until the operator
        // opts in. The gripper edge is the reason this exists — the press jaws hold
        // one edge, which needs more clearance than the other three.
        [ObservableProperty] private bool _useAdvancedSpacing;
        [ObservableProperty] private double _marginLeft = 10;
        [ObservableProperty] private double _marginTop = 10;
        [ObservableProperty] private double _marginRight = 10;
        [ObservableProperty] private double _marginBottom = 10;
        [ObservableProperty] private double _gutterHorizontal = 5;
        [ObservableProperty] private double _gutterVertical = 5;
        [ObservableProperty] private double _bleedHorizontal = 3;
        [ObservableProperty] private double _bleedVertical = 3;

        /// <summary>Seeds the per-edge boxes from the uniform values when first enabled.</summary>
        partial void OnUseAdvancedSpacingChanged(bool value)
        {
            if (!value) return;
            MarginLeft = MarginTop = MarginRight = MarginBottom = SheetMargin;
            GutterHorizontal = GutterVertical = Gutter;
            BleedHorizontal = BleedVertical = Bleed;
        }

        // ── Marks ──────────────────────────────────────────────────────────
        [ObservableProperty] private bool _cropMarks = true;
        [ObservableProperty] private bool _foldMarks;
        [ObservableProperty] private bool _registrationMarks;
        [ObservableProperty] private bool _colorBars;
        [ObservableProperty] private bool _jobInfo = true;

        // ── Results ────────────────────────────────────────────────────────
        [ObservableProperty] private bool _hasResult;
        [ObservableProperty] private bool _hasError;
        [ObservableProperty] private string _errorMessage = "";
        [ObservableProperty] private string _statusMessage = "";

        [ObservableProperty] private int _sheetsRequired;
        [ObservableProperty] private int _signatureCount;
        [ObservableProperty] private int _pagesPerSide;
        [ObservableProperty] private int _paddingPages;
        [ObservableProperty] private bool _isDuplex;
        [ObservableProperty] private double _utilizationPercent;
        [ObservableProperty] private string _humanReadableSummary = "";

        public ObservableCollection<string> Warnings { get; } = new();

        // Preview rectangles (normalised 0-1) for the first sheet's front side
        public ObservableCollection<ImpositionPreviewRect> PreviewRects { get; } = new();

        // ── Templates & export ─────────────────────────────────────────────
        [ObservableProperty] private string _templateName = "";
        [ObservableProperty] private ImpositionTemplate? _selectedTemplate;
        public ObservableCollection<ImpositionTemplate> Templates { get; } = new();

        /// <summary>PDF/X identification level: 0=None, 1=PDF/X-1a, 2=PDF/X-3.</summary>
        [ObservableProperty] private int _pdfXIndex;

        // ── PDF resize tool ────────────────────────────────────────────────
        [ObservableProperty] private string _resizePreset = "A4";
        [ObservableProperty] private double _resizeWidth = 210;
        [ObservableProperty] private double _resizeHeight = 297;
        [ObservableProperty] private bool _resizeMaintainAspect = true;
        [ObservableProperty] private bool _isResizeCustom;

        // ── Preview navigation ─────────────────────────────────────────────
        [ObservableProperty] private int _currentSheetIndex;
        [ObservableProperty] private bool _showBack;

        // Sheet geometry for the visual preview (mm) — lets the preview keep the
        // real aspect ratio instead of stretching into a fixed box.
        [ObservableProperty] private double _previewSheetWidthMm;
        [ObservableProperty] private double _previewSheetHeightMm;
        [ObservableProperty] private double _previewBleedMm;
        public int TotalSheets => _lastResult?.Sheets.Count ?? 0;
        public bool CanShowBack => _lastResult?.IsDuplex == true;
        public string CurrentSheetLabel =>
            TotalSheets == 0 ? "—" : Lf("Imp_SheetLabel", CurrentSheetIndex + 1, TotalSheets) + (ShowBack ? Res("Imp_Back") : Res("Imp_Front"));

        private ImpositionResult? _lastResult;

        // ── Choice lists ───────────────────────────────────────────────────
        // Localized combo item names, resolved from the ACTIVE language dictionary
        // (order MUST match ImpositionType / SheetAlignment enums).
        public string[] AvailableTypes { get; } =
        {
            Res("Imp_Type_NUp"), Res("Imp_Type_Saddle"), Res("Imp_Type_Perfect"),
            Res("Imp_Type_CutStack"), Res("Imp_Type_StepRepeat"),
            Res("Imp_Type_WorkTurn"), Res("Imp_Type_WorkTumble"),
        };
        public string[] AvailableNUps { get; } = { "2-Up", "4-Up", "6-Up", "8-Up", "16-Up" };
        public string[] AvailableAlignments { get; } =
        {
            Res("Align_TopLeft"), Res("Align_TopCentre"), Res("Align_TopRight"),
            Res("Align_Left"), Res("Align_Centre"), Res("Align_Right"),
            Res("Align_BottomLeft"), Res("Align_BottomCentre"), Res("Align_BottomRight"),
        };
        public string[] SheetPresets => SizePresets.Keys.ToArray();
        // Labelled as an INTENT, not a conformance claim: the exporter cannot embed a
        // real ICC OutputIntent yet, so promising "PDF/X-1a" would be false.
        public string[] AvailablePdfX { get; } =
            { Res("Imp_PlainPdf"), Res("Imp_IntentX1a"), Res("Imp_IntentX3") };

        public ImpositionViewModel(
            ImpositionService planner,
            PdfOperationsService pdfOps,
            PdfImpositionEngine engine,
            ImpositionTemplateStore templates,
            Apex.Core.Interfaces.IFileDialogService dialogs)
        {
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _pdfOps = pdfOps ?? throw new ArgumentNullException(nameof(pdfOps));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            RefreshTemplates();
        }

        // ── Partial callbacks ──────────────────────────────────────────────
        partial void OnSelectedTypeIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsNUpScheme));
            OnPropertyChanged(nameof(IsBookletScheme));
            OnPropertyChanged(nameof(IsPerfectBinding));
            OnPropertyChanged(nameof(IsRepeatScheme));
            OnPropertyChanged(nameof(SelectedType));
            OnPropertyChanged(nameof(SchemeDescription));
        }

        partial void OnSelectedSheetPresetChanged(string value)
        {
            if (SizePresets.TryGetValue(value, out var sz))
            {
                IsCustomSheet = value == "مخصص";
                if (!IsCustomSheet)
                {
                    SheetWidth = sz.W;
                    SheetHeight = sz.H;
                }
            }
        }

        partial void OnResizePresetChanged(string value)
        {
            if (SizePresets.TryGetValue(value, out var sz))
            {
                IsResizeCustom = value == "مخصص";
                if (!IsResizeCustom)
                {
                    ResizeWidth = sz.W;
                    ResizeHeight = sz.H;
                }
            }
        }

        // ── PDF resize command ─────────────────────────────────────────────

        [RelayCommand]
        private void ResizePdf()
        {
            HasError = false;
            ErrorMessage = "";

            if (!HasSource || !File.Exists(SourcePath))
            {
                HasError = true;
                ErrorMessage = Res("Imp_ChooseSourceFirst");
                return;
            }
            if (ResizeWidth <= 0 || ResizeHeight <= 0)
            {
                HasError = true;
                ErrorMessage = Res("Imp_TargetPositive");
                return;
            }

            var target = _dialogs.SaveFile(Res("Imp_SaveResized"), PdfSaveFilter,
                $"{Path.GetFileNameWithoutExtension(SourcePath)}-{ResizeWidth:0}x{ResizeHeight:0}mm.pdf");
            if (target == null) return;

            try
            {
                var bytes = File.ReadAllBytes(SourcePath);
                var resized = _pdfOps.ResizePages(bytes, ResizeWidth, ResizeHeight, ResizeMaintainAspect);
                File.WriteAllBytes(target, resized);
                StatusMessage = Lf("Imp_Resized", ResizeWidth.ToString("0"), ResizeHeight.ToString("0"), Path.GetFileName(target));
                HasError = false;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = Lf("Imp_ResizeFailed", ex.Message);
            }
        }

        // ── Commands ───────────────────────────────────────────────────────

        [RelayCommand]
        private void BrowseSource()
        {
            var path = _dialogs.OpenFile(Res("Imp_ChooseSource"), PdfFilter);
            if (path == null) return;
            LoadSource(path);
        }

        /// <summary>Loads a source PDF by path (extracted for testability).</summary>
        public void LoadSource(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                SourcePageCount = _pdfOps.GetPageCount(bytes);
                SourcePath = path;
                HasSource = true;

                // Prepress guard: auto-fill the page size from the REAL source page.
                // A configured size that differs from the source used to shrink the
                // placed pages (uniform fit) without the user realising why.
                var (srcW, srcH) = _pdfOps.GetFirstPageSizeMm(bytes);
                if (srcW > 1 && srcH > 1)
                {
                    PageWidth = Math.Round(srcW, 1);
                    PageHeight = Math.Round(srcH, 1);
                    StatusMessage = Lf("Imp_LoadedDetected", SourcePageCount,
                        PageWidth.ToString("0.#"), PageHeight.ToString("0.#"));
                }
                else
                {
                    StatusMessage = Lf("Imp_LoadedSource", SourcePageCount);
                }
                HasError = false;

                // Check the file BEFORE any paper is committed to the job.
                _ = RunPreflightAsync(path);
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = Lf("Imp_ReadFailed", ex.Message);
                HasSource = false;
            }
        }

        [RelayCommand]
        private async Task PlanAsync()
        {
            HasError = false;
            HasResult = false;
            ErrorMessage = "";

            try
            {
                var input = BuildInput();
                var result = _planner.Plan(input);

                if (!result.IsValid)
                {
                    HasError = true;
                    ErrorMessage = result.ErrorMessage;
                    PreviewImage = null;
                    return;
                }

                _lastResult = result;
                ApplyResult(result);
                HasResult = true;

                await RenderPreviewAsync();
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = Lf("Imp_PlanFailed", ex.Message);
                HasResult = false;
                PreviewImage = null;
            }
        }

        // ── Live preview: rasterize the first imposed sheet so the operator sees the
        //    real page content laid out (with alignment/scaling/marks) before export. ──
        [ObservableProperty] private System.Windows.Media.Imaging.BitmapSource? _previewImage;
        [ObservableProperty] private bool _isPreviewLoading;

        /// <summary>True when a rendered sheet image is available (hide the slot-rect fallback).</summary>
        public bool HasPreviewImage => PreviewImage != null;
        partial void OnPreviewImageChanged(System.Windows.Media.Imaging.BitmapSource? value)
            => OnPropertyChanged(nameof(HasPreviewImage));

        private async Task RenderPreviewAsync()
        {
            if (_lastResult == null || !HasSource || !File.Exists(SourcePath) || _lastResult.Sheets.Count == 0)
            {
                PreviewImage = null;
                return;
            }

            var plan = _lastResult;
            var source = SourcePath;
            var marks = BuildMarks();
            int sheetIndex = CurrentSheetIndex;
            int reqId = ++_previewRequestId;   // supersede any in-flight preview

            IsPreviewLoading = true;
            try
            {
                byte[] png = await Task.Run(() => _engine.RenderSheetPng(plan, source, marks, sheetIndex, dpi: 110));
                if (reqId != _previewRequestId) return;   // a newer request started; drop this stale result
                PreviewImage = LoadFrozenBitmap(png);
            }
            catch
            {
                if (reqId == _previewRequestId) PreviewImage = null; // best-effort; export unaffected
            }
            finally
            {
                if (reqId == _previewRequestId) IsPreviewLoading = false;
            }
        }

        private int _previewRequestId;

        private static System.Windows.Media.Imaging.BitmapImage LoadFrozenBitmap(byte[] png)
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            using var ms = new MemoryStream(png);
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // True while a PDF export is running on a background thread — bind to show a
        // spinner / disable the UI. The generated ExportCommand also auto-disables itself.
        [ObservableProperty] private bool _isExporting;

        [RelayCommand]
        private async Task ExportAsync()
        {
            if (_lastResult == null || !_lastResult.IsValid)
            {
                HasError = true;
                ErrorMessage = Res("Imp_PlanFirst");
                return;
            }
            if (!HasSource || !File.Exists(SourcePath))
            {
                HasError = true;
                ErrorMessage = Res("Imp_ChooseValidSource");
                return;
            }

            var target = _dialogs.SaveFile(Res("Imp_SaveImposition"), PdfSaveFilter,
                $"{Path.GetFileNameWithoutExtension(SourcePath)}-imposed.pdf");
            if (target == null) return;

            // Snapshot everything the background thread needs (no cross-thread VM access).
            var result = _lastResult;
            var source = SourcePath;
            var marks = BuildMarks();
            var export = new ImpositionExportOptions
            {
                PdfX = (PdfXConformance)PdfXIndex,
                Title = Path.GetFileNameWithoutExtension(SourcePath),
                Author = "Apex Print OS"
            };

            IsExporting = true;
            HasError = false;
            StatusMessage = Res("Imp_Exporting");
            try
            {
                // Heavy PDF generation + disk write run off the UI thread → no freeze.
                await Task.Run(() =>
                {
                    var pdf = _engine.Generate(result, source, marks, export);
                    File.WriteAllBytes(target, pdf);
                });
                StatusMessage = Lf("Imp_Exported", Path.GetFileName(target));
                HasError = false;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = Lf("Imp_ExportFailed", ex.Message);
                StatusMessage = "";
            }
            finally
            {
                IsExporting = false;
            }
        }

        // ── Template & settings commands ───────────────────────────────────

        [RelayCommand]
        private void SaveTemplate()
        {
            if (string.IsNullOrWhiteSpace(TemplateName))
            {
                HasError = true;
                ErrorMessage = Res("Imp_EnterTemplateName");
                return;
            }
            try
            {
                var tpl = new ImpositionTemplate
                {
                    Name = TemplateName.Trim(),
                    Input = BuildInput(),
                    Marks = BuildMarks()
                };
                _templates.Save(tpl);
                RefreshTemplates();
                TemplateName = "";
                StatusMessage = Lf("Imp_TemplateSaved", tpl.Name);
                HasError = false;
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Msg_SaveFailed", ex.Message); }
        }

        [RelayCommand]
        private void LoadTemplate()
        {
            if (SelectedTemplate == null) return;
            ApplyTemplate(SelectedTemplate);
            StatusMessage = Lf("Imp_TemplateLoaded", SelectedTemplate.Name);
            HasError = false;
        }

        [RelayCommand]
        private void DeleteTemplate()
        {
            if (SelectedTemplate == null) return;

            // Built-in presets are generated, not stored — they cannot be deleted.
            if (BuiltInImpositionTemplates.IsBuiltIn(SelectedTemplate))
            {
                StatusMessage = Res("Imp_BuiltInReadOnly");
                return;
            }

            _templates.Delete(SelectedTemplate.Id);
            RefreshTemplates();
            StatusMessage = Res("Imp_TemplateDeleted");
        }

        [RelayCommand]
        private void ExportSettings()
        {
            var target = _dialogs.SaveFile(Res("Imp_ExportSettingsTitle"), SettingsFilter,
                $"{(string.IsNullOrWhiteSpace(TemplateName) ? "imposition-settings" : TemplateName.Trim())}.apeximp");
            if (target == null) return;
            try
            {
                var tpl = new ImpositionTemplate
                {
                    Name = string.IsNullOrWhiteSpace(TemplateName) ? Res("Imp_ExportedSettingsName") : TemplateName.Trim(),
                    Input = BuildInput(),
                    Marks = BuildMarks()
                };
                _templates.ExportToFile(tpl, target);
                StatusMessage = Lf("Imp_SettingsExported", Path.GetFileName(target));
                HasError = false;
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Imp_ExportFailed", ex.Message); }
        }

        [RelayCommand]
        private void ImportSettings()
        {
            var path = _dialogs.OpenFile(Res("Imp_ImportSettingsTitle"), SettingsFilter);
            if (path == null) return;
            try
            {
                var tpl = _templates.ImportFromFile(path);
                RefreshTemplates();
                ApplyTemplate(tpl);
                StatusMessage = Lf("Imp_SettingsImported", tpl.Name);
                HasError = false;
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Imp_ImportFailed", ex.Message); }
        }

        // ── Preflight ──────────────────────────────────────────────────────────

        /// <summary>Findings for the loaded source file, worst first.</summary>
        public ObservableCollection<string> PreflightFindings { get; } = new();

        [ObservableProperty] private bool _hasPreflightFindings;
        [ObservableProperty] private bool _preflightBlocksPrint;
        [ObservableProperty] private string _preflightSummary = "";
        [ObservableProperty] private string _preflightSummaryColor = "#94A3B8";

        private readonly Apex.Services.Printing.PreflightAnalysisService _preflight =
            Apex.Services.Printing.PreflightAnalysisService.Instance;

        /// <summary>
        /// Runs the existing preflight analyser on the loaded file and surfaces the
        /// result. The analyser was already written and fully functional but nothing
        /// ever called it, so resolution, colour and font problems were only
        /// discovered on press — after the paper was spent.
        /// </summary>
        private async Task RunPreflightAsync(string path)
        {
            try
            {
                var report = await _preflight.AnalyzeAsync(path);

                PreflightFindings.Clear();
                foreach (var item in report.Items
                             .Where(i => i.Severity >= Apex.Services.Printing.PreflightSeverity.Warning)
                             .OrderByDescending(i => i.Severity))
                {
                    PreflightFindings.Add(item.Suggestion is { Length: > 0 }
                        ? $"[{item.Category}] {item.Message} → {item.Suggestion}"
                        : $"[{item.Category}] {item.Message}");
                }

                HasPreflightFindings = PreflightFindings.Count > 0;
                PreflightBlocksPrint = !report.CanPrint;

                PreflightSummary =
                    !report.CanPrint ? Lf("Imp_PreflightBlocked", report.ErrorCount)
                    : report.HasErrors ? Lf("Imp_PreflightErrors", report.ErrorCount)
                    : report.HasWarnings ? Lf("Imp_PreflightWarnings", report.WarningCount)
                    : Res("Imp_PreflightClean");

                PreflightSummaryColor =
                    !report.CanPrint || report.HasErrors ? "#EF4444"
                    : report.HasWarnings ? "#F59E0B"
                    : "#22C55E";
            }
            catch
            {
                // Preflight is advisory: never block loading a file because the
                // analysis itself failed.
                PreflightFindings.Clear();
                HasPreflightFindings = false;
                PreflightBlocksPrint = false;
                PreflightSummary = "";
            }
        }

        private void RefreshTemplates()
        {
            Templates.Clear();
            // Standard presets first, then the operator's own saved templates.
            foreach (var t in BuiltInImpositionTemplates.All()) Templates.Add(t);
            foreach (var t in _templates.GetAll()) Templates.Add(t);
        }

        private void ApplyTemplate(ImpositionTemplate tpl)
        {
            var i = tpl.Input;
            PageWidth = i.PageWidth; PageHeight = i.PageHeight;
            SheetWidth = i.SheetWidth; SheetHeight = i.SheetHeight;
            SelectedSheetPreset = MatchPreset(i.SheetWidth, i.SheetHeight);
            SelectedTypeIndex = (int)i.Type;
            SelectedNUpIndex = NUpToIndex(i.NUp);
            LeavesPerSignature = i.LeavesPerSignature;
            RequiredCopies = i.RequiredCopies;
            Bleed = i.Bleed; SheetMargin = i.SheetMargin; Gutter = i.Gutter;
            AllowRotation = i.AllowRotation;

            var m = tpl.Marks;
            CropMarks = m.CropMarks; FoldMarks = m.FoldMarks;
            RegistrationMarks = m.RegistrationMarks; ColorBars = m.ColorBars; JobInfo = m.JobInfo;
        }

        private static string MatchPreset(double w, double h)
        {
            foreach (var kv in SizePresets)
                if (kv.Key != "مخصص" && System.Math.Abs(kv.Value.W - w) < 0.5 && System.Math.Abs(kv.Value.H - h) < 0.5)
                    return kv.Key;
            return "مخصص";
        }

        private static int NUpToIndex(NUpLayout n) => n switch
        {
            NUpLayout.TwoUp => 0,
            NUpLayout.FourUp => 1,
            NUpLayout.SixUp => 2,
            NUpLayout.EightUp => 3,
            _ => 4
        };

        [RelayCommand]
        private void Reset()
        {
            SourcePath = "";
            SourcePageCount = 0;
            HasSource = false;
            PageWidth = 210; PageHeight = 297;
            SelectedSheetPreset = "SRA3";
            SelectedTypeIndex = 0; SelectedNUpIndex = 0; SelectedAlignmentIndex = 4;
            LeavesPerSignature = 4; RequiredCopies = 100;
            Bleed = 3; SheetMargin = 10; Gutter = 5;
            AllowRotation = true; ScaleToFit = false; MirrorBacks = false; AlignSheetsIndependently = false;
            CropMarks = true; FoldMarks = false; RegistrationMarks = false;
            ColorBars = false; JobInfo = true;
            HasResult = false; HasError = false;
            _lastResult = null;
            Warnings.Clear();
            PreviewRects.Clear();
            HumanReadableSummary = "";
            StatusMessage = "";
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private ImpositionInput BuildInput() => new()
        {
            SourcePageCount = HasSource ? SourcePageCount : Math.Max(1, SourcePageCount),
            PageWidth = PageWidth,
            PageHeight = PageHeight,
            SheetWidth = SheetWidth,
            SheetHeight = SheetHeight,
            Type = (ImpositionType)SelectedTypeIndex,
            NUp = IndexToNUp(SelectedNUpIndex),
            Binding = IsPerfectBinding ? BindingType.PerfectBinding : BindingType.SaddleStitch,
            LeavesPerSignature = LeavesPerSignature,
            RequiredCopies = RequiredCopies,
            Bleed = Bleed,
            SheetMargin = SheetMargin,
            Gutter = Gutter,
            PaperThickness = PaperThickness,

            // null keeps the uniform value; only the advanced mode overrides per edge.
            MarginLeft = UseAdvancedSpacing ? MarginLeft : null,
            MarginTop = UseAdvancedSpacing ? MarginTop : null,
            MarginRight = UseAdvancedSpacing ? MarginRight : null,
            MarginBottom = UseAdvancedSpacing ? MarginBottom : null,
            GutterHorizontal = UseAdvancedSpacing ? GutterHorizontal : null,
            GutterVertical = UseAdvancedSpacing ? GutterVertical : null,
            BleedHorizontal = UseAdvancedSpacing ? BleedHorizontal : null,
            BleedVertical = UseAdvancedSpacing ? BleedVertical : null,
            AllowRotation = AllowRotation,
            Alignment = (SheetAlignment)SelectedAlignmentIndex,
            ScaleToFit = ScaleToFit,
            MirrorBacksHorizontally = MirrorBacks,
            AlignSheetsIndependently = AlignSheetsIndependently,
            Unit = MeasurementUnit.Millimeter
        };

        private PrintMarksOptions BuildMarks() => new()
        {
            CropMarks = CropMarks,
            FoldMarks = FoldMarks,
            RegistrationMarks = RegistrationMarks,
            ColorBars = ColorBars,
            JobInfo = JobInfo,
            JobName = Path.GetFileNameWithoutExtension(SourcePath),
            FileName = Path.GetFileName(SourcePath),
            IncludeDate = true
        };

        private static NUpLayout IndexToNUp(int idx) => idx switch
        {
            0 => NUpLayout.TwoUp,
            1 => NUpLayout.FourUp,
            2 => NUpLayout.SixUp,
            3 => NUpLayout.EightUp,
            _ => NUpLayout.SixteenUp
        };

        private void ApplyResult(ImpositionResult r)
        {
            SheetsRequired = r.SheetsRequired;
            SignatureCount = r.SignatureCount;
            PagesPerSide = r.PagesPerSide;
            PaddingPages = r.PaddingPages;
            IsDuplex = r.IsDuplex;
            UtilizationPercent = r.UtilizationPercent;
            HumanReadableSummary = r.HumanReadableSummary;

            Warnings.Clear();
            foreach (var w in r.Warnings) Warnings.Add(w);

            CurrentSheetIndex = 0;
            ShowBack = false;
            NotifyPreviewNav();
            RenderCurrentSheet();
        }

        // ── Preview navigation commands ────────────────────────────────────

        [RelayCommand]
        private void NextSheet()
        {
            if (_lastResult == null) return;
            if (CurrentSheetIndex < _lastResult.Sheets.Count - 1)
            {
                CurrentSheetIndex++;
                ShowBack = false;
                NotifyPreviewNav();
                RenderCurrentSheet();
                _ = RenderPreviewAsync();
            }
        }

        [RelayCommand]
        private void PrevSheet()
        {
            if (_lastResult == null || CurrentSheetIndex <= 0) return;
            CurrentSheetIndex--;
            ShowBack = false;
            NotifyPreviewNav();
            RenderCurrentSheet();
            _ = RenderPreviewAsync();
        }

        [RelayCommand]
        private void ToggleSide()
        {
            if (!CanShowBack) return;
            ShowBack = !ShowBack;
            NotifyPreviewNav();
            RenderCurrentSheet();
            _ = RenderPreviewAsync();
        }

        private void NotifyPreviewNav()
        {
            OnPropertyChanged(nameof(TotalSheets));
            OnPropertyChanged(nameof(CanShowBack));
            OnPropertyChanged(nameof(CurrentSheetLabel));
        }

        private void RenderCurrentSheet()
        {
            PreviewRects.Clear();
            var r = _lastResult;
            if (r == null || r.SheetWidthMm <= 0 || r.SheetHeightMm <= 0) return;

            var sheet = r.Sheets.ElementAtOrDefault(CurrentSheetIndex);
            if (sheet == null) return;

            // Publish the sheet geometry so the preview can keep the true aspect ratio.
            PreviewSheetWidthMm = r.SheetWidthMm;
            PreviewSheetHeightMm = r.SheetHeightMm;
            PreviewBleedMm = r.BleedMm;

            var side = (ShowBack && sheet.Back != null) ? sheet.Back : sheet.Front;
            foreach (var slot in side.Slots)
            {
                PreviewRects.Add(new ImpositionPreviewRect
                {
                    XMm = slot.X,
                    YMm = slot.Y,
                    WidthMm = slot.Width,
                    HeightMm = slot.Height,
                    Rotation = slot.Rotation,
                    PageLabel = slot.IsBlank ? "" : slot.SourcePageNumber.ToString(),
                });
            }
        }
    }

    /// <summary>Normalised rectangle (0-1) for the imposition sheet preview canvas.</summary>
    /// <summary>
    /// One placed page on the sheet preview, in MILLIMETRES.
    ///
    /// Millimetres (not ratios) so the preview can be drawn at the sheet's true
    /// aspect ratio. The old ratio-based model was stretched into a fixed 420×300
    /// box, which showed a portrait SRA3 sheet as landscape — a preview that
    /// disagreed with the actual press sheet is worse than no preview.
    /// </summary>
    public class ImpositionPreviewRect
    {
        public double XMm { get; set; }
        public double YMm { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }

        /// <summary>Rotation of the placed page in degrees (0/90/180/270).</summary>
        public int Rotation { get; set; }

        /// <summary>1-based source page number, or empty for an intentional blank.</summary>
        public string PageLabel { get; set; } = "";

        public bool IsBlank => string.IsNullOrEmpty(PageLabel);
        public bool IsRotated => Rotation != 0;
    }
}
