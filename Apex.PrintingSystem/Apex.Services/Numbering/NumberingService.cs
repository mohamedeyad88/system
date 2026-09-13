using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Numbering
{
    /// <summary>
    /// Service for managing numbering print jobs, including cycle-based printing.
    /// </summary>
    public class NumberingService
    {
        private readonly JobOrchestrator _orchestrator;
        private readonly Composer _composer;
        private readonly TemplateLoader _templateLoader;
        private readonly CycleStatePersistenceManager _stateManager;
        private CyclePrintRunner? _currentCycleRunner;

        public NumberingService()
        {
            _orchestrator = new JobOrchestrator();
            _composer = new Composer();
            _templateLoader = new TemplateLoader();
            _stateManager = new CycleStatePersistenceManager();
        }

        /// <summary>
        /// Runs a traditional print job (legacy method).
        /// </summary>
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
            Dictionary<int, int>? copyTrayMapping = null,
            PrintScaleMode scaleMode = PrintScaleMode.ActualSize,
            IProgress<ProgressInfo>? progress = null,
            CancellationToken ct = default,
            NumberingMode? numberingMode = null,
            bool useSmartPrinting = true,
            bool useArabicDigits = false,
            NumberFormatOptions? numberFormat = null)
        {
            var copyTypes = new List<CopyType> { CopyType.Original };
            for (int i = 1; i < copiesPerPage; i++)
            {
                copyTypes.Add((CopyType)i);
            }

            var copyTrayMappingDict = copyTrayMapping ?? new Dictionary<int, int>();

            // Determine NumberingMode from parameter or default to Auto
            var mode = numberingMode ?? NumberingMode.Auto;

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
                CopyTypes: copyTypes,
                CopyTrayMapping: copyTrayMappingDict,
                ScaleMode: scaleMode,
                NumberingMode: mode,
                UseSmartPrinting: useSmartPrinting,
                UseArabicDigits: useArabicDigits,
                NumberFormat: numberFormat
            );

            return await _orchestrator.RunStreamingPrintJobAsync(options, progress, ct);
        }

        /// <summary>
        /// How far an earlier attempt at this exact job got, or null if there is nothing to
        /// pick up. Ask before printing, and put the answer to the operator — resuming
        /// silently would mean a job asked to start at 1 quietly starting at 201.
        /// </summary>
        public async Task<CheckpointRecord?> FindUnfinishedJobAsync(
            string templatePath,
            IReadOnlyList<SlotSpec> slots,
            long startNumber,
            long totalNumbers,
            int copiesPerPage,
            NumberFormatOptions? numberFormat = null)
            => await _orchestrator.FindUnfinishedJobAsync(
                BuildJobKey(templatePath, slots, startNumber, totalNumbers, copiesPerPage, numberFormat));

        /// <summary>Drops the record of an interrupted attempt once the operator starts over.</summary>
        public void ForgetUnfinishedJob(
            string templatePath,
            IReadOnlyList<SlotSpec> slots,
            long startNumber,
            long totalNumbers,
            int copiesPerPage,
            NumberFormatOptions? numberFormat = null)
            => _orchestrator.ForgetUnfinishedJob(
                BuildJobKey(templatePath, slots, startNumber, totalNumbers, copiesPerPage, numberFormat));

        /// <summary>
        /// The subset of a job's options that decides its identity. Only these fields are
        /// hashed, so the rest can be left at defaults here without affecting the answer.
        /// </summary>
        private static NumberedPrintJobOptions BuildJobKey(
            string templatePath,
            IReadOnlyList<SlotSpec> slots,
            long startNumber,
            long totalNumbers,
            int copiesPerPage,
            NumberFormatOptions? numberFormat)
            => new(
                PrinterName: "",
                TemplatePath: templatePath,
                Dpi: 300,
                StartNumber: startNumber,
                TotalNumbers: totalNumbers,
                CopiesPerPage: copiesPerPage,
                Slots: slots,
                UsePrinterStoredTemplate: false,
                LowResourceMode: false,
                CheckpointEvery: 100,
                NumberFormat: numberFormat);

        /// <summary>
        /// Runs cycle-based printing: each number is a CycleJob with its own print job, dependencies enforced.
        /// Fail-fast tray verification; sequential execution with dependency manager.
        /// </summary>
        public async Task<CycleRunResult> RunCyclePrintAsync(
            string printerName,
            string templatePath,
            IReadOnlyList<SlotSpec> slots,
            long startNumber,
            long totalNumbers,
            int copiesPerPage,
            Dictionary<int, int> trayMapping,
            int dpi = 300,
            CancellationToken ct = default)
        {
            var dependencyManager = new JobDependencyManager();
            var sequencedQueue = new SequencedJobQueue(dependencyManager);
            var trayVerifier = new TrayVerificationService();
            var executor = new CycleJobExecutor(dependencyManager, trayVerifier);
            _currentCycleRunner = new CyclePrintRunner(dependencyManager, sequencedQueue, trayVerifier, executor, _stateManager);

            var jobId = Guid.NewGuid().ToString();
            try
            {
                var result = await _currentCycleRunner.RunAsync(
                    printerName,
                    templatePath,
                    slots,
                    startNumber,
                    totalNumbers,
                    copiesPerPage,
                    trayMapping,
                    dpi,
                    ct,
                    jobId);

                result.JobId = jobId;
                return result;
            }
            finally
            {
                // Keep runner reference for control operations
            }
        }

        /// <summary>
        /// Resumes cycle printing from a saved state.
        /// </summary>
        public async Task<CycleRunResult> ResumeCyclePrintAsync(
            string jobId,
            CancellationToken ct = default)
        {
            if (_currentCycleRunner == null)
            {
                var dependencyManager = new JobDependencyManager();
                var sequencedQueue = new SequencedJobQueue(dependencyManager);
                var trayVerifier = new TrayVerificationService();
                var executor = new CycleJobExecutor(dependencyManager, trayVerifier);
                _currentCycleRunner = new CyclePrintRunner(dependencyManager, sequencedQueue, trayVerifier, executor, _stateManager);
            }

            var result = await _currentCycleRunner.ResumeFromStateAsync(jobId, ct);
            result.JobId = jobId;
            return result;
        }

        // Control methods for cycle-based printing
        public void PauseCyclePrinting()
        {
            _currentCycleRunner?.Pause();
        }

        public void ResumeCyclePrinting()
        {
            _currentCycleRunner?.Resume();
        }

        public bool IsCyclePrintingPaused => _currentCycleRunner?.IsPaused ?? false;

        public bool RetryCycle(string jobId)
        {
            return _currentCycleRunner?.RetryCycle(jobId) ?? false;
        }

        public bool SkipCycle(string jobId, string? reason = null)
        {
            return _currentCycleRunner?.SkipCycle(jobId, reason) ?? false;
        }

        public IReadOnlyCollection<Apex.Core.Models.CycleJob>? GetAllCycles()
        {
            return _currentCycleRunner?.GetAllCycles();
        }

        public string? GetCurrentCycleJobId()
        {
            return _currentCycleRunner?.CurrentJobId;
        }

        /// <summary>
        /// Gets all pending cycle print states (for recovery on app restart).
        /// </summary>
        public async Task<List<CyclePrintState>> GetPendingStatesAsync()
        {
            return await _stateManager.GetPendingStatesAsync();
        }

        /// <summary>
        /// Generates a preview image for a single number.
        /// </summary>
        public SKImage GeneratePreview(Stream templateStream, List<SlotSpec> slots, long startNumber, TemplateFormat format = TemplateFormat.Image)
        {
            var options = new BookJobOptions(
                TemplateStream: null,
                TemplatePath: null,
                TemplateFormat: format,
                Layout: LayoutSpec.A4,
                Slots: slots,
                StartNumber: startNumber,
                TotalNumbers: 1,
                PagesPerBook: 1,
                CopiesPerPage: 1,
                Mode: NumberingMode.Auto,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 0,
                OutputMode: "Preview",
                OutputPath: "");

            using var templateImage = _templateLoader.LoadTemplate(templateStream, format);
            var pageImage = _composer.ComposePage(templateImage, new[] { startNumber }, options, 0);
            return pageImage;
        }

        /// <summary>
        /// Generates preview images for multiple pages.
        /// </summary>
        public List<SKImage> GeneratePreviewPages(Stream templateStream, List<SlotSpec> slots, long startNumber, int pageCount, TemplateFormat format = TemplateFormat.Image)
        {
            var options = new BookJobOptions(
                TemplateStream: null,
                TemplatePath: null,
                TemplateFormat: format,
                Layout: LayoutSpec.A4,
                Slots: slots,
                StartNumber: startNumber,
                TotalNumbers: pageCount,
                PagesPerBook: 1,
                CopiesPerPage: 1,
                Mode: NumberingMode.Auto,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 0,
                OutputMode: "Preview",
                OutputPath: "");

            var previews = new List<SKImage>();
            using var templateImage = _templateLoader.LoadTemplate(templateStream, format);

            for (int i = 0; i < pageCount; i++)
            {
                var number = startNumber + i;
                var pageImage = _composer.ComposePage(templateImage, new[] { number }, options, 0);
                previews.Add(pageImage);
            }

            return previews;
        }
    }
}

