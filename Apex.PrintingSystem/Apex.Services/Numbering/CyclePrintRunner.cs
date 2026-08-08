using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Threading;
using System.Threading.Tasks;
using Apex.Core.Enums;
using Apex.Core.Models;
using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using CopyType = Apex.NumberedBooksEngine.Models.CopyType;

namespace Apex.Services.Numbering
{
    /// <summary>
    /// Runs cycle-based printing end-to-end:
    /// - Verifies trays
    /// - Rasterizes template once, caches in printer
    /// - Enqueues cycles with dependencies
    /// - Executes them sequentially respecting dependencies
    /// - Saves state on Pause/Error for resume capability
    /// </summary>
    public class CyclePrintRunner
    {
        private readonly JobDependencyManager _dependencyManager;
        private readonly SequencedJobQueue _queue;
        private readonly TrayVerificationService _trayVerifier;
        private readonly CycleJobExecutor _executor;
        private readonly CycleStatePersistenceManager _stateManager;
        private string? _currentJobId;

        public CyclePrintRunner(
            JobDependencyManager dependencyManager,
            SequencedJobQueue queue,
            TrayVerificationService trayVerifier,
            CycleJobExecutor executor,
            CycleStatePersistenceManager? stateManager = null)
        {
            _dependencyManager = dependencyManager;
            _queue = queue;
            _trayVerifier = trayVerifier;
            _executor = executor;
            _stateManager = stateManager ?? new CycleStatePersistenceManager();
        }

        public async Task<CycleRunResult> RunAsync(
            string printerName,
            string templatePath,
            IReadOnlyList<SlotSpec> slots,
            long startNumber,
            long totalNumbers,
            int copiesPerPage,
            Dictionary<int, PaperSourceKind> trayMapping,
            int dpi,
            CancellationToken ct,
            string? jobId = null)
        {
            _currentJobId = jobId ?? Guid.NewGuid().ToString();
            var result = new CycleRunResult();

            // Fail-fast tray verification
            var verification = _trayVerifier.Verify(printerName, trayMapping);
            if (!verification.Success)
            {
                result.Errors.Add(verification.ErrorMessage ?? "Tray verification failed.");
                return result;
            }

            // Prepare printer and template once
            using var templateManager = new TemplateManager();
            var (templateImage, checksum) = templateManager.RasterizeTemplate(templatePath, dpi);

            using var printer = new GdiSpoolPrinter();
            printer.SetCachedTemplate(templateImage);

            var settings = new PrintJobSettings
            {
                PrinterName = printerName,
                Copies = 1,
                Dpi = dpi,
                CopyTrayMapping = trayMapping,
                ScaleMode = PrintScaleMode.ActualSize
            };

            // Create and enqueue cycles
            var orchestrator = new CycleOrchestrator(_dependencyManager, _queue, _trayVerifier, batchSize: 5);
            var cycles = orchestrator.CreateAndEnqueueCycles(
                printerName,
                startNumber,
                totalNumbers,
                copiesPerPage,
                slots,
                trayMapping);

            string? lastError = null;

            // Execute sequentially
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Check if paused
                if (_queue.IsPaused)
                {
                    // Save state on pause
                    await SaveStateAsync(printerName, templatePath, slots, startNumber, totalNumbers,
                        copiesPerPage, trayMapping, dpi, lastError);
                    await Task.Delay(100, ct);
                    continue;
                }

                var job = _queue.DequeueReady();
                if (job == null)
                {
                    // check if all done
                    if (AllDone())
                    {
                        // Delete state on successful completion
                        _stateManager.DeleteState(_currentJobId);
                        break;
                    }
                    await Task.Delay(50, ct);
                    continue;
                }

                try
                {
                    await _executor.ExecuteAsync(job, printerName, printer, settings, checksum, ct);
                    _queue.UpdateJobStatus(job.JobId, CycleStatus.CompletedPhysical);
                    result.Completed++;
                    lastError = null;
                }
                catch (OperationCanceledException)
                {
                    _queue.UpdateJobStatus(job.JobId, CycleStatus.Failed, "Cancelled");
                    result.Errors.Add($"Cycle {job.CycleNumber} cancelled.");
                    lastError = $"Cycle {job.CycleNumber} cancelled.";
                    // Save state on cancellation
                    await SaveStateAsync(printerName, templatePath, slots, startNumber, totalNumbers,
                        copiesPerPage, trayMapping, dpi, lastError);
                    throw;
                }
                catch (Exception ex)
                {
                    _queue.UpdateJobStatus(job.JobId, CycleStatus.Failed, ex.Message);
                    result.Errors.Add($"Cycle {job.CycleNumber} failed: {ex.Message}");
                    result.Failed++;
                    lastError = ex.Message;
                    // Save state on error
                    await SaveStateAsync(printerName, templatePath, slots, startNumber, totalNumbers,
                        copiesPerPage, trayMapping, dpi, lastError);
                    // stop on failure to let UI decide (Retry/Skip)
                    break;
                }
            }

            return result;

            bool AllDone()
            {
                foreach (var job in _queue.GetAllJobs())
                {
                    if (!job.Status.IsTerminal())
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Resumes printing from a saved state.
        /// </summary>
        public async Task<CycleRunResult> ResumeFromStateAsync(
            string jobId,
            CancellationToken ct)
        {
            var state = await _stateManager.LoadStateAsync(jobId);
            if (state == null)
            {
                throw new InvalidOperationException($"No saved state found for job {jobId}");
            }

            _currentJobId = jobId;

            // Restore cycles from state
            var cycles = new List<CycleJob>();
            foreach (var cycleState in state.Cycles)
            {
                var cycle = new CycleJob
                {
                    JobId = cycleState.JobId,
                    CycleNumber = cycleState.CycleNumber,
                    StartNumber = cycleState.StartNumber,
                    EndNumber = cycleState.EndNumber,
                    Status = cycleState.Status,
                    DependsOnJobId = cycleState.DependsOnJobId,
                    StartedAtUtc = cycleState.StartedAtUtc,
                    CompletedAtUtc = cycleState.CompletedAtUtc,
                    ErrorMessage = cycleState.ErrorMessage,
                    CopiesPerPage = state.CopiesPerPage,
                    TrayMapping = new Dictionary<int, System.Drawing.Printing.PaperSourceKind>(state.TrayMapping)
                };

                // Rebuild pages
                foreach (var slot in state.Slots)
                {
                    for (int copyIndex = 0; copyIndex < state.CopiesPerPage; copyIndex++)
                    {
                        cycle.Pages.Add(new CyclePage
                        {
                            Number = cycleState.StartNumber,
                            Type = copyIndex switch
                            {
                                0 => Apex.NumberedBooksEngine.Models.CopyType.Original,
                                1 => Apex.NumberedBooksEngine.Models.CopyType.Copy1,
                                2 => Apex.NumberedBooksEngine.Models.CopyType.Copy2,
                                3 => Apex.NumberedBooksEngine.Models.CopyType.Copy3,
                                _ => Apex.NumberedBooksEngine.Models.CopyType.Original
                            },
                            Tray = state.TrayMapping.TryGetValue(copyIndex, out var trayKind)
                                ? trayKind
                                : System.Drawing.Printing.PaperSourceKind.Upper,
                            PageIndex = copyIndex,
                            Slots = new List<Apex.NumberedBooksEngine.Models.SlotSpec> { slot }
                        });
                    }
                }

                cycles.Add(cycle);
                _dependencyManager.AddJob(cycle);

                // Re-enqueue cycles that are not completed
                if (!cycle.Status.IsTerminal())
                {
                    _queue.Enqueue(cycle);
                }
            }

            // Continue execution
            return await RunAsync(
                state.PrinterName,
                state.TemplatePath,
                state.Slots,
                state.StartNumber,
                state.TotalNumbers,
                state.CopiesPerPage,
                state.TrayMapping,
                state.Dpi,
                ct,
                jobId);
        }

        /// <summary>
        /// Saves current state of all cycles.
        /// </summary>
        private async Task SaveStateAsync(
            string printerName,
            string templatePath,
            IReadOnlyList<SlotSpec> slots,
            long startNumber,
            long totalNumbers,
            int copiesPerPage,
            Dictionary<int, System.Drawing.Printing.PaperSourceKind> trayMapping,
            int dpi,
            string? lastError)
        {
            var allCycles = _queue.GetAllJobs();
            await _stateManager.SaveStateAsync(
                _currentJobId!,
                printerName,
                templatePath,
                slots,
                startNumber,
                totalNumbers,
                copiesPerPage,
                trayMapping,
                dpi,
                allCycles,
                lastError);
        }

        // Control methods for UI
        public void Pause() => _queue.Pause();
        public void Resume() => _queue.Resume();
        public bool IsPaused => _queue.IsPaused;
        public bool RetryCycle(string jobId) => _queue.RetryJob(jobId);
        public bool SkipCycle(string jobId, string? reason = null) => _queue.SkipJob(jobId, reason);
        public IReadOnlyCollection<CycleJob> GetAllCycles() => _queue.GetAllJobs();
        public string? CurrentJobId => _currentJobId;
    }

    public class CycleRunResult
    {
        public string? JobId { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; } = new();

        public bool Success => Failed == 0 && Errors.Count == 0;
    }
}

