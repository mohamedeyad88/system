using SkiaSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Direct printing service using System.Drawing.Printing.PrintDocument.
    /// Pages are sent directly to the Windows Print Spooler without intermediate files.
    /// </summary>
    public class WindowsPrintSpoolerService : IPrintOutputService
    {
        private PrintDocument? _printDocument;
        private PrintJobSettings _settings = new();
        private CancellationToken _ct;
        private ManualResetEventSlim _pauseEvent = new(true);
        
        private SKImage? _currentPage;
        private TaskCompletionSource<bool>? _pageCompletionSource;
        private readonly Stopwatch _stopwatch = new();
        private long _pagesProcessed;
        private bool _jobEnded;

        public PrintJobStatus Status { get; private set; } = new();

        public event EventHandler<PrintJobStatus>? StatusChanged;

        public Task StartJobAsync(PrintJobSettings settings, CancellationToken ct)
        {
            _settings = settings;
            _ct = ct;
            _pagesProcessed = 0;
            _jobEnded = false;
            _stopwatch.Restart();

            Status = new PrintJobStatus
            {
                Status = "Starting",
                CurrentPage = 0,
                TotalPages = 0
            };
            OnStatusChanged();

            _printDocument = new PrintDocument();
            _printDocument.PrinterSettings.PrinterName = settings.PrinterName;
            _printDocument.PrinterSettings.Copies = (short)settings.Copies;
            _printDocument.PrinterSettings.Collate = settings.Collate;
            _printDocument.DefaultPageSettings.PrinterResolution = new PrinterResolution
            {
                Kind = PrinterResolutionKind.Custom,
                X = settings.Dpi,
                Y = settings.Dpi
            };

            _printDocument.PrintPage += PrintDocument_PrintPage;

            return Task.CompletedTask;
        }

        public async Task PrintPageAsync(SKImage page)
        {
            if (_ct.IsCancellationRequested || Status.IsCancelled)
            {
                throw new OperationCanceledException();
            }

            // Wait if paused
            _pauseEvent.Wait(_ct);

            _currentPage = page;
            _pageCompletionSource = new TaskCompletionSource<bool>();

            // Start printing if this is the first page
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
                    }
                });
            }

            // Wait for page to be printed
            await _pageCompletionSource.Task;

            _pagesProcessed++;
            Status.CurrentPage = _pagesProcessed;
            Status.PagesPerSecond = _pagesProcessed / Math.Max(_stopwatch.Elapsed.TotalSeconds, 0.001);
            Status.ElapsedTime = _stopwatch.Elapsed;
            Status.Status = "Printing";
            OnStatusChanged();
        }

        private void PrintDocument_PrintPage(object sender, PrintPageEventArgs e)
        {
            if (_currentPage == null || _jobEnded)
            {
                e.HasMorePages = false;
                return;
            }

            try
            {
                // Convert SKImage to System.Drawing.Bitmap
                using var data = _currentPage.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream();
                data.SaveTo(stream);
                stream.Position = 0;

                using var bitmap = new Bitmap(stream);

                // Draw to print graphics
                e.Graphics?.DrawImage(bitmap, e.MarginBounds);

                // Signal page completion
                _pageCompletionSource?.TrySetResult(true);
                
                // Wait for next page or end
                Thread.Sleep(50); // Small delay to allow next page to be queued
                e.HasMorePages = !_jobEnded && _currentPage != null;
            }
            catch (Exception ex)
            {
                Status.Error = ex.Message;
                _pageCompletionSource?.TrySetException(ex);
            }
        }

        public Task EndJobAsync()
        {
            _jobEnded = true;
            _stopwatch.Stop();

            Status.Status = "Completed";
            Status.ElapsedTime = _stopwatch.Elapsed;
            OnStatusChanged();

            _printDocument?.Dispose();
            _printDocument = null;

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
            _pauseEvent.Set(); // Release any waiting
            _pageCompletionSource?.TrySetCanceled();
            OnStatusChanged();
        }

        private void OnStatusChanged()
        {
            StatusChanged?.Invoke(this, Status);
        }
    }
}
