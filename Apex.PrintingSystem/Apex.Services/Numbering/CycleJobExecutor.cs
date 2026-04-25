using System;
using System.Threading;
using System.Threading.Tasks;
using Apex.Core.Enums;
using Apex.Core.Models;
using Apex.NumberedBooksEngine.Core;

namespace Apex.Services.Numbering
{
    /// <summary>
    /// Executes a single CycleJob against a printer (streaming pages with tray switching).
    /// Assumes GdiSpoolPrinter supports SetCurrentCopyIndex + QueryPageSettings tray selection.
    /// </summary>
    public class CycleJobExecutor
    {
        private readonly JobDependencyManager _dependencyManager;
        private readonly TrayVerificationService _trayVerifier;

        public CycleJobExecutor(JobDependencyManager dependencyManager, TrayVerificationService trayVerifier)
        {
            _dependencyManager = dependencyManager;
            _trayVerifier = trayVerifier;
        }

        public async Task ExecuteAsync(
            CycleJob cycle,
            string printerName,
            GdiSpoolPrinter printer,
            PrintJobSettings settings,
            string templateChecksum,
            CancellationToken ct)
        {
            // Dependency check
            if (!_dependencyManager.AreDependenciesSatisfied(cycle))
                throw new InvalidOperationException($"Dependencies not satisfied for cycle {cycle.CycleNumber}.");

            // Tray verification before start (fail-fast)
            var verification = _trayVerifier.Verify(printerName, cycle.TrayMapping);
            if (!verification.Success)
            {
                _dependencyManager.UpdateStatus(cycle.JobId, CycleStatus.Failed, verification.ErrorMessage);
                throw new InvalidOperationException($"Tray verification failed: {verification.ErrorMessage}");
            }

            _dependencyManager.UpdateStatus(cycle.JobId, CycleStatus.Printing);

            // Start printer job (1 copy; we handle copies per page manually)
            var jobSettings = new PrintJobSettings
            {
                PrinterName = printerName,
                Copies = 1,
                Collate = settings.Collate,
                Dpi = settings.Dpi,
                CopyTrayMapping = cycle.TrayMapping,
#pragma warning disable CS0618
                FitToPage = settings.FitToPage,
#pragma warning restore CS0618
                ScaleMode = settings.ScaleMode
            };

            await printer.StartJobAsync(jobSettings, ct);

            try
            {
                foreach (var page in cycle.Pages)
                {
                    ct.ThrowIfCancellationRequested();

                    // Set copy index so QueryPageSettings selects correct tray
                    printer.SetCurrentCopyIndex(page.PageIndex);

                    // Build overlay command for this page
                    var command = BuildCommand(cycle, page, settings.Dpi, templateChecksum);

                    await printer.PrintPageWithOverlaysAsync(command);
                }

                await printer.EndJobAsync();
                _dependencyManager.UpdateStatus(cycle.JobId, CycleStatus.CompletedPhysical);
            }
            catch (Exception ex)
            {
                _dependencyManager.UpdateStatus(cycle.JobId, CycleStatus.Failed, ex.Message);
                throw;
            }
        }

        private GdiPagePrintCommand BuildCommand(CycleJob cycle, CyclePage page, int dpi, string templateChecksum)
        {
            // Build GDI command for page printing
            return new GdiPagePrintCommand
            {
                PageNumbers = new[] { page.Number },
                Slots = page.Slots ?? new System.Collections.Generic.List<Apex.NumberedBooksEngine.Models.SlotSpec>(),
                CopyType = page.Type
            };
        }
    }
}

