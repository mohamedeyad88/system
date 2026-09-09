using SkiaSharp;
using System;
using System.Collections.Concurrent;
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
        // File-based debug logging for tray issues
        private static readonly string _trayLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ApexPrintingSystem", "tray_debug.log");

        private static void LogTray(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(_trayLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var logLine = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                File.AppendAllText(_trayLogPath, logLine + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine(logLine);
            }
            catch { }
        }

        /// <summary>
        /// Detects the DPI of an image based on its pixel dimensions
        /// by matching against common page sizes (A4, A5, Letter, etc.)
        /// </summary>
        private static float DetectImageDpi(int widthPx, int heightPx)
        {
            // Common page sizes in inches (width x height, portrait)
            var pageSizes = new[]
            {
                (8.27f, 11.69f, "A4"),      // A4
                (5.83f, 8.27f, "A5"),       // A5
                (4.13f, 5.83f, "A6"),       // A6
                (8.5f, 11f, "Letter"),      // US Letter
                (8.5f, 14f, "Legal"),       // US Legal
            };

            // Common DPI values to check
            var dpiValues = new[] { 72f, 96f, 150f, 200f, 300f, 600f };

            float bestDpi = 72f; // Default to screen DPI
            float bestMatch = float.MaxValue;

            foreach (var dpi in dpiValues)
            {
                float widthInches = widthPx / dpi;
                float heightInches = heightPx / dpi;

                foreach (var (pageW, pageH, name) in pageSizes)
                {
                    // Check both portrait and landscape
                    float diffPortrait = Math.Abs(widthInches - pageW) + Math.Abs(heightInches - pageH);
                    float diffLandscape = Math.Abs(widthInches - pageH) + Math.Abs(heightInches - pageW);
                    float diff = Math.Min(diffPortrait, diffLandscape);

                    if (diff < bestMatch)
                    {
                        bestMatch = diff;
                        bestDpi = dpi;
                    }
                }
            }

            // If match is too poor (> 0.5 inch off), use a conservative default
            if (bestMatch > 0.5f)
            {
                // Calculate DPI that would make the image fit A4
                float dpiForA4Width = widthPx / 8.27f;
                float dpiForA4Height = heightPx / 11.69f;
                bestDpi = (dpiForA4Width + dpiForA4Height) / 2f;
                LogTray($"DetectDPI: Poor match ({bestMatch:F2}), calculated DPI for A4: {bestDpi:F0}");
            }
            else
            {
                LogTray($"DetectDPI: Best match at {bestDpi} DPI (diff={bestMatch:F3})");
            }

            return bestDpi;
        }
        private PrintDocument? _printDocument;
        private PageSettings? _jobPageSettings;
        private PaperSize? _defaultPaperSize;
        private bool _defaultLandscape;
        private PrintJobSettings _settings = new();
        private CancellationToken _ct;
        private ManualResetEventSlim _pauseEvent = new(true);

        // ═══════════════════════════════════════════════════════════════════
        // CRITICAL FIX: Use a queue to hold all pages for printing
        // Each entry stores (Image, CopyIndex) to ensure correct tray selection
        // PrintDocument.Print() will process all pages from the queue
        // ═══════════════════════════════════════════════════════════════════
        private readonly ConcurrentQueue<PageQueueEntry> _pageQueue = new();
        private readonly SemaphoreSlim _pageQueueSemaphore = new(0);
        private PageQueueEntry? _currentPage;

        // The sheet artwork, prepared ONCE per job. Overlay pages reuse it instead of each
        // building — and PNG round-tripping — a full-page composite of their own.
        private Bitmap? _sharedBackground;

        /// <summary>
        /// One page waiting to print: either a fully composed raster (the original path,
        /// still used for barcodes and Arabic labels) or the shared artwork plus the text
        /// to draw over it.
        /// </summary>
        private sealed class PageQueueEntry
        {
            public SKImage? Image;
            public IReadOnlyList<TextOverlay>? Overlays;
            public int CopyIndex;
        }
        private int _currentPageCopyIndex = 0; // Copy index for the currently processing page
        private TaskCompletionSource<bool>? _pageCompletionSource;
        private readonly Stopwatch _stopwatch = new();
        private long _pagesProcessed;
        private long _totalPagesQueued = 0;
        private bool _jobEnded;
        private bool _printStarted = false;
        private Task? _printTask;
        private Exception? _printException;

        public PrintJobStatus Status { get; private set; } = new();

        public event EventHandler<PrintJobStatus>? StatusChanged;

        private int _currentCopyIndex = 0;

        /// <summary>
        /// CRITICAL: Complete state reset for each new job.
        /// Prevents state leakage between jobs that causes deadlocks and incorrect behavior.
        /// </summary>
        private void ResetJobState()
        {
            // Clear page queue completely
            while (_pageQueue.TryDequeue(out var oldEntry))
            {
                oldEntry.Image?.Dispose();
            }

            // Reset all counters
            _pagesProcessed = 0;
            _totalPagesQueued = 0;
            _currentCopyIndex = 0;
            _jobEnded = false;
            _printStarted = false;
            _printTask = null;
            _printException = null;
            _currentPage = null;
            _pageCompletionSource = null;

            _sharedBackground?.Dispose();
            _sharedBackground = null;
            _loggedRenderPath = false;

            // Reset pause event to allow printing
            _pauseEvent.Set();

            // Dispose previous print document if any
            if (_printDocument != null)
            {
                _printDocument.QueryPageSettings -= PrintDocument_QueryPageSettings;
                _printDocument.PrintPage -= PrintDocument_PrintPage;
                _printDocument.Dispose();
                _printDocument = null;
            }

            System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] JOB STATE RESET - All counters and queues cleared");
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");
        }

        public Task StartJobAsync(PrintJobSettings settings, CancellationToken ct)
        {
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL: Reset ALL state before starting new job
            // This prevents deadlocks and state leakage from previous jobs
            // ═══════════════════════════════════════════════════════════════════
            ResetJobState();

            _settings = settings;
            _ct = ct;
            _stopwatch.Restart();

            Status = new PrintJobStatus
            {
                Status = "Starting",
                CurrentPage = 0,
                TotalPages = 0
            };
            OnStatusChanged();

            System.Diagnostics.Debug.WriteLine($"[NUMBERING] Job Start - Printer: {settings.PrinterName}, DPI: {settings.Dpi}, Copies: {settings.Copies}");

            _printDocument = new PrintDocument();
            _printDocument.PrinterSettings.PrinterName = settings.PrinterName;
            _printDocument.PrinterSettings.Copies = (short)settings.Copies;
            _printDocument.PrinterSettings.Collate = settings.Collate;

            // Print-to-file virtual printers (Microsoft Print to PDF / XPS) silently
            // drop jobs that carry no output file name — the numbering run would
            // report success with no file (same QA-measured failure as quick print).
            // Supply an auto-derived output path on the user's desktop.
            var printerLower = settings.PrinterName?.ToLowerInvariant() ?? "";
            if (printerLower.Contains("microsoft print to pdf") || printerLower.Contains("xps"))
            {
                string ext = printerLower.Contains("xps") ? ".oxps" : ".pdf";
                string outDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string outFile = System.IO.Path.Combine(
                    outDir, $"numbering-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");
                _printDocument.PrinterSettings.PrintToFile = true;
                _printDocument.PrinterSettings.PrintFileName = outFile;
                System.Diagnostics.Debug.WriteLine(
                    $"[NUMBERING] Virtual printer → PrintToFile: {outFile}");
            }
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Ensure minimum 300 DPI to match file quality - no quality reduction
            // ═══════════════════════════════════════════════════════════════════
            int printDpi = Math.Max(settings.Dpi, 300);  // Ensure minimum 300 DPI
            _defaultPaperSize = _printDocument.DefaultPageSettings.PaperSize;
            _defaultLandscape = _printDocument.DefaultPageSettings.Landscape;

            _jobPageSettings = (PageSettings)_printDocument.DefaultPageSettings.Clone();
            _jobPageSettings.PrinterResolution = new PrinterResolution
            {
                Kind = PrinterResolutionKind.Custom,
                X = printDpi,
                Y = printDpi
            };
            if (_defaultPaperSize != null)
            {
                _jobPageSettings.PaperSize = _defaultPaperSize;
            }
            _jobPageSettings.Landscape = _defaultLandscape;

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Use QueryPageSettings to set PaperSource BEFORE PrintPage
            // This ensures Windows reads the correct tray before rendering the page
            // ═══════════════════════════════════════════════════════════════════
            _printDocument.QueryPageSettings += PrintDocument_QueryPageSettings;
            _printDocument.PrintPage += PrintDocument_PrintPage;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sets the current copy index for tray routing (0 = Original, 1 = Copy 1, etc.)
        /// </summary>
        public void SetCurrentCopyIndex(int copyIndex)
        {
            _currentCopyIndex = copyIndex;
        }

        /// <summary>
        /// Prepares the sheet artwork once for the whole job, so overlay pages can be queued
        /// as a handful of numbers instead of a full-page bitmap each. Safe to call with the
        /// same template repeatedly; the previous copy is released.
        /// </summary>
        public void SetSharedBackground(SKImage template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            _sharedBackground?.Dispose();
            _sharedBackground = null;

            try
            {
                using var data = template.Encode(SKEncodedImageFormat.Png, 100);
                if (data == null) return;
                using var stream = new MemoryStream();
                data.SaveTo(stream);
                stream.Position = 0;
                _sharedBackground = new Bitmap(stream);
                LogTray($"Shared background prepared: {_sharedBackground.Width}x{_sharedBackground.Height}");
            }
            catch (Exception ex)
            {
                // Not fatal: without a shared background every page simply takes the
                // original composed-raster path, which is slower but still correct.
                _sharedBackground = null;
                LogTray($"⚠️ SetSharedBackground failed, falling back to composed pages: {ex.Message}");
            }
        }

        /// <summary>True when <see cref="PrintPageAsync(IReadOnlyList{TextOverlay})"/> can be used.</summary>
        public bool HasSharedBackground => _sharedBackground != null;

        /// <summary>
        /// Queues a page as the shared artwork plus live text. No per-page surface, no
        /// per-page PNG round-trip, and the number is drawn by the printer at its own
        /// resolution rather than as 300-dpi pixels.
        /// </summary>
        public Task PrintPageAsync(IReadOnlyList<TextOverlay> overlays)
        {
            if (overlays == null) throw new ArgumentNullException(nameof(overlays));
            if (_sharedBackground == null)
                throw new InvalidOperationException("SetSharedBackground must be called before queueing overlay pages.");

            // Say once, in the log, which way this job is actually drawing. A silent
            // fallback to the slow path is exactly the kind of thing that hides for months.
            if (!_loggedRenderPath)
            {
                _loggedRenderPath = true;
                LogTray("Render path: DEVICE TEXT over shared sheet");
            }

            return EnqueueAsync(new PageQueueEntry { Overlays = overlays, CopyIndex = _currentCopyIndex });
        }

        public Task PrintPageAsync(SKImage page)
        {
            if (!_loggedRenderPath)
            {
                _loggedRenderPath = true;
                LogTray("Render path: COMPOSED RASTER per page");
            }

            return EnqueueAsync(new PageQueueEntry { Image = page, CopyIndex = _currentCopyIndex });
        }

        private bool _loggedRenderPath;

        private async Task EnqueueAsync(PageQueueEntry entry)
        {
            if (_ct.IsCancellationRequested || Status.IsCancelled)
            {
                throw new OperationCanceledException();
            }

            // Wait if paused
            _pauseEvent.Wait(_ct);

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Queue the page WITH its copy index
            // This ensures correct tray selection when the page is actually printed
            // ═══════════════════════════════════════════════════════════════════
            _pageQueue.Enqueue(entry);
            Interlocked.Increment(ref _totalPagesQueued);

            // #region agent log
            System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService] Page queued with CopyIndex={_currentCopyIndex}. Total queued: {_totalPagesQueued}, Pages processed: {_pagesProcessed}");
            // #endregion

            // Start printing if this is the first page
            if (!_printStarted && _printDocument != null)
            {
                _printStarted = true;
                _printTask = Task.Run(() =>
                {
                    try
                    {
                        // #region agent log
                        System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService] Starting PrintDocument.Print() - will process all queued pages");
                        // #endregion

                        // ═══════════════════════════════════════════════════════════════════
                        // CRITICAL FIX: PrintDocument.Print() will call PrintPage event
                        // for each page as long as HasMorePages is true
                        // The PrintPage handler will dequeue pages from _pageQueue
                        // ═══════════════════════════════════════════════════════════════════
                        _printDocument.Print();

                        // #region agent log
                        System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService] PrintDocument.Print() completed. Pages processed: {_pagesProcessed}, Total queued: {_totalPagesQueued}");
                        // #endregion
                    }
                    catch (Exception ex)
                    {
                        _printException = ex;
                        Status.Error = ex.Message;
                        Status.Status = "Error";
                        OnStatusChanged();

                        // #region agent log
                        System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService] ❌ PrintDocument.Print() failed: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService] Stack trace: {ex.StackTrace}");
                        // #endregion

                        // Signal any waiting pages
                        _pageCompletionSource?.TrySetException(ex);
                    }
                });
            }

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Non-blocking wait - don't block producer thread
            // The PrintDocument will process pages from queue asynchronously
            // We only need to ensure the page is queued, not wait for processing
            // ═══════════════════════════════════════════════════════════════════

            // Brief wait to allow queue processing to start (non-blocking for producer)
            int waitAttempts = 0;
            const int maxWaitAttempts = 500; // Max 5 seconds check, but don't block

            // Check for errors periodically, but don't block on page completion
            while (waitAttempts < maxWaitAttempts && !_jobEnded && _printException == null)
            {
                // If print task completed or failed, stop waiting
                if (_printTask != null && _printTask.IsCompleted)
                {
                    break;
                }

                // Check if we've processed at least some pages (printing is working)
                if (_pagesProcessed >= _totalPagesQueued - 10)
                {
                    // We're keeping up, no need to wait
                    break;
                }

                await Task.Delay(10, _ct);
                waitAttempts++;
            }

            // ═══════════════════════════════════════════════════════════════════
            // FAIL-FAST: If print exception occurred, throw immediately
            // ═══════════════════════════════════════════════════════════════════
            if (_printException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ❌ Print exception detected: {_printException.Message}");
                throw _printException;
            }
        }

        /// <summary>
        /// QueryPageSettings event handler - called BEFORE PrintPage for each page.
        /// This is the CORRECT place to set PaperSource for multi-tray printing.
        /// </summary>
        private void PrintDocument_QueryPageSettings(object sender, QueryPageSettingsEventArgs e)
        {
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Dequeue the page HERE in QueryPageSettings (called first)
            // so we have the correct copy index for tray selection.
            // PrintPage will then use the already-dequeued _currentPage.
            // ═══════════════════════════════════════════════════════════════════
            if (_currentPage == null) // Only dequeue if we don't already have a page
            {
                int waitAttempts = 0;
                const int maxWaitAttempts = 3000;

                PageQueueEntry? pageEntry = null;
                while (!_pageQueue.TryDequeue(out pageEntry) && !_jobEnded && waitAttempts < maxWaitAttempts)
                {
                    Thread.Sleep(10);
                    waitAttempts++;
                }

                _currentPage = pageEntry;
                _currentPageCopyIndex = pageEntry?.CopyIndex ?? 0;

                if (_currentPage != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] QueryPageSettings: Dequeued page with CopyIndex={_currentPageCopyIndex}");
                }
            }

            // Start from the job-scoped copy to avoid mutating printer defaults
            if (_jobPageSettings != null)
            {
                e.PageSettings = (PageSettings)_jobPageSettings.Clone();
                if (_defaultPaperSize != null)
                {
                    e.PageSettings.PaperSize = _defaultPaperSize;
                }
                e.PageSettings.Landscape = _defaultLandscape;
            }

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Set PaperSource in QueryPageSettings using the
            // copy index that was stored WITH the page when it was queued.
            // This ensures correct tray selection even with async queuing.
            // ═══════════════════════════════════════════════════════════════════
            LogTray($"QueryPageSettings: CopyIndex={_currentPageCopyIndex}, HasTrayMapping={_settings.CopyTrayMapping != null}, TrayMappingCount={_settings.CopyTrayMapping?.Count ?? 0}");

            if (_settings.CopyTrayMapping != null && _settings.CopyTrayMapping.Count > 0)
            {
                // Log all tray mappings
                foreach (var mapping in _settings.CopyTrayMapping)
                {
                    LogTray($"  TrayMapping[copy {mapping.Key}] = RawKind {mapping.Value}");
                }
            }

            if (_settings.CopyTrayMapping != null &&
                _settings.CopyTrayMapping.TryGetValue(_currentPageCopyIndex, out var trayRawKind))
            {
                try
                {
                    var paperSources = _printDocument?.PrinterSettings.PaperSources;
                    if (paperSources != null && paperSources.Count > 0)
                    {
                        LogTray($"QueryPageSettings: Looking for RawKind={trayRawKind} among {paperSources.Count} sources");
                        for (int i = 0; i < paperSources.Count; i++)
                        {
                            LogTray($"  Printer Source[{i}]: RawKind={paperSources[i].RawKind}, Kind={paperSources[i].Kind}, Name='{paperSources[i].SourceName}'");
                        }

                        // Exact match on the printer's own source id.
                        //
                        // This replaces three stacked guessing strategies — match by Kind,
                        // then classify drawers by scanning their names for "tray"/"درج"/
                        // "manual", then fall back to using the enum's numeric value as an
                        // index. They existed because the mapping carried a PaperSourceKind,
                        // and Windows reports most vendor drawers as Custom: every drawer
                        // looked alike, so the first one won and every copy came off the
                        // same paper. RawKind identifies a drawer exactly, so there is
                        // nothing left to guess at.
                        PaperSource? chosen = null;
                        for (int i = 0; i < paperSources.Count; i++)
                        {
                            if (paperSources[i].RawKind == trayRawKind)
                            {
                                chosen = paperSources[i];
                                break;
                            }
                        }

                        if (chosen != null)
                        {
                            e.PageSettings.PaperSource = chosen;
                            LogTray($"✅ TRAY SELECTED: '{chosen.SourceName}' (RawKind={chosen.RawKind}) for CopyIndex={_currentPageCopyIndex}");
                        }
                        else
                        {
                            // The saved job names a drawer this printer does not have —
                            // say which, rather than silently printing on the default.
                            LogTray($"❌ TRAY NOT FOUND: no source with RawKind={trayRawKind} on '{_settings.PrinterName}' - using printer default");
                        }
                    }
                    else
                    {
                        LogTray($"⚠️ QueryPageSettings: PaperSources is null or empty!");
                    }
                }
                catch (Exception ex)
                {
                    // If tray selection fails, continue with default
                    LogTray($"❌ QueryPageSettings Error: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.QueryPageSettings] No tray mapping configured (CopyIndex={_currentCopyIndex}, Mapping={(_settings.CopyTrayMapping != null ? "exists" : "null")})");
            }
        }

        private void PrintDocument_PrintPage(object sender, PrintPageEventArgs e)
        {
            // ═══════════════════════════════════════════════════════════════════
            // Page was already dequeued in QueryPageSettings (called before PrintPage)
            // _currentPage and _currentPageCopyIndex are already set
            // ═══════════════════════════════════════════════════════════════════

            // ═══════════════════════════════════════════════════════════════════
            // FAIL-FAST: If no page available (QueryPageSettings couldn't get one), end gracefully
            // ═══════════════════════════════════════════════════════════════════
            if (_currentPage == null)
            {
                e.HasMorePages = false;
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] PrintPage - No page available. JobEnded: {_jobEnded}, Processed: {_pagesProcessed}/{_totalPagesQueued}");
                return;
            }

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Even if _jobEnded is true, continue processing if
            // there are still pages in the queue. Only stop when queue is empty.
            // ═══════════════════════════════════════════════════════════════════
            if (_jobEnded && _pageQueue.IsEmpty)
            {
                e.HasMorePages = false;
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] PrintPage - Job ended and queue empty. Processed: {_pagesProcessed}/{_totalPagesQueued}");
                return;
            }

            try
            {
                // #region agent log
                System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.PrintPage] Processing page {_pagesProcessed + 1}. Remaining in queue: {_pageQueue.Count}");
                // #endregion

                // An overlay page reuses the job's shared artwork; a composed page carries
                // its own raster and is converted here. `ownsBitmap` decides which one gets
                // released at the end — releasing the shared one would kill the whole job.
                Bitmap bitmap;
                bool ownsBitmap;

                if (_currentPage.Overlays != null && _sharedBackground != null)
                {
                    bitmap = _sharedBackground;
                    ownsBitmap = false;
                }
                else if (_currentPage.Image != null)
                {
                    using var data = _currentPage.Image.Encode(SKEncodedImageFormat.Png, 100);
                    using var stream = new MemoryStream();
                    data.SaveTo(stream);
                    stream.Position = 0;
                    bitmap = new Bitmap(stream);
                    ownsBitmap = true;
                }
                else
                {
                    LogTray("❌ Page entry carries neither a composed image nor overlays");
                    e.HasMorePages = false;
                    return;
                }

                // ═══════════════════════════════════════════════════════════════════
                // DEFINITIVE FIX: 1:1 Physical Size Printing
                // ═══════════════════════════════════════════════════════════════════
                // Rules:
                // 1. Use GraphicsUnit.Inch for all coordinates
                // 2. Use PageBounds only (not MarginBounds)
                // 3. Calculate physical size in inches from image pixels
                // 4. Draw at exact physical size - NO scaling
                // 5. Works for A4, A3, A3+, Letter, Legal, Custom
                // ═══════════════════════════════════════════════════════════════════

                if (e.Graphics == null)
                {
                    LogTray("❌ Graphics is null - cannot print");
                    e.HasMorePages = false;
                    return;
                }

                var imageSize = bitmap.Size;

                // ═══════════════════════════════════════════════════════════════════
                // STEP 1: Detect the actual DPI of the image
                // ═══════════════════════════════════════════════════════════════════
                float detectedDpi = DetectImageDpi(imageSize.Width, imageSize.Height);

                // ═══════════════════════════════════════════════════════════════════
                // STEP 2: Calculate physical size in INCHES
                // This is the REAL physical size of the document
                // ═══════════════════════════════════════════════════════════════════
                float imageWidthInches = imageSize.Width / detectedDpi;
                float imageHeightInches = imageSize.Height / detectedDpi;

                // ═══════════════════════════════════════════════════════════════════
                // STEP 3: Set Graphics unit to INCHES
                // All subsequent coordinates will be in inches
                // ═══════════════════════════════════════════════════════════════════
                e.Graphics.PageUnit = GraphicsUnit.Inch;

                // ═══════════════════════════════════════════════════════════════════
                // STEP 4: Set high-quality rendering
                // ═══════════════════════════════════════════════════════════════════
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                e.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                // ═══════════════════════════════════════════════════════════════════
                // STEP 5: Get page size in inches (PageBounds after setting PageUnit)
                // ═══════════════════════════════════════════════════════════════════
                // Note: After setting PageUnit to Inch, PageBounds returns size in inches
                // For A4: approximately 8.27 x 11.69 inches
                float pageWidthInches = e.PageBounds.Width / 100f;  // PageBounds is in 1/100 inch before transform
                float pageHeightInches = e.PageBounds.Height / 100f;

                // ═══════════════════════════════════════════════════════════════════
                // STEP 6: Draw at origin (0,0) with exact physical size
                // NO scaling, NO centering offsets that could cause issues
                // The image fills the page at its true physical size
                // ═══════════════════════════════════════════════════════════════════

                LogTray($"═══════════════════════════════════════════════════════════");
                LogTray($"1:1 PHYSICAL PRINT:");
                LogTray($"  Image: {imageSize.Width}x{imageSize.Height} pixels");
                LogTray($"  Detected DPI: {detectedDpi}");
                LogTray($"  Physical Size: {imageWidthInches:F2} x {imageHeightInches:F2} inches");
                LogTray($"  Page Size: {pageWidthInches:F2} x {pageHeightInches:F2} inches");
                LogTray($"  Drawing at: (0, 0) with size ({imageWidthInches:F2}, {imageHeightInches:F2}) inches");
                LogTray($"═══════════════════════════════════════════════════════════");

                // Draw the image at exact physical size in inches
                // RectangleF uses float for precise inch measurements
                e.Graphics.DrawImage(
                    bitmap,
                    new RectangleF(0f, 0f, imageWidthInches, imageHeightInches),  // Destination in inches
                    new RectangleF(0f, 0f, imageSize.Width, imageSize.Height),    // Source in pixels
                    GraphicsUnit.Pixel  // Source unit is pixels
                );

                // Numbers last, drawn as real type on the device rather than baked pixels.
                if (_currentPage.Overlays != null)
                {
                    DrawTextOverlays(e.Graphics, _currentPage.Overlays,
                                     imageSize.Width, imageWidthInches, imageHeightInches);
                }

                if (ownsBitmap) bitmap.Dispose();

                // Update page count
                _pagesProcessed++;
                Status.CurrentPage = _pagesProcessed;
                Status.PagesPerSecond = _pagesProcessed / Math.Max(_stopwatch.Elapsed.TotalSeconds, 0.001);
                Status.ElapsedTime = _stopwatch.Elapsed;
                Status.Status = "Printing";
                OnStatusChanged();

                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL FIX: Check if there are more pages in the queue OR if more
                // pages are expected (not all pages have been queued yet)
                // HasMorePages must be true for PrintDocument to continue printing
                // ═══════════════════════════════════════════════════════════════════
                // Continue printing if:
                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL FIX: HasMorePages should continue as long as:
                // 1. There are pages in the queue, OR
                // 2. We haven't received all expected pages yet (pages still being queued)
                // 3. EVEN IF _jobEnded is true (because EndJobAsync waits for processing)
                // 
                // Only stop when: queue is empty AND all expected pages are processed
                // ═══════════════════════════════════════════════════════════════════
                bool hasMoreInQueue = !_pageQueue.IsEmpty;
                bool expectingMorePages = _pagesProcessed < _totalPagesQueued;

                // If job has ended, only continue if there are actually more pages to process
                if (_jobEnded)
                {
                    e.HasMorePages = hasMoreInQueue; // Continue only if queue has pages
                }
                else
                {
                    e.HasMorePages = hasMoreInQueue || expectingMorePages; // Continue if expecting more
                }

                // #region agent log
                System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.PrintPage] Page {_pagesProcessed} printed. HasMorePages: {e.HasMorePages}, QueueCount: {_pageQueue.Count}, ExpectingMore: {expectingMorePages}, TotalQueued: {_totalPagesQueued}, JobEnded: {_jobEnded}");
                // #endregion

                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL: Dispose page after printing to prevent memory leaks
                // The page was created in GdiSpoolPrinter and passed here for printing.
                // Overlay pages own nothing — their artwork is the job-wide background.
                // ═══════════════════════════════════════════════════════════════════
                _currentPage?.Image?.Dispose();
                _currentPage = null;
            }
            catch (Exception ex)
            {
                // ═══════════════════════════════════════════════════════════════════
                // Dispose page on error as well
                // ═══════════════════════════════════════════════════════════════════
                _currentPage?.Image?.Dispose();
                _currentPage = null;
                _printException = ex;
                Status.Error = ex.Message;
                Status.Status = "Error";
                OnStatusChanged();

                // #region agent log
                System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.PrintPage] ❌ Error printing page: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.PrintPage] Stack trace: {ex.StackTrace}");
                // #endregion

                e.HasMorePages = false;
            }
        }

        /// <summary>
        /// Draws each number onto the page with the printer's own text engine.
        ///
        /// <para>The Graphics is already in inches, so everything here is converted into
        /// inches and the driver renders at whatever resolution the press actually has —
        /// 600 or 1200&#160;dpi on a laser — instead of being resampled up from a 300&#160;dpi
        /// bitmap. That is the whole point: it is what stops the digits looking jagged
        /// beside the artwork.</para>
        ///
        /// <para>Sizing has to agree exactly with <c>Composer.ComputeDpiScale</c> and with
        /// the designer's SlotFontScaleConverter, or a job would print at a different size
        /// than the operator laid out. FontSize is in 96-dpi design units; multiply by the
        /// template's resolution scale to get template pixels, then divide by the template's
        /// dpi to get inches on paper.</para>
        /// </summary>
        private static void DrawTextOverlays(
            Graphics g,
            IReadOnlyList<TextOverlay> overlays,
            int backgroundWidthPx,
            float pageWidthInches,
            float pageHeightInches)
        {
            const float a4WidthInches = 8.27f;
            const float designDpi = 96f;

            if (backgroundWidthPx <= 0 || pageWidthInches <= 0) return;

            float dpiScale = Math.Clamp(backgroundWidthPx / (a4WidthInches * designDpi), 1f, 5f);
            float inchesPerTemplatePixel = pageWidthInches / backgroundWidthPx;

            foreach (var overlay in overlays)
            {
                if (string.IsNullOrEmpty(overlay.Text)) continue;

                float emInches = overlay.FontSize * dpiScale * inchesPerTemplatePixel;
                if (emInches <= 0) continue;

                Font? font = null;
                try
                {
                    try
                    {
                        font = new Font(overlay.FontFamily, emInches, FontStyle.Regular, GraphicsUnit.Inch);
                    }
                    catch
                    {
                        // A design may name a font this machine does not have; the sheet
                        // must still carry its number.
                        font = new Font(FontFamily.GenericSansSerif, emInches, FontStyle.Regular, GraphicsUnit.Inch);
                    }

                    var color = ParseColor(overlay.ColorHex, overlay.Opacity);
                    using var brush = new SolidBrush(color);

                    float x = overlay.X * pageWidthInches;
                    float y = overlay.Y * pageHeightInches;
                    float boxWidth = overlay.Width * pageWidthInches;
                    float boxHeight = overlay.Height * pageHeightInches;
                    if (boxWidth <= 0) boxWidth = pageWidthInches - x;
                    if (boxHeight <= 0) boxHeight = emInches * 2f;

                    using var format = new StringFormat(StringFormat.GenericTypographic)
                    {
                        // Top-anchored, matching the composer (which pins the patch's top
                        // edge to the slot) and the designer canvas.
                        LineAlignment = StringAlignment.Near,
                        Alignment = overlay.Align switch
                        {
                            Models.TextAlign.Center => StringAlignment.Center,
                            Models.TextAlign.Right => StringAlignment.Far,
                            _ => StringAlignment.Near
                        },
                        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
                    };

                    var state = g.Save();
                    try
                    {
                        if (Math.Abs(overlay.Rotation) > 0.01f)
                        {
                            float cx = x + boxWidth / 2f;
                            float cy = y + boxHeight / 2f;
                            g.TranslateTransform(cx, cy);
                            g.RotateTransform(overlay.Rotation);
                            g.TranslateTransform(-cx, -cy);
                        }

                        g.DrawString(overlay.Text, font, brush,
                                     new RectangleF(x, y, boxWidth, boxHeight), format);
                    }
                    finally
                    {
                        g.Restore(state);
                    }
                }
                finally
                {
                    font?.Dispose();
                }
            }
        }

        private static Color ParseColor(string? hex, float opacity)
        {
            var baseColor = Color.Black;
            if (!string.IsNullOrWhiteSpace(hex))
            {
                try { baseColor = ColorTranslator.FromHtml(hex); }
                catch { baseColor = Color.Black; }
            }

            int alpha = (int)Math.Round(Math.Clamp(opacity <= 0 ? 1f : opacity, 0f, 1f) * 255);
            return Color.FromArgb(alpha, baseColor);
        }

        public async Task EndJobAsync()
        {
            // #region agent log
            System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.EndJobAsync] Ending job. Pages processed: {_pagesProcessed}, Total queued: {_totalPagesQueued}, Queue remaining: {_pageQueue.Count}");
            // #endregion

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Wait for all queued pages to be processed BEFORE
            // setting _jobEnded = true. This ensures PrintDocument_PrintPage 
            // continues processing until all pages are printed.
            // ═══════════════════════════════════════════════════════════════════

            // Wait for print task to complete (all pages sent to Windows)
            if (_printTask != null)
            {
                // Wait for all queued pages to be processed before signaling end
                int maxWait = 600; // 60 seconds max wait
                int waited = 0;
                while (_pagesProcessed < _totalPagesQueued && waited < maxWait)
                {
                    await Task.Delay(100);
                    waited++;

                    if (waited % 50 == 0) // Log every 5 seconds
                    {
                        System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.EndJobAsync] Waiting for pages... Processed: {_pagesProcessed}/{_totalPagesQueued}");
                    }
                }

                // NOW signal job end
                _jobEnded = true;

                try
                {
                    await _printTask;

                    // #region agent log
                    System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.EndJobAsync] Print task completed. Final pages processed: {_pagesProcessed}");
                    // #endregion
                }
                catch (Exception ex)
                {
                    // #region agent log
                    System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.EndJobAsync] ❌ Print task failed: {ex.Message}");
                    // #endregion

                    Status.Error = ex.Message;
                    Status.Status = "Error";
                    OnStatusChanged();
                    throw; // Re-throw to indicate failure
                }
            }
            else
            {
                // No print task was started
                _jobEnded = true;
            }

            // Verify all pages were processed
            if (_pagesProcessed < _totalPagesQueued)
            {
                var errorMsg = $"Not all pages were printed. Expected: {_totalPagesQueued}, Printed: {_pagesProcessed}";
                Status.Error = errorMsg;
                Status.Status = "Error";
                OnStatusChanged();

                // #region agent log
                System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.EndJobAsync] ❌ {errorMsg}");
                // #endregion

                throw new InvalidOperationException(errorMsg);
            }

            _stopwatch.Stop();

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Only report success AFTER all pages are sent to Windows
            // ═══════════════════════════════════════════════════════════════════
            Status.Status = "Completed";
            Status.ElapsedTime = _stopwatch.Elapsed;
            OnStatusChanged();

            _printDocument?.Dispose();
            _printDocument = null;

            // #region agent log
            System.Diagnostics.Debug.WriteLine($"[WindowsPrintSpoolerService.EndJobAsync] ✅ Job completed successfully. Total pages: {_pagesProcessed}");
            // #endregion
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

            // The shared sheet is a full-page bitmap; a cancelled job should not leave one
            // resident until the next job happens to reset the state.
            _sharedBackground?.Dispose();
            _sharedBackground = null;

            OnStatusChanged();
        }

        private void OnStatusChanged()
        {
            StatusChanged?.Invoke(this, Status);
        }
    }
}
