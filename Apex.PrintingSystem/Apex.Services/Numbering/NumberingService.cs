using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Numbering
{
    public class NumberingService
    {
        private readonly JobOrchestrator _orchestrator;
        private readonly Composer _composer;
        private readonly TemplateLoader _templateLoader;

        public NumberingService()
        {
            _orchestrator = new JobOrchestrator();
            _composer = new Composer();
            _templateLoader = new TemplateLoader();
        }

        public async Task<JobResult> RunJobAsync(BookJobOptions options, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            return await _orchestrator.RunJobAsync(options, progress, ct);
        }

        /// <summary>
        /// Runs a streaming print job directly to the printer (no intermediate PDF).
        /// </summary>
        public async Task<JobResult> RunStreamingJobAsync(
            string printerName,
            string templatePath,
            IReadOnlyList<SlotSpec> slots,
            long startNumber,
            long totalNumbers,
            int copiesPerPage,
            IProgress<ProgressInfo> progress, 
            CancellationToken ct)
        {
            var options = new NumberedPrintJobOptions(
                PrinterName: printerName,
                TemplatePath: templatePath,
                Dpi: 300,
                StartNumber: startNumber,
                TotalNumbers: totalNumbers,
                CopiesPerPage: copiesPerPage,
                Slots: slots,
                UsePrinterStoredTemplate: false,
                LowResourceMode: false,
                CheckpointEvery: 100,
                CopyTypes: null
            );

            return await _orchestrator.RunStreamingPrintJobAsync(options, progress, ct);
        }

        public SKImage GeneratePreview(Stream templateStream, List<SlotSpec> slots, long startNumber, TemplateFormat format = TemplateFormat.Image)
        {
            // Create a dummy options object for preview
            var options = new BookJobOptions(
                TemplateStream: templateStream,
                TemplatePath: null,
                TemplateFormat: format,
                Layout: LayoutSpec.A4, // Default for preview
                Slots: slots,
                StartNumber: startNumber,
                TotalNumbers: 1,
                PagesPerBook: 1,
                CopiesPerPage: 1,
                Mode: NumberingMode.Linear,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 100,
                OutputMode: "SinglePdf",
                OutputPath: "preview.pdf"
            );

            // Load template
            using var templateImage = _templateLoader.LoadTemplate(templateStream, format);
            
            // Generate single page numbers
            var pageNumbers = new List<long> { startNumber }; // Simplified for preview

            // Compose
            return _composer.ComposePage(templateImage, pageNumbers.ToArray(), options, 0);
        }

        /// <summary>
        /// Generates multiple preview pages (in-memory, no file output).
        /// </summary>
        /// <param name="templateStream">Template stream (image or PDF).</param>
        /// <param name="slots">Slot specifications.</param>
        /// <param name="startNumber">Starting number.</param>
        /// <param name="totalNumbers">Total numbers to generate.</param>
        /// <param name="format">Template format.</param>
        /// <param name="mode">Numbering mode.</param>
        /// <param name="pageCount">Number of preview pages to generate (default 4).</param>
        /// <returns>List of SKImage objects representing preview pages.</returns>
        public List<SKImage> GeneratePreviewPages(
            Stream templateStream, 
            List<SlotSpec> slots, 
            long startNumber, 
            long totalNumbers,
            TemplateFormat format = TemplateFormat.Image,
            NumberingMode mode = NumberingMode.Auto,
            int pageCount = 4)
        {
            var options = new BookJobOptions(
                TemplateStream: templateStream,
                TemplatePath: null,
                TemplateFormat: format,
                Layout: LayoutSpec.A4,
                Slots: slots,
                StartNumber: startNumber,
                TotalNumbers: totalNumbers,
                PagesPerBook: 1,
                CopiesPerPage: 1,
                Mode: mode,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 100,
                OutputMode: "Preview",
                OutputPath: ""
            );

            // Load template once
            using var templateImage = _templateLoader.LoadTemplate(templateStream, format);
            
            // Get numbering strategy
            var strategy = NumberingStrategyFactory.Create(options);
            
            var previewPages = new List<SKImage>();
            int generated = 0;

            foreach (var pageNumbers in strategy.GeneratePageNumbers(options))
            {
                if (generated >= pageCount) break;
                
                var page = _composer.ComposePage(templateImage, pageNumbers, options, 0);
                previewPages.Add(page);
                generated++;
            }

            return previewPages;
        }
    }
}
