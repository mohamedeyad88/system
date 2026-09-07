using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Options for streaming numbered print job.
    /// </summary>
    public record NumberedPrintJobOptions(
        string PrinterName,
        string TemplatePath,
        int Dpi,
        long StartNumber,
        long TotalNumbers,
        int CopiesPerPage,
        IReadOnlyList<SlotSpec> Slots,
        bool UsePrinterStoredTemplate,
        bool LowResourceMode,
        int CheckpointEvery,
        IReadOnlyList<CopyType>? CopyTypes = null,
        Dictionary<int, System.Drawing.Printing.PaperSourceKind>? CopyTrayMapping = null,
        PrintScaleMode? ScaleMode = null,
        NumberingMode? NumberingMode = null,
        bool UseSmartPrinting = true,
        bool UseArabicDigits = false,  // true → Arabic-Indic numerals (٠١٢...)
        // Digit count / series prefix / suffix, e.g. INV-000123/2026.
        NumberFormatOptions? NumberFormat = null
    );

    [SupportedOSPlatform("windows")]
    public class JobOrchestrator
    {
        private readonly Composer _composer;
        private readonly TemplateManager _templateManager;
        private readonly PrinterCapabilityDetector _capabilityDetector;
        private readonly PrintCommandBuilder _commandBuilder;
        private readonly CheckpointManager _checkpointManager;

        public JobOrchestrator()
        {
            _composer = new Composer();
            _templateManager = new TemplateManager();
            _capabilityDetector = new PrinterCapabilityDetector();
            _commandBuilder = new PrintCommandBuilder();
            _checkpointManager = new CheckpointManager();
        }

        /// <summary>
        /// Hands the job's number format to every component that draws a number.
        ///
        /// There are two: the composer builds previews and PDF pages, the command
        /// builder feeds the streaming printer. Configuring one and not the other is
        /// how a job came to print NumberFormatOptions.Default instead of what the
        /// operator chose, so they are set together here and nowhere else.
        /// </summary>
        internal void ApplyNumberFormat(NumberFormatOptions? requested, bool useArabicDigits)
        {
            var format = (requested ?? NumberFormatOptions.Default) with
            {
                UseArabicDigits = useArabicDigits
            };

            _composer.UseArabicDigits = useArabicDigits;
            _composer.NumberFormat = format;
            _commandBuilder.NumberFormat = format;
        }

        /// <summary>The format the composer will draw with — for tests.</summary>
        internal NumberFormatOptions ComposerNumberFormat => _composer.NumberFormat;

        /// <summary>The format the streaming printer will draw with — for tests.</summary>
        internal NumberFormatOptions PrintBuilderNumberFormat => _commandBuilder.NumberFormat;

        /// <summary>
        /// Runs a streaming print job using "template once" optimization.
        /// </summary>
        public async Task<JobResult> RunStreamingPrintJobAsync(
            NumberedPrintJobOptions options,
            IProgress<ProgressInfo> progress,
            CancellationToken ct)
        {
            var jobId = Guid.NewGuid().ToString("N").Substring(0, 12);
            var errors = new List<string>();

            try
            {
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] STARTING NUMBERING JOB: {jobId}");
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] Printer: {options.PrinterName}");
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] Template: {options.TemplatePath}");
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] Numbers: {options.StartNumber} to {options.StartNumber + options.TotalNumbers - 1}");
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");

                // The streaming path below draws through _commandBuilder, not the
                // composer, so both have to be configured — see ApplyNumberFormat.
                ApplyNumberFormat(options.NumberFormat, options.UseArabicDigits);

                // 1. Check for resume checkpoint
                var resumeNumber = await _checkpointManager.GetResumeStartNumberAsync(jobId);
                var startNumber = resumeNumber ?? options.StartNumber;

                // 2. Detect printer capabilities
                var capabilities = _capabilityDetector.Detect(options.PrinterName);

                // 3. Rasterize template ONCE
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] Loading template...");
                var (templateImage, checksum) = _templateManager.RasterizeTemplate(options.TemplatePath, options.Dpi);

                if (templateImage == null)
                {
                    throw new InvalidOperationException("فشل في تحميل القالب. تأكد من صحة مسار الملف ونوعه.");
                }
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ✅ Template loaded - Size: {templateImage.Width}x{templateImage.Height}");

                // 4. Create appropriate printer implementation
                using var printer = new GdiSpoolPrinter(); // Fallback mode (universal)

                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL: Reset printer state for new job to prevent state leakage
                // ═══════════════════════════════════════════════════════════════════
                printer.ResetForNewJob();

                // The printer formats the numbers it draws, so it needs the same digit
                // count, prefix and suffix as everything else in this job.
                printer.ConfigureNumberFormat(options.NumberFormat, options.UseArabicDigits);

                // Set cached template with validation
                printer.SetCachedTemplate(templateImage);

                if (!printer.IsTemplateReady)
                {
                    throw new InvalidOperationException("فشل في تحضير القالب للطباعة. حدث خطأ في معالجة الصورة.");
                }

                // 5. Build numbering strategy
                // Use NumberingMode from options or default to Auto
                var numberingMode = options.NumberingMode ?? NumberingMode.Auto;

                var bookOptions = new BookJobOptions(
                    TemplateStream: null,
                    TemplatePath: options.TemplatePath,
                    TemplateFormat: TemplateFormat.Image,
                    Layout: LayoutSpec.A4,
                    Slots: options.Slots.ToList(),
                    StartNumber: startNumber,
                    TotalNumbers: options.TotalNumbers,
                    PagesPerBook: 1,
                    CopiesPerPage: options.CopiesPerPage,
                    Mode: numberingMode,
                    LowResourceMode: options.LowResourceMode,
                    DegreeOfParallelism: 1,
                    CheckpointEvery: options.CheckpointEvery,
                    OutputMode: "DirectPrint",
                    OutputPath: ""
                );

                var strategy = NumberingStrategyFactory.Create(bookOptions);
                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL FIX: Use CopiesPerPage for accurate total pages calculation
                // This ensures correct progress tracking regardless of CopyTypes state
                // ═══════════════════════════════════════════════════════════════════
                long totalPages = (options.TotalNumbers / Math.Max(options.Slots.Count, 1))
                    * options.CopiesPerPage;
                long pagesGenerated = 0;

                // 6. Start print job
                await printer.StartJobAsync(new PrintJobSettings
                {
                    PrinterName = options.PrinterName,
                    Copies = 1,
                    Dpi = options.Dpi,
                    CopyTrayMapping = options.CopyTrayMapping,
                    ScaleMode = options.ScaleMode ?? PrintScaleMode.ActualSize,  // Use ScaleMode from options, default to ActualSize
                    FitToPage = (options.ScaleMode ?? PrintScaleMode.ActualSize) == PrintScaleMode.FitToPage  // Backward compatibility
                }, ct);

                // 7. Stream pages with multi-copy support
                var copyTypes = options.CopyTypes ?? new[] { CopyType.Original };

                // ═══════════════════════════════════════════════════════════════════
                // DIAGNOSTIC LOGGING: Track copy types and tray mapping configuration
                // ═══════════════════════════════════════════════════════════════════
                System.Diagnostics.Debug.WriteLine($"[JobOrchestrator] Starting streaming print job:");
                System.Diagnostics.Debug.WriteLine($"  TotalNumbers: {options.TotalNumbers}");
                System.Diagnostics.Debug.WriteLine($"  CopiesPerPage: {options.CopiesPerPage}");
                System.Diagnostics.Debug.WriteLine($"  CopyTypes: {string.Join(", ", copyTypes)}");
                System.Diagnostics.Debug.WriteLine($"  UseSmartPrinting: {options.UseSmartPrinting} ({(options.UseSmartPrinting ? "Interleaved: Page→Copies" : "Batch: Copy→Pages")})");
                if (options.CopyTrayMapping != null)
                {
                    foreach (var kvp in options.CopyTrayMapping)
                    {
                        System.Diagnostics.Debug.WriteLine($"  TrayMapping[{kvp.Key}] = {kvp.Value}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  TrayMapping: null (no multi-tray printing)");
                }

                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL FIX: Support two printing modes based on UseSmartPrinting
                // 
                // Smart Printing (Interleaved): Page→Copies
                //   Page 1: Original → Copy1 → Copy2
                //   Page 2: Original → Copy1 → Copy2
                //   ...
                //
                // Traditional Printing (Batch): Copy→Pages
                //   Original: Page 1 → Page 2 → ... → Page N
                //   Copy1:    Page 1 → Page 2 → ... → Page N
                //   Copy2:    Page 1 → Page 2 → ... → Page N
                // ═══════════════════════════════════════════════════════════════════
                if (options.UseSmartPrinting)
                {
                    // ═══════════════════════════════════════════════════════════════════
                    // SMART PRINTING: Interleaved per Page (Page→Copies)
                    // Outer loop: Pages, Inner loop: Copies
                    // Order: Page1-Original, Page1-Copy1, Page1-Copy2, Page2-Original, ...
                    // ═══════════════════════════════════════════════════════════════════
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] SMART PRINTING MODE (Interleaved: Page→Copies)");
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] Expected pages: {totalPages}");
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");

                    long pageIndex = 0;
                    foreach (var pageNumbers in strategy.GeneratePageNumbers(bookOptions))
                    {
                        if (ct.IsCancellationRequested)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NUMBERING] Cancellation requested at page {pageIndex}");
                            break;
                        }

                        System.Diagnostics.Debug.WriteLine($"[NUMBERING] Page {pageIndex + 1}: Numbers [{string.Join(", ", pageNumbers)}]");

                        int copyIndex = 0;
                        foreach (var copyType in copyTypes)
                        {
                            if (ct.IsCancellationRequested) break;

                            System.Diagnostics.Debug.WriteLine($"[NUMBERING]   Copy {copyIndex} ({copyType})");

                            if (options.CopyTrayMapping != null &&
                                options.CopyTrayMapping.TryGetValue(copyIndex, out var trayKind))
                            {
                                System.Diagnostics.Debug.WriteLine($"[NUMBERING]   → Tray: {trayKind}");
                            }

                            // Set current copy index for tray routing BEFORE printing
                            printer.SetCurrentCopyIndex(copyIndex);

                            // Hand the printer the REAL slots and copy type. This used to
                            // go through a PagePrintCommand that could not carry slot size,
                            // alignment, rotation or copy styling, so the printer rebuilt
                            // them from defaults and every copy printed like the original.
                            await printer.PrintPageWithOverlaysAsync(new GdiPagePrintCommand
                            {
                                PageNumbers = pageNumbers,
                                Slots = options.Slots,
                                CopyType = copyType
                            });

                            pagesGenerated++;
                            copyIndex++;
                        }

                        var lastNumber = pageNumbers.LastOrDefault(n => n > 0);
                        pageIndex++;

                        // Checkpoint
                        await _checkpointManager.SaveCheckpointIfNeeded(jobId, lastNumber, pagesGenerated);

                        // Progress
                        if (pagesGenerated % 10 == 0)
                        {
                            var progressPercent = (int)((double)pagesGenerated / totalPages * 100);
                            progress?.Report(new ProgressInfo(
                                pagesGenerated,
                                totalPages,
                                progressPercent,
                                lastNumber));
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] Smart printing loop completed: {pagesGenerated} pages generated");
                }
                else
                {
                    // ═══════════════════════════════════════════════════════════════════
                    // TRADITIONAL PRINTING: Batch per Copy (Copy→Pages)
                    // Outer loop: Copies, Inner loop: Pages
                    // Order: All-Pages-Original, All-Pages-Copy1, All-Pages-Copy2, ...
                    // ═══════════════════════════════════════════════════════════════════
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] TRADITIONAL PRINTING MODE (Batch: Copy→Pages)");
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] Expected pages: {totalPages}");
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");

                    int copyIndex = 0;
                    foreach (var copyType in copyTypes)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NUMBERING] Cancellation requested at copy {copyIndex}");
                            break;
                        }

                        System.Diagnostics.Debug.WriteLine($"[NUMBERING] Starting Copy {copyIndex} ({copyType})");

                        // Set current copy index for tray routing (applies to all pages in this batch)
                        printer.SetCurrentCopyIndex(copyIndex);

                        if (options.CopyTrayMapping != null &&
                            options.CopyTrayMapping.TryGetValue(copyIndex, out var trayKind))
                        {
                            System.Diagnostics.Debug.WriteLine($"[NUMBERING]   → Tray: {trayKind}");
                        }

                        // Inner loop: All pages for this copy
                        long pageIndex = 0;
                        foreach (var pageNumbers in strategy.GeneratePageNumbers(bookOptions))
                        {
                            if (ct.IsCancellationRequested) break;

                            if (pageIndex % 50 == 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"[NUMBERING]   Page {pageIndex + 1}: Numbers [{string.Join(", ", pageNumbers)}]");
                            }

                            // Hand the printer the REAL slots and copy type. This used to
                            // go through a PagePrintCommand that could not carry slot size,
                            // alignment, rotation or copy styling, so the printer rebuilt
                            // them from defaults and every copy printed like the original.
                            await printer.PrintPageWithOverlaysAsync(new GdiPagePrintCommand
                            {
                                PageNumbers = pageNumbers,
                                Slots = options.Slots,
                                CopyType = copyType
                            });

                            pagesGenerated++;
                            pageIndex++;

                            var lastNumber = pageNumbers.LastOrDefault(n => n > 0);

                            // Checkpoint
                            await _checkpointManager.SaveCheckpointIfNeeded(jobId, lastNumber, pagesGenerated);

                            // Progress
                            if (pagesGenerated % 10 == 0)
                            {
                                var progressPercent = (int)((double)pagesGenerated / totalPages * 100);
                                progress?.Report(new ProgressInfo(
                                    pagesGenerated,
                                    totalPages,
                                    progressPercent,
                                    lastNumber));
                            }
                        }

                        System.Diagnostics.Debug.WriteLine($"[NUMBERING] Copy {copyIndex} completed: {pageIndex} pages");
                        copyIndex++;
                    }

                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] Traditional printing loop completed: {pagesGenerated} pages generated");
                }

                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL FIX: EndJobAsync now waits for PrintDocument.Print() to complete
                // and verifies all pages were sent to Windows before reporting success
                // If EndJobAsync throws, it means printing failed at OS level
                // ═══════════════════════════════════════════════════════════════════
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ENDING PRINT JOB");
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] Pages generated: {pagesGenerated}");
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ═══════════════════════════════════════════════════════════");

                try
                {
                    await printer.EndJobAsync();

                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] ✅ JOB COMPLETED SUCCESSFULLY");
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING]   Job ID: {jobId}");
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING]   Total pages: {pagesGenerated}");

                    _checkpointManager.DeleteCheckpoint(jobId); // Success, remove checkpoint

                    return new JobResult(jobId, true, "", pagesGenerated, errors);
                }
                catch (Exception endJobEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] ❌ JOB FAILED: {endJobEx.Message}");

                    errors.Add($"Print job completion failed: {endJobEx.Message}");

                    return new JobResult(jobId, false, "", pagesGenerated, errors);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ❌ JOB EXCEPTION: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[NUMBERING]   Stack: {ex.StackTrace}");

                errors.Add(ex.Message);

                return new JobResult(jobId, false, "", 0, errors);
            }
        }

        /// <summary>
        /// Legacy PDF generation job (unchanged).
        /// </summary>
        public async Task<JobResult> RunJobAsync(BookJobOptions options, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            var result = new JobResult(Guid.NewGuid().ToString(), false, options.OutputPath, 0, new List<string>());

            try
            {
                using var templateLoader = new TemplateLoader();
                using var templateImage = templateLoader.LoadTemplate(options.TemplateStream, options.TemplateFormat);

                var dir = Path.GetDirectoryName(options.OutputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using var fileStream = new FileStream(options.OutputPath, FileMode.Create);
                using var pdfStreamer = new PdfStreamer(fileStream);

                var strategy = NumberingStrategyFactory.Create(options);
                long totalPages = (long)Math.Ceiling((double)options.TotalNumbers / options.Slots.Count) * options.CopiesPerPage;
                long pagesGenerated = 0;

                foreach (var pageNumbers in strategy.GeneratePageNumbers(options))
                {
                    for (int copy = 0; copy < options.CopiesPerPage; copy++)
                    {
                        if (ct.IsCancellationRequested) break;

                        using var pageImage = _composer.ComposePage(templateImage, pageNumbers, options, copy);
                        pdfStreamer.AddPage(pageImage);

                        pagesGenerated++;

                        if (pagesGenerated % 10 == 0)
                        {
                            progress?.Report(new ProgressInfo(pagesGenerated, totalPages, (double)pagesGenerated / totalPages * 100, pageNumbers.LastOrDefault(n => n > 0)));
                        }
                    }

                    if (ct.IsCancellationRequested) break;
                }

                pdfStreamer.Save();

                return result with { Success = true, TotalPagesGenerated = pagesGenerated };
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                return result with { Success = false };
            }
        }
    }
}

