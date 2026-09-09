using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using Apex.Core.Models;
using Apex.NumberedBooksEngine.Models;

namespace Apex.Services.Numbering
{
    /// <summary>
    /// Splits a numbering request into CycleJobs, assigns dependencies, batches, and enqueues into a sequenced queue.
    /// </summary>
    public class CycleOrchestrator
    {
        private readonly JobDependencyManager _dependencyManager;
        private readonly SequencedJobQueue _queue;
        private readonly ITrayVerificationService _trayVerifier;
        private readonly int _batchSize;

        public CycleOrchestrator(
            JobDependencyManager dependencyManager,
            SequencedJobQueue queue,
            ITrayVerificationService trayVerifier,
            int batchSize = 5)
        {
            _dependencyManager = dependencyManager;
            _queue = queue;
            _trayVerifier = trayVerifier;
            _batchSize = Math.Max(1, batchSize);
        }

        /// <summary>
        /// Create CycleJobs for a range and enqueue them in the sequenced queue.
        /// Performs a single tray verification upfront (fail-fast).
        /// </summary>
        public IReadOnlyList<CycleJob> CreateAndEnqueueCycles(
            string printerName,
            long startNumber,
            long totalNumbers,
            int copiesPerPage,
            IReadOnlyList<SlotSpec> slots,
            Dictionary<int, int> trayMapping)
        {
            var verification = _trayVerifier.Verify(printerName, trayMapping);
            if (!verification.Success)
            {
                throw new InvalidOperationException($"Tray verification failed: {verification.ErrorMessage}");
            }

            var cycles = new List<CycleJob>((int)totalNumbers);
            CycleJob? previous = null;

            for (int i = 0; i < totalNumbers; i++)
            {
                var number = startNumber + i;
                var cycle = new CycleJob
                {
                    CycleNumber = i + 1,
                    StartNumber = number,
                    EndNumber = number,
                    CopiesPerPage = copiesPerPage,
                    TrayMapping = new Dictionary<int, int>(trayMapping),
                    DependsOnJobId = previous?.JobId,
                    BatchId = $"batch-{i / _batchSize}"
                };

                // Build pages for this cycle
                for (int copyIndex = 0; copyIndex < copiesPerPage; copyIndex++)
                {
                    cycle.Pages.Add(new CyclePage
                    {
                        Number = number,
                        Type = copyIndex switch
                        {
                            0 => CopyType.Original,
                            1 => CopyType.Copy1,
                            2 => CopyType.Copy2,
                            3 => CopyType.Copy3,
                            _ => CopyType.Original
                        },
                        Tray = trayMapping.TryGetValue(copyIndex, out var trayRawKind) ? trayRawKind : (int)PaperSourceKind.AutomaticFeed,
                        PageIndex = copyIndex,
                        Slots = new List<SlotSpec>(slots)
                    });
                }

                cycles.Add(cycle);
                _queue.Enqueue(cycle);
                previous = cycle;
            }

            return cycles;
        }
    }
}

