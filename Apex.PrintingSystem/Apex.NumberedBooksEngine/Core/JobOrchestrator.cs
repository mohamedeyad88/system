using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        IReadOnlyList<CopyType>? CopyTypes = null  // Specific copy styles to print
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
                // 1. Check for resume checkpoint
                var resumeNumber = await _checkpointManager.GetResumeStartNumberAsync(jobId);
                var startNumber = resumeNumber ?? options.StartNumber;

                // 2. Detect printer capabilities
                var capabilities = _capabilityDetector.Detect(options.PrinterName);

                // 3. Rasterize template ONCE
                var (templateImage, checksum) = _templateManager.RasterizeTemplate(options.TemplatePath, options.Dpi);

                // 4. Create appropriate printer implementation
                using var printer = new GdiSpoolPrinter(); // Fallback mode (universal)
                printer.SetCachedTemplate(templateImage);

                // 5. Build numbering strategy
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
                    Mode: NumberingMode.Auto,
                    LowResourceMode: options.LowResourceMode,
                    DegreeOfParallelism: 1,
                    CheckpointEvery: options.CheckpointEvery,
                    OutputMode: "DirectPrint",
                    OutputPath: ""
                );

                var strategy = NumberingStrategyFactory.Create(bookOptions);
                long totalPages = (options.TotalNumbers / Math.Max(options.Slots.Count, 1)) 
                    * (options.CopyTypes?.Count ?? 1);
                long pagesGenerated = 0;

                // 6. Start print job
                await printer.StartJobAsync(new PrintJobSettings
                {
                    PrinterName = options.PrinterName,
                    Copies = 1,
                    Dpi = options.Dpi
                }, ct);

                // 7. Stream pages with multi-copy support
                var copyTypes = options.CopyTypes ?? new[] { CopyType.Original };

                foreach (var pageNumbers in strategy.GeneratePageNumbers(bookOptions))
                {
                    if (ct.IsCancellationRequested) break;

                    foreach (var copyType in copyTypes)
                    {
                        if (ct.IsCancellationRequested) break;

                        // Build overlay command with copy-specific styling
                        var command = _commandBuilder.BuildGdiCommandWithCopyStyle(
                            checksum, 
                            pageNumbers, 
                            options.Slots, 
                            options.Dpi,
                            copyType);

                        // Print using cached template + overlay
                        await printer.PrintPageWithOverlaysAsync(command);

                        pagesGenerated++;
                    }

                    var lastNumber = pageNumbers.LastOrDefault(n => n > 0);

                    // Checkpoint
                    await _checkpointManager.SaveCheckpointIfNeeded(jobId, lastNumber, pagesGenerated);

                    // Progress
                    if (pagesGenerated % 10 == 0)
                    {
                        progress?.Report(new ProgressInfo(
                            pagesGenerated, 
                            totalPages, 
                            (double)pagesGenerated / totalPages * 100, 
                            lastNumber));
                    }
                }

                await printer.EndJobAsync();
                _checkpointManager.DeleteCheckpoint(jobId); // Success, remove checkpoint

                return new JobResult(jobId, true, "", pagesGenerated, errors);
            }
            catch (Exception ex)
            {
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

