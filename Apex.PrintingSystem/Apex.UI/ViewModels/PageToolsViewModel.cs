using Apex.Core.Interfaces;
using Apex.Services.Imposition;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
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
        private byte[]? _source;
        private byte[]? _result;
        private string _resultSuffix = "output";

        private static string PdfFilter => L("Msg_PdfFilter");

        [ObservableProperty] private string _sourcePath = "";
        [ObservableProperty] private bool _hasSource;
        [ObservableProperty] private int _pageCount;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _hasResult;
        [ObservableProperty] private string _statusMessage = "";
        [ObservableProperty] private bool _hasError;
        [ObservableProperty] private string _errorMessage = "";

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

        public PageToolsViewModel(PdfPageToolsService tools, IFileDialogService dialogs)
        {
            _tools = tools;
            _dialogs = dialogs;
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
                _result = null; HasResult = false;
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

        [RelayCommand]
        private async Task SampleAsync()
        {
            int pages = SamplePages; double w = SampleWidthMm, h = SampleHeightMm;
            IsBusy = true; HasError = false; StatusMessage = L("Msg_CreatingDoc");
            try
            {
                _result = await Task.Run(() => _tools.GenerateSampleDocument(pages, w, h));
                _resultSuffix = "sample";
                HasResult = true;
                StatusMessage = L("Msg_SampleCreated");
                await ShowPreviewAsync(_result);
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Msg_CreateFailed", ex.Message); }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void SaveResult()
        {
            if (_result == null) { HasError = true; ErrorMessage = L("Msg_RunFirst"); return; }
            string baseName = string.IsNullOrEmpty(SourcePath) ? "apex" : Path.GetFileNameWithoutExtension(SourcePath);
            var target = _dialogs.SaveFile(L("Msg_SaveOutput"), PdfFilter, $"{baseName}-{_resultSuffix}.pdf");
            if (target == null) return;
            try
            {
                File.WriteAllBytes(target, _result);
                HasError = false;
                StatusMessage = Lf("Msg_Saved", Path.GetFileName(target));
            }
            catch (Exception ex) { HasError = true; ErrorMessage = Lf("Msg_SaveFailed", ex.Message); }
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

            var input = _source;
            IsBusy = true; HasError = false; StatusMessage = L("Msg_Processing");
            try
            {
                _result = await Task.Run(() => op(input));
                _resultSuffix = suffix;
                HasResult = true;
                StatusMessage = L("Msg_OpDone");
                await ShowPreviewAsync(_result);
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
