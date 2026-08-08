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
        private readonly ConcurrentQueue<(SKImage Image, int CopyIndex)> _pageQueue = new();
        private readonly SemaphoreSlim _pageQueueSemaphore = new(0);
        private SKImage? _currentPage;
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

        public async Task PrintPageAsync(SKImage page)
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
            _pageQueue.Enqueue((page, _currentCopyIndex));
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

                (SKImage Image, int CopyIndex) pageEntry = default;
                while (!_pageQueue.TryDequeue(out pageEntry) && !_jobEnded && waitAttempts < maxWaitAttempts)
                {
                    Thread.Sleep(10);
                    waitAttempts++;
                }

                _currentPage = pageEntry.Image;
                _currentPageCopyIndex = pageEntry.CopyIndex;

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
                    LogTray($"  TrayMapping[{mapping.Key}] = {mapping.Value} ({(int)mapping.Value})");
                }
            }

            if (_settings.CopyTrayMapping != null &&
                _settings.CopyTrayMapping.TryGetValue(_currentPageCopyIndex, out var trayKind))
            {
                try
                {
                    // Find the matching paper source
                    var paperSources = _printDocument?.PrinterSettings.PaperSources;
                    if (paperSources != null && paperSources.Count > 0)
                    {
                        LogTray($"QueryPageSettings: Looking for TrayKind={trayKind} ({(int)trayKind}) among {paperSources.Count} sources");

                        // Log all available sources first
                        for (int i = 0; i < paperSources.Count; i++)
                        {
                            LogTray($"  Printer Source[{i}]: Kind={paperSources[i].Kind} ({(int)paperSources[i].Kind}), RawKind={paperSources[i].RawKind}, Name='{paperSources[i].SourceName}'");
                        }

                        bool trayFound = false;

                        // ═══════════════════════════════════════════════════════════════════
                        // STRATEGY 1: Try to match by PaperSourceKind
                        // ═══════════════════════════════════════════════════════════════════
                        for (int i = 0; i < paperSources.Count; i++)
                        {
                            if (paperSources[i].Kind == trayKind)
                            {
                                e.PageSettings.PaperSource = paperSources[i];
                                trayFound = true;
                                LogTray($"✅ TRAY SELECTED BY KIND: '{paperSources[i].SourceName}' for CopyIndex={_currentPageCopyIndex}");
                                break;
                            }
                        }

                        // ═══════════════════════════════════════════════════════════════════
                        // STRATEGY 2: For printers using Custom kind, use logical mapping:
                        // Upper (Tray 1) → First non-auto tray (usually index 1 or 2)
                        // Lower (Tray 2) → Second non-auto tray
                        // Manual → Look for manual/hand feed source
                        // ═══════════════════════════════════════════════════════════════════
                        if (!trayFound)
                        {
                            LogTray($"⚠️ TrayKind {trayKind} ({(int)trayKind}) NOT FOUND by Kind! Trying logical mapping...");

                            // Build list of non-auto trays
                            var manualTrays = new List<int>();
                            var casseteTrays = new List<int>();

                            for (int i = 0; i < paperSources.Count; i++)
                            {
                                var srcName = paperSources[i].SourceName.ToLower();
                                var rawKind = paperSources[i].RawKind;

                                // Detect manual feed trays (common patterns)
                                if (srcName.Contains("manual") || srcName.Contains("hand") ||
                                    srcName.Contains("يدو") || // Arabic "manual"
                                    rawKind == 4 || // Manual
                                    rawKind == 261 || rawKind == 262) // Common manual raw kinds
                                {
                                    manualTrays.Add(i);
                                }
                                // Detect cassette/regular trays
                                else if (srcName.Contains("tray") || srcName.Contains("cassette") ||
                                         srcName.Contains("درج") || // Arabic "tray"
                                         (paperSources[i].Kind == PaperSourceKind.Custom &&
                                          paperSources[i].Kind != PaperSourceKind.AutomaticFeed))
                                {
                                    casseteTrays.Add(i);
                                }
                            }

                            LogTray($"  Found manual trays: [{string.Join(",", manualTrays)}], cassette trays: [{string.Join(",", casseteTrays)}]");

                            int selectedIndex = -1;

                            switch (trayKind)
                            {
                                case PaperSourceKind.Upper: // Tray 1
                                    selectedIndex = casseteTrays.Count > 0 ? casseteTrays[0] : -1;
                                    break;
                                case PaperSourceKind.Lower: // Tray 2
                                    selectedIndex = casseteTrays.Count > 1 ? casseteTrays[1] : -1;
                                    break;
                                case PaperSourceKind.Middle: // Tray 3
                                    selectedIndex = casseteTrays.Count > 2 ? casseteTrays[2] : -1;
                                    break;
                                case PaperSourceKind.Manual: // Manual Feed
                                    selectedIndex = manualTrays.Count > 0 ? manualTrays[0] : -1;
                                    break;
                                default:
                                    // Try by index directly as last resort
                                    // Upper=1, Lower=2, Middle=3, Manual=4
                                    int kindValue = (int)trayKind;
                                    if (kindValue > 0 && kindValue < paperSources.Count)
                                    {
                                        selectedIndex = kindValue;
                                    }
                                    break;
                            }

                            if (selectedIndex >= 0 && selectedIndex < paperSources.Count)
                            {
                                e.PageSettings.PaperSource = paperSources[selectedIndex];
                                trayFound = true;
                                LogTray($"✅ TRAY SELECTED BY MAPPING: [{selectedIndex}] '{paperSources[selectedIndex].SourceName}' for CopyIndex={_currentPageCopyIndex}");
                            }
                        }

                        if (!trayFound)
                        {
                            LogTray($"❌ TRAY NOT FOUND - using printer default");
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

                // Convert SKImage to System.Drawing.Bitmap
                using var data = _currentPage.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream();
                data.SaveTo(stream);
                stream.Position = 0;

                using var bitmap = new Bitmap(stream);

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
                // The page was created in GdiSpoolPrinter and passed here for printing
                // ═══════════════════════════════════════════════════════════════════
                _currentPage?.Dispose();
                _currentPage = null;
            }
            catch (Exception ex)
            {
                // ═══════════════════════════════════════════════════════════════════
                // Dispose page on error as well
                // ═══════════════════════════════════════════════════════════════════
                _currentPage?.Dispose();
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
            OnStatusChanged();
        }

        private void OnStatusChanged()
        {
            StatusChanged?.Invoke(this, Status);
        }
    }
}
