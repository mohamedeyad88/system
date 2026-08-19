using Apex.Core.Interfaces;
using Apex.Services.Imposition;
using Apex.Services.Preflight;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// "Page Tools" pre-press toolbox with a live preview. Each operation runs on a
    /// background thread, shows the result in the preview, and keeps it in memory so the
    /// operator can review it before saving (preview-then-save).
    /// </summary>
    public partial class PageToolsViewModel : ViewModelBase
    {
        private readonly PdfPageToolsService _tools;
        private readonly IFileDialogService _dialogs;
        private byte[]? _source;    // the original loaded / generated document (for Reset)
        private byte[]? _working;   // current document after the applied pipeline; ops now
                                    // CHAIN on this instead of always re-running on the
                                    // original — so reverse → bleed → tile is one flow.

        // Undo / redo history. Each snapshot captures the working document AND the list
        // of applied step names, so undo restores both the bytes and the filename suffix.
        private sealed record Snapshot(byte[]? Doc, System.Collections.Generic.List<string> Steps);
        private readonly System.Collections.Generic.Stack<Snapshot> _undo = new();
        private readonly System.Collections.Generic.Stack<Snapshot> _redo = new();
        private System.Collections.Generic.List<string> _steps = new();

        private Snapshot Capture() => new(_working, new System.Collections.Generic.List<string>(_steps));
        private string CurrentSuffix() =>
            _steps.Count == 0 ? "output" : string.Join("-", System.Linq.Enumerable.TakeLast(_steps, 3));

        private static string PdfFilter => L("Msg_PdfFilter");

        [ObservableProperty] private string _sourcePath = "";
        [ObservableProperty] private bool _hasSource;
        [ObservableProperty] private int _pageCount;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _hasResult;
        [ObservableProperty] private string _statusMessage = "";
        [ObservableProperty] private bool _hasError;
        [ObservableProperty] private string _errorMessage = "";

        // ── Pipeline history (chaining + undo/redo) ──────────────────────────
        [ObservableProperty] private bool _canUndo;
        [ObservableProperty] private bool _canRedo;
        [ObservableProperty] private string _pipelineLabel = "";

        private void RefreshHistory()
        {
            CanUndo = _undo.Count > 0;
            CanRedo = _redo.Count > 0;
            HasResult = _steps.Count > 0;
            PipelineLabel = _steps.Count == 0 ? "" : string.Join(" → ", _steps);
        }

        // ── Preflight (file check before print) ──────────────────────────────
        private readonly PreflightService _preflight = new();
        public ObservableCollection<PreflightLine> PreflightResults { get; } = new();
        [ObservableProperty] private bool _hasPreflight;
        [ObservableProperty] private bool _preflightPassed;
        [ObservableProperty] private string _preflightSummary = "";

        [RelayCommand]
        private async Task RunPreflight()
        {
            var doc = _working ?? _source;
            if (doc == null) { HasError = true; ErrorMessage = L("Msg_ChoosePdfFirst"); return; }

            IsBusy = true; HasError = false; StatusMessage = L("Msg_Processing");
            try
            {
                var report = await Task.Run(() => _preflight.Check(doc));
                PreflightResults.Clear();
                foreach (var f in report.Findings)
                    PreflightResults.Add(new PreflightLine(FormatFinding(f), SeverityColor(f.Severity), SeverityIcon(f.Severity)));

                PreflightPassed = report.Passed;
                PreflightSummary = report.Passed
                    ? Lf("PF_SummaryPass", report.Warnings)
                    : Lf("PF_SummaryFail", report.Errors, report.Warnings);
                HasPreflight = true;
                StatusMessage = "";
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Msg_OpFailed", ex.Message); }
            finally { IsBusy = false; }
        }

        private string FormatFinding(PreflightFinding f) => f.Check switch
        {
            PreflightCheck.PageCount         => Lf("PF_PageCount", f.Detail ?? "0"),
            PreflightCheck.PageSize          => Lf("PF_PageSize", f.Detail ?? ""),
            PreflightCheck.InconsistentSizes => Lf("PF_InconsistentSizes", f.Detail ?? ""),
            PreflightCheck.NoBleed           => Lf("PF_NoBleed", f.Detail ?? ""),
            PreflightCheck.TinyPage          => Lf("PF_TinyPage", f.Page ?? 0, f.Detail ?? ""),
            PreflightCheck.Encrypted         => L("PF_Encrypted"),
            PreflightCheck.RgbImages         => Lf("PF_RgbImages", f.Detail ?? "0"),
            PreflightCheck.LowResImages      => Lf("PF_LowResImages", f.Detail ?? "0"),
            PreflightCheck.NoImages          => L("PF_NoImages"),
            PreflightCheck.Clean             => L("PF_Clean"),
            PreflightCheck.ImageScanSkipped  => L("PF_ImageScanSkipped"),
            _                                => f.Check.ToString()
        };

        private static System.Windows.Media.Brush SeverityColor(PreflightSeverity s)
        {
            string hex = s switch
            {
                PreflightSeverity.Error   => "#EF4444",
                PreflightSeverity.Warning => "#F59E0B",
                _                         => "#94A3B8"
            };
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static string SeverityIcon(PreflightSeverity s) => s switch
        {
            PreflightSeverity.Error   => "❌",
            PreflightSeverity.Warning => "⚠️",
            _                         => "ℹ️"
        };

        /// <summary>One localized preflight line for the results list.</summary>
        public sealed record PreflightLine(string Message, System.Windows.Media.Brush Color, string Icon);

        // ── Live preview + page navigation ───────────────────────────────────
        private byte[]? _previewPdf;
        [ObservableProperty] private System.Windows.Media.Imaging.BitmapSource? _previewImage;
        [ObservableProperty] private bool _isPreviewLoading;
        [ObservableProperty] private int _currentPage;      // 0-based
        [ObservableProperty] private int _previewPageCount;
        public bool HasPreviewImage => PreviewImage != null;
        public string PreviewPageLabel => PreviewPageCount == 0 ? "—" : Lf("Msg_PageXofY", CurrentPage + 1, PreviewPageCount);
        public bool CanNavigate => PreviewPageCount > 1;

        partial void OnPreviewImageChanged(System.Windows.Media.Imaging.BitmapSource? value)
            => OnPropertyChanged(nameof(HasPreviewImage));
        partial void OnPreviewPageCountChanged(int value)
        {
            OnPropertyChanged(nameof(PreviewPageLabel));
            OnPropertyChanged(nameof(CanNavigate));
        }
        partial void OnCurrentPageChanged(int value) => OnPropertyChanged(nameof(PreviewPageLabel));

        // ── Preview zoom ─────────────────────────────────────────────────────
        // The rendered page is the operator's last check before an expensive run:
        // tile seams, bleed, page-shift. It has to be zoomable to inspect. 1.0 =
        // fit; range and step match the Template Designer so the two feel the same.
        [ObservableProperty] private double _previewZoom = 1.0;
        public string PreviewZoomLabel => $"{(int)System.Math.Round(PreviewZoom * 100)}%";
        partial void OnPreviewZoomChanged(double value) => OnPropertyChanged(nameof(PreviewZoomLabel));

        [RelayCommand] private void PreviewZoomIn()  => PreviewZoom = System.Math.Min(3.0, System.Math.Round(PreviewZoom + 0.25, 2));
        [RelayCommand] private void PreviewZoomOut() => PreviewZoom = System.Math.Max(0.25, System.Math.Round(PreviewZoom - 0.25, 2));
        [RelayCommand] private void PreviewZoomReset() => PreviewZoom = 1.0;

        /// <summary>Ctrl+Wheel on the preview; +/- one 0.25 step per notch.</summary>
        public void ApplyPreviewWheelZoom(int delta)
            => PreviewZoom = System.Math.Round(System.Math.Max(0.25, System.Math.Min(3.0, PreviewZoom + (delta > 0 ? 0.25 : -0.25))), 2);

        [RelayCommand]
        private async Task NextPageAsync()
        {
            if (_previewPdf == null || CurrentPage >= PreviewPageCount - 1) return;
            CurrentPage++;
            await RenderCurrentPageAsync();
        }

        [RelayCommand]
        private async Task PrevPageAsync()
        {
            if (_previewPdf == null || CurrentPage <= 0) return;
            CurrentPage--;
            await RenderCurrentPageAsync();
        }

        // ── Operation parameters ─────────────────────────────────────────────
        [ObservableProperty] private int _tileCols = 2;
        [ObservableProperty] private int _tileRows = 2;
        [ObservableProperty] private double _tileOverlapMm = 5;
        [ObservableProperty] private double _joinGapMm = 0;
        [ObservableProperty] private double _bleedMm = 3;
        [ObservableProperty] private double _trimMm = 3;
        [ObservableProperty] private double _shiftXmm;
        [ObservableProperty] private double _shiftYmm;
        [ObservableProperty] private int _insertAfter;
        [ObservableProperty] private int _insertCount = 1;
        [ObservableProperty] private bool _backsReversed = true;
        [ObservableProperty] private int _samplePages = 4;
        [ObservableProperty] private double _sampleWidthMm = 210;
        [ObservableProperty] private double _sampleHeightMm = 297;

        // Step-and-repeat (cards / stickers many-up). Sheet defaults to A3.
        [ObservableProperty] private double _stepSheetWidthMm = 297;
        [ObservableProperty] private double _stepSheetHeightMm = 420;
        [ObservableProperty] private int _stepCols = 2;
        [ObservableProperty] private int _stepRows = 4;
        [ObservableProperty] private double _stepGutterMm = 3;
        [ObservableProperty] private bool _stepCropMarks = true;

        // Watermark / stamp (overlay a second PDF on every page). 0 = the stamp's own size.
        [ObservableProperty] private double _stampXmm;
        [ObservableProperty] private double _stampYmm;
        [ObservableProperty] private double _stampWidthMm;
        [ObservableProperty] private double _stampHeightMm;

        // Mask (cover a rectangle on one page, e.g. redact).
        [ObservableProperty] private int _maskPage = 1;
        [ObservableProperty] private double _maskXmm;
        [ObservableProperty] private double _maskYmm;
        [ObservableProperty] private double _maskWidthMm = 50;
        [ObservableProperty] private double _maskHeightMm = 20;

        // Page-range ops (rotate / extract / delete). ToPage 0 = last page.
        [ObservableProperty] private int _rangeFrom = 1;
        [ObservableProperty] private int _rangeTo;

        // Printer's marks (crop + registration + colour bars in an added margin).
        [ObservableProperty] private double _marksMarginMm = 10;
        [ObservableProperty] private bool _marksCrop = true;
        [ObservableProperty] private bool _marksReg = true;
        [ObservableProperty] private bool _marksColorBars = true;

        // Split into multiple files (terminal export, not part of the pipeline).
        [ObservableProperty] private int _splitEveryN = 1;

        private readonly Apex.UI.Services.WorkflowHandoff _handoff;

        public PageToolsViewModel(PdfPageToolsService tools, IFileDialogService dialogs,
            Apex.UI.Services.WorkflowHandoff handoff)
        {
            _tools = tools;
            _dialogs = dialogs;
            _handoff = handoff;
        }

        /// <summary>Workflow chain: send the current result straight to the print queue.</summary>
        [RelayCommand]
        private void SendToPrinting()
        {
            var doc = _working ?? _source;
            if (doc == null) { HasError = true; ErrorMessage = L("Msg_ChoosePdfFirst"); return; }
            try
            {
                string baseName = string.IsNullOrEmpty(SourcePath) ? "apex" : Path.GetFileNameWithoutExtension(SourcePath);
                string tmp = Path.Combine(Path.GetTempPath(), $"{baseName}-{CurrentSuffix()}-{Guid.NewGuid():N}.pdf");
                File.WriteAllBytes(tmp, doc);
                _handoff.SendToPrinting(tmp);
                StatusMessage = L("PT_SentToPrint");
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Msg_SaveFailed", ex.Message); }
        }

        [RelayCommand]
        private async Task LoadSourceAsync()
        {
            var path = _dialogs.OpenFile(L("Msg_ChoosePdf"), PdfFilter);
            if (path == null) return;
            try
            {
                _source = File.ReadAllBytes(path);
                SourcePath = path;
                PageCount = _tools.GetPageCount(_source);
                HasSource = true;
                HasError = false;
                // A fresh document starts a fresh pipeline.
                _working = _source;
                _undo.Clear(); _redo.Clear(); _steps.Clear();
                RefreshHistory();
                StatusMessage = Lf("Msg_Loaded", PageCount);
                await ShowPreviewAsync(_source);   // preview the source immediately
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = Lf("Msg_OpenFailed", ex.Message);
                HasSource = false;
                PreviewImage = null;
            }
        }

        // ── Single-file operations (preview, don't save yet) ─────────────────
        [RelayCommand] private Task ReverseAsync() => RunAsync("reversed", b => _tools.ReversePages(b));
        [RelayCommand] private Task InterleaveAsync() => RunAsync("collated", b => _tools.InterleaveFrontsBacks(b, BacksReversed));
        [RelayCommand] private Task InsertAsync() => RunAsync("inserted", b => _tools.InsertBlankPages(b, InsertAfter, InsertCount));
        [RelayCommand] private Task JoinTwoUpAsync() => RunAsync("joined", b => _tools.JoinTwoUp(b, JoinGapMm));
        [RelayCommand] private Task TileAsync() => RunAsync("tiled", b => _tools.TilePages(b, TileCols, TileRows, TileOverlapMm));
        [RelayCommand] private Task BleedAsync() => RunAsync("bleed", b => _tools.AddBleed(b, BleedMm));
        [RelayCommand] private Task TrimAsync() => RunAsync("trimmed", b => _tools.TrimAndShift(b, TrimMm, ShiftXmm, ShiftYmm));
        [RelayCommand] private Task StepRepeatAsync() => RunAsync("nup", b =>
            _tools.StepAndRepeat(b, StepSheetWidthMm, StepSheetHeightMm, StepCols, StepRows, StepGutterMm, StepCropMarks));

        [RelayCommand] private Task MaskAsync() => RunAsync("masked", b =>
            _tools.MaskArea(b, MaskPage, MaskXmm, MaskYmm, MaskWidthMm, MaskHeightMm));

        [RelayCommand] private Task Rotate90Async()  => RunAsync("rot",     b => _tools.RotatePages(b, 90, RangeFrom, RangeTo));
        [RelayCommand] private Task ExtractRangeAsync() => RunAsync("extract", b => _tools.ExtractPages(b, RangeFrom, RangeTo));
        [RelayCommand] private Task DeleteRangeAsync()  => RunAsync("del",     b => _tools.DeletePages(b, RangeFrom, RangeTo));
        [RelayCommand] private Task PrinterMarksAsync() => RunAsync("marks",   b => _tools.AddPrinterMarks(b, MarksMarginMm, MarksCrop, MarksReg, MarksColorBars));

        /// <summary>Terminal export: splits the working document into numbered files next to the chosen name.</summary>
        [RelayCommand]
        private void SplitToFiles()
        {
            var doc = _working ?? _source;
            if (doc == null) { HasError = true; ErrorMessage = L("Msg_ChoosePdfFirst"); return; }

            string baseName = string.IsNullOrEmpty(SourcePath) ? "apex" : Path.GetFileNameWithoutExtension(SourcePath);
            var target = _dialogs.SaveFile(L("Msg_SaveOutput"), PdfFilter, $"{baseName}-part.pdf");
            if (target == null) return;
            try
            {
                var parts = _tools.SplitEvery(doc, SplitEveryN);
                string dir = Path.GetDirectoryName(target) ?? ".";
                string bn = Path.GetFileNameWithoutExtension(target);
                for (int i = 0; i < parts.Count; i++)
                    File.WriteAllBytes(Path.Combine(dir, $"{bn}-{i + 1:D2}.pdf"), parts[i]);
                HasError = false;
                StatusMessage = Lf("PT_SplitDone", parts.Count);
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Msg_SaveFailed", ex.Message); }
        }

        /// <summary>Overlays a second PDF (logo / "DRAFT" watermark) on every page.</summary>
        [RelayCommand]
        private async Task StampAsync()
        {
            if (_working == null && _source == null)
            { HasError = true; ErrorMessage = L("Msg_ChoosePdfFirst"); return; }

            var stampPath = _dialogs.OpenFile(L("PT_StampPick"), PdfFilter);
            if (stampPath == null) return;
            byte[] stamp = File.ReadAllBytes(stampPath);

            await RunAsync("stamp", b => _tools.StampPdf(b, stamp, StampXmm, StampYmm,
                StampWidthMm > 0 ? StampWidthMm : (double?)null,
                StampHeightMm > 0 ? StampHeightMm : (double?)null));
        }

        [RelayCommand]
        private async Task SampleAsync()
        {
            int pages = SamplePages; double w = SampleWidthMm, h = SampleHeightMm;
            IsBusy = true; HasError = false; StatusMessage = L("Msg_CreatingDoc");
            try
            {
                var sample = await Task.Run(() => _tools.GenerateSampleDocument(pages, w, h));

                // The sample becomes the loaded document, not just a preview.
                //
                // It used to be assigned to _result only, so every tool then refused
                // with "choose a PDF first" and the source panel still read "pages: 0".
                // The one feature whose stated purpose is trying the tools out could
                // not be used to try them out.
                _source = sample;
                _working = sample;
                SourcePath = "";                 // generated, so exports fall back to "apex-…"
                PageCount = _tools.GetPageCount(sample);
                HasSource = true;
                _undo.Clear(); _redo.Clear();
                _steps = new System.Collections.Generic.List<string> { "sample" };
                RefreshHistory();
                StatusMessage = L("Msg_SampleCreated");
                await ShowPreviewAsync(sample);
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Msg_CreateFailed", ex.Message); }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void SaveResult()
        {
            if (_working == null || _steps.Count == 0) { HasError = true; ErrorMessage = L("Msg_RunFirst"); return; }
            string baseName = string.IsNullOrEmpty(SourcePath) ? "apex" : Path.GetFileNameWithoutExtension(SourcePath);
            var target = _dialogs.SaveFile(L("Msg_SaveOutput"), PdfFilter, $"{baseName}-{CurrentSuffix()}.pdf");
            if (target == null) return;
            try
            {
                File.WriteAllBytes(target, _working);
                HasError = false;
                StatusMessage = Lf("Msg_Saved", Path.GetFileName(target));
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Msg_SaveFailed", ex.Message); }
        }

        // ── Undo / Redo / Reset ──────────────────────────────────────────────
        [RelayCommand]
        private async Task Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Push(Capture());
            var snap = _undo.Pop();
            _working = snap.Doc;
            _steps = snap.Steps;
            RefreshHistory();
            StatusMessage = L("Msg_Undone");
            if (_working != null) await ShowPreviewAsync(_working);
        }

        [RelayCommand]
        private async Task Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Push(Capture());
            var snap = _redo.Pop();
            _working = snap.Doc;
            _steps = snap.Steps;
            RefreshHistory();
            StatusMessage = L("Msg_Redone");
            if (_working != null) await ShowPreviewAsync(_working);
        }

        /// <summary>Discards the whole pipeline and returns to the original document.</summary>
        [RelayCommand]
        private async Task ResetPipeline()
        {
            if (_source == null || _steps.Count == 0) return;
            _undo.Push(Capture());
            _redo.Clear();
            _working = _source;
            _steps = new System.Collections.Generic.List<string>();
            RefreshHistory();
            StatusMessage = L("Msg_PipelineReset");
            await ShowPreviewAsync(_source);
        }

        // ── Shared runner: guard → background op → preview (keep result in memory) ──
        private async Task RunAsync(string suffix, Func<byte[], byte[]> op)
        {
            if (_source == null || !HasSource)
            {
                HasError = true;
                ErrorMessage = L("Msg_ChoosePdfFirst");
                return;
            }

            // Chain on the current working document (the result of earlier operations),
            // not the original source — this is what lets tools stack.
            var input = _working ?? _source;
            var before = Capture();
            IsBusy = true; HasError = false; StatusMessage = L("Msg_Processing");
            try
            {
                var output = await Task.Run(() => op(input!));
                _undo.Push(before);   // a new action clears the redo branch
                _redo.Clear();
                _working = output;
                _steps.Add(suffix);
                RefreshHistory();
                StatusMessage = L("Msg_OpDone");
                await ShowPreviewAsync(output);
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = Lf("Msg_OpFailed", ex.Message);
            }
            finally { IsBusy = false; }
        }

        private async Task ShowPreviewAsync(byte[] pdf)
        {
            _previewPdf = pdf;
            try { PreviewPageCount = _tools.GetPageCount(pdf); } catch { PreviewPageCount = 0; }
            CurrentPage = 0;
            await RenderCurrentPageAsync();
        }

        private int _previewRequestId;

        private async Task RenderCurrentPageAsync()
        {
            if (_previewPdf == null) { PreviewImage = null; return; }
            var pdf = _previewPdf;
            int page = CurrentPage;
            int reqId = ++_previewRequestId;   // supersede any in-flight render
            IsPreviewLoading = true;
            try
            {
                byte[] png = await Task.Run(() => _tools.RenderPagePng(pdf, page, 110));
                if (reqId != _previewRequestId) return;   // dropped: a newer request started
                PreviewImage = LoadFrozenBitmap(png);
            }
            catch { if (reqId == _previewRequestId) PreviewImage = null; }
            finally { if (reqId == _previewRequestId) IsPreviewLoading = false; }
        }

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
    }
}
