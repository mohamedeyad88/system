using SkiaSharp;
using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// GDI-based spooler printer implementation.
    /// Uses "template once" strategy: caches template bitmap in memory,
    /// blits it per page, and draws text overlays using vector GDI.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class GdiSpoolPrinter : IPrintOutputService, IDisposable
    {
        private PrintDocument? _printDocument;
        private Bitmap? _cachedTemplateBitmap;
        private PagePrintCommand? _currentCommand;
        private TaskCompletionSource<bool>? _pageCompletion;
        private ManualResetEventSlim _pauseEvent = new(true);
        private CancellationToken _ct;
        private bool _jobEnded;
        private long _pagesProcessed;
        private readonly System.Diagnostics.Stopwatch _stopwatch = new();

        public PrintJobStatus Status { get; private set; } = new();
        public event EventHandler<PrintJobStatus>? StatusChanged;

        /// <summary>
        /// Sets the cached template bitmap to use for all pages.
        /// This is the "template once" optimization.
        /// </summary>
        public void SetCachedTemplate(SKImage templateImage)
        {
            // Convert SKImage to System.Drawing.Bitmap
            using var data = templateImage.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Position = 0;

            _cachedTemplateBitmap?.Dispose();
            _cachedTemplateBitmap = new Bitmap(stream);
        }

        /// <summary>
        /// Sets the cached template from a file path.
        /// </summary>
        public void SetCachedTemplate(string templatePath, int dpi = 300)
        {
            using var templateManager = new TemplateManager();
            var (image, _) = templateManager.RasterizeTemplate(templatePath, dpi);
            SetCachedTemplate(image);
        }

        public Task StartJobAsync(PrintJobSettings settings, CancellationToken ct)
        {
            _ct = ct;
            _pagesProcessed = 0;
            _jobEnded = false;
            _stopwatch.Restart();

            Status = new PrintJobStatus
            {
                Status = "Starting",
                CurrentPage = 0
            };
            OnStatusChanged();

            _printDocument = new PrintDocument();
            _printDocument.PrinterSettings.PrinterName = settings.PrinterName;
            _printDocument.PrinterSettings.Copies = (short)settings.Copies;
            _printDocument.PrintPage += PrintDocument_PrintPage;

            return Task.CompletedTask;
        }

        public async Task PrintPageAsync(SKImage page)
        {
            // This method is for compatibility; for streaming, use PrintPageWithOverlaysAsync
            await PrintPageWithOverlaysAsync(null);
        }

        /// <summary>
        /// Prints a page using the cached template and the given overlay command.
        /// This is the optimized streaming path.
        /// </summary>
        public async Task PrintPageWithOverlaysAsync(PagePrintCommand? command)
        {
            if (_ct.IsCancellationRequested || Status.IsCancelled)
                throw new OperationCanceledException();

            _pauseEvent.Wait(_ct);

            _currentCommand = command;
            _pageCompletion = new TaskCompletionSource<bool>();

            // Start printing if first page
            if (_pagesProcessed == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        _printDocument?.Print();
                    }
                    catch (Exception ex)
                    {
                        Status.Error = ex.Message;
                        Status.Status = "Error";
                        OnStatusChanged();
                        _pageCompletion?.TrySetException(ex);
                    }
                });
            }

            await _pageCompletion.Task;

            _pagesProcessed++;
            Status.CurrentPage = _pagesProcessed;
            Status.PagesPerSecond = _pagesProcessed / Math.Max(_stopwatch.Elapsed.TotalSeconds, 0.001);
            Status.ElapsedTime = _stopwatch.Elapsed;
            Status.Status = "Printing";
            OnStatusChanged();
        }

        private void PrintDocument_PrintPage(object sender, PrintPageEventArgs e)
        {
            if (_jobEnded || e.Graphics == null)
            {
                e.HasMorePages = false;
                return;
            }

            try
            {
                // Step 1: Blit the cached template (fast operation)
                if (_cachedTemplateBitmap != null)
                {
                    e.Graphics.DrawImage(_cachedTemplateBitmap, e.MarginBounds);
                }

                // Step 2: Draw text overlays (vector text, very efficient)
                if (_currentCommand != null)
                {
                    foreach (var slot in _currentCommand.Slots)
                    {
                        DrawSlotText(e.Graphics, slot, e.MarginBounds);
                    }
                }

                _pageCompletion?.TrySetResult(true);

                // Wait briefly for next page
                Thread.Sleep(10);
                e.HasMorePages = !_jobEnded;
            }
            catch (Exception ex)
            {
                _pageCompletion?.TrySetException(ex);
                e.HasMorePages = false;
            }
        }

        private readonly Dictionary<string, Font> _fontCache = new();

        private Font GetCachedFont(string family, float size, FontStyle style)
        {
            var key = $"{family}|{size}|{style}";
            if (!_fontCache.TryGetValue(key, out var font))
            {
                font = new Font(family, size, style, GraphicsUnit.Point);
                _fontCache[key] = font;
            }
            return font;
        }

        private void DrawSlotText(Graphics g, SlotOverlayCommand slot, Rectangle bounds)
        {
            // Parse color
            var color = ColorTranslator.FromHtml(slot.ColorHex.StartsWith("#") ? slot.ColorHex : $"#{slot.ColorHex}");

            // Use cached font (DO NOT dispose here)
            var font = GetCachedFont(slot.FontFamily, slot.FontSize, FontStyle.Regular);
            using var brush = new SolidBrush(color);

            // Calculate position relative to print bounds (using Normalized coordinates 0..1)
            // This works for any paper size (A4, Letter) and any DPI
            float x = bounds.Left + (slot.NormalizedX * bounds.Width);
            float y = bounds.Top + (slot.NormalizedY * bounds.Height);

            // Draw text (vector, not raster - very efficient)
            g.DrawString(slot.Text, font, brush, x, y);
        }

        public Task EndJobAsync()
        {
            _jobEnded = true;
            _stopwatch.Stop();

            Status.Status = "Completed";
            Status.ElapsedTime = _stopwatch.Elapsed;
            OnStatusChanged();

            return Task.CompletedTask;
        }

        public void Pause()
        {
            _pauseEvent.Reset();
            Status.IsPaused = true;
            Status.Status = "Paused";
            OnStatusChanged();
        }

        public void Resume()
        {
            _pauseEvent.Set();
            Status.IsPaused = false;
            Status.Status = "Printing";
            OnStatusChanged();
        }

        public void Cancel()
        {
            Status.IsCancelled = true;
            Status.Status = "Cancelled";
            _pauseEvent.Set();
            _pageCompletion?.TrySetCanceled();
            OnStatusChanged();
        }

        private void OnStatusChanged()
        {
            StatusChanged?.Invoke(this, Status);
        }

        public void Dispose()
        {
            _printDocument?.Dispose();
            _cachedTemplateBitmap?.Dispose();
            _pauseEvent.Dispose();
            
            foreach (var font in _fontCache.Values)
                font.Dispose();
            _fontCache.Clear();
        }
    }
}
