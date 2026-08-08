using System;
using System.Collections.Generic;
using Apex.NumberedBooksEngine.Models;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Represents a single page/sheet assignment with all slot numbers for that page.
    /// </summary>
    public record PageAssignment(int SheetIndex, IReadOnlyList<SlotAssignment> SlotNumbers);

    /// <summary>
    /// Represents the assignment of a number to a specific slot.
    /// </summary>
    /// <param name="SeriesId">
    /// Which counter the number came from. Carried through so the composer formats it
    /// with that series' own prefix and padding rather than the job's default — two
    /// series on one sheet must not come out looking like the same number.
    /// </param>
    public record SlotAssignment(string SlotId, long Number, string? SeriesId = null);

    /// <summary>
    /// Interface for number sequencing algorithms.
    /// </summary>
    public interface INumberSequencer
    {
        IEnumerable<PageAssignment> GenerateLinearAssignments(
            long startNumber, long totalNumbers, IReadOnlyList<SlotSpec> slots, long step = 1);

        IEnumerable<PageAssignment> GenerateImposedAssignments(
            long startNumber, long totalNumbers, IReadOnlyList<SlotSpec> slots, int copiesPerNumber = 1, long step = 1);
    }

    /// <summary>
    /// Number sequencer implementing Linear (Shershara) and Imposed (Cutting) modes.
    /// </summary>
    public class NumberSequencer : INumberSequencer
    {
        // Legacy API for compatibility
        public IEnumerable<long[]> GenerateSequence(BookJobOptions options)
        {
            if (options.Mode == NumberingMode.Imposed)
            {
                return GenerateImposedSequence(options);
            }
            else
            {
                return GenerateLinearSequence(options);
            }
        }

        private IEnumerable<long[]> GenerateLinearSequence(BookJobOptions options)
        {
            long current = options.StartNumber;
            // Total numbers logic implies count. If step > 1, the range of values is larger, but count is same?
            // "TotalNumbers" usually means "Count of items to generate".
            // So if Start=1, Count=3, Step=2 -> 1, 3, 5.
            // The logic: value = Start + (index * Step).

            long step = options.Step;
            long count = options.TotalNumbers; // This is count of numbers
            int slotsPerPage = options.Slots.Count;

            long index = 0;

            while (index < count)
            {
                var pageValues = new long[slotsPerPage];
                for (int i = 0; i < slotsPerPage; i++)
                {
                    if (index < count)
                    {
                        pageValues[i] = options.StartNumber + (index * step);
                        index++;
                    }
                    else
                    {
                        pageValues[i] = -1; // Empty slot
                    }
                }
                yield return pageValues;
            }
        }

        private IEnumerable<long[]> GenerateImposedSequence(BookJobOptions options)
        {
            int slotsPerSheet = options.Slots.Count;
            long totalNumbers = options.TotalNumbers; // Count
            long startNumber = options.StartNumber;
            long step = options.Step;

            int totalSheets = (int)Math.Ceiling((double)totalNumbers / slotsPerSheet);

            for (int sheet = 0; sheet < totalSheets; sheet++)
            {
                var pageValues = new long[slotsPerSheet];
                for (int slotIndex = 0; slotIndex < slotsPerSheet; slotIndex++)
                {
                    // Linear Index based on imposed logic:
                    // For sheet S, slot K: Index = S + (K * TotalSheets)
                    // Value = Start + (Index * Step)

                    long index = sheet + (long)slotIndex * totalSheets;

                    if (index < totalNumbers)
                    {
                        pageValues[slotIndex] = startNumber + (index * step);
                    }
                    else
                    {
                        pageValues[slotIndex] = -1;
                    }
                }
                yield return pageValues;
            }
        }

        // New API: Generate PageAssignment with slot IDs
        public IEnumerable<PageAssignment> GenerateLinearAssignments(
            long startNumber, long totalNumbers, IReadOnlyList<SlotSpec> slots, long step = 1)
        {
            if (slots == null || slots.Count == 0 || totalNumbers <= 0)
                yield break;

            long count = totalNumbers;
            long currentIdx = 0;
            int sheetIndex = 0;

            while (currentIdx < count)
            {
                var slotAssignments = new List<SlotAssignment>();

                foreach (var slot in slots)
                {
                    if (currentIdx >= count)
                        break;

                    long val = startNumber + (currentIdx * step);
                    slotAssignments.Add(new SlotAssignment(slot.Id, val));
                    currentIdx++;
                }

                if (slotAssignments.Count > 0)
                {
                    yield return new PageAssignment(sheetIndex, slotAssignments);
                    sheetIndex++;
                }
            }
        }

        public IEnumerable<PageAssignment> GenerateImposedAssignments(
            long startNumber, long totalNumbers, IReadOnlyList<SlotSpec> slots, int copiesPerNumber = 1, long step = 1)
        {
            if (slots == null || slots.Count == 0 || totalNumbers <= 0)
                yield break;

            int slotsPerSheet = slots.Count;

            // Total items to print = totalNumbers * copiesPerNumber ?? 
            // Usually imposed numbering with copies: 
            // If copiesPerNumber > 1, do we repeat the number? 
            // Typically with imposed (cutting), we want stacks.
            // If we have 25000 numbers and 2 copies, we might want 2 stacks of 25000 (total 50k sheets?) 
            // OR 1 stack of 1,1,2,2... (This breaks cutting stack logic usually).
            // Usually Copies means "Collated sets" or "Stack replicates".
            // Let's assume copiesPerNumber means we generate the same number X times. 
            // Ideally implementation of duplicates in cutting mode:
            // S1: 1..N
            // S2: 1..N (Copy 2)
            // But we only have slots.
            // If slots > 1, we fill slots.

            // Standard Imposed (copies=1):
            // S1: 1..X, S2: X+1..Y

            // For now, retaining standard logic, ignoring copies within sequencer unless explicit.
            // Using TotalNumbers as "Unique Numbers Count".

            long totalSheets = (totalNumbers + slotsPerSheet - 1) / slotsPerSheet;

            for (int sheetIndex = 0; sheetIndex < totalSheets; sheetIndex++)
            {
                var slotAssignments = new List<SlotAssignment>();

                for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    // Index in the sequence
                    long globalIndex = (slotIndex * totalSheets) + sheetIndex;

                    if (globalIndex < totalNumbers)
                    {
                        long val = startNumber + (globalIndex * step);
                        slotAssignments.Add(new SlotAssignment(slots[slotIndex].Id, val));
                    }
                }

                if (slotAssignments.Count > 0)
                {
                    yield return new PageAssignment(sheetIndex, slotAssignments);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Multiple independent series on one sheet
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Linear (Shershara) assignments where each slot draws from its own series.
        ///
        /// Slots of the SAME series still take consecutive values — that is what makes
        /// a cut stack come out in order. Slots of DIFFERENT series advance their own
        /// counters independently, which is how a receipt number and a control number
        /// share a sheet without either being derived from the other.
        /// </summary>
        public IEnumerable<PageAssignment> GenerateLinearAssignments(
            IReadOnlyList<SlotSpec> slots, IReadOnlyList<NumberSeries> series)
        {
            foreach (var page in Generate(slots, series, imposed: false)) yield return page;
        }

        /// <summary>Imposed (cutting) assignments with one counter per series.</summary>
        public IEnumerable<PageAssignment> GenerateImposedAssignments(
            IReadOnlyList<SlotSpec> slots, IReadOnlyList<NumberSeries> series)
        {
            foreach (var page in Generate(slots, series, imposed: true)) yield return page;
        }

        /// <summary>
        /// Slots that name a series the job does not define. They would print blank,
        /// so a caller can refuse the job instead of discovering it on paper.
        /// </summary>
        public static IReadOnlyList<SlotSpec> FindSlotsWithUnknownSeries(
            IReadOnlyList<SlotSpec> slots, IReadOnlyList<NumberSeries> series)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in series) known.Add(s.Id);

            var orphans = new List<SlotSpec>();
            foreach (var slot in slots)
            {
                if (string.IsNullOrEmpty(slot.SeriesId)) continue;   // primary series
                if (!known.Contains(slot.SeriesId!)) orphans.Add(slot);
            }
            return orphans;
        }

        private static IEnumerable<PageAssignment> Generate(
            IReadOnlyList<SlotSpec> slots, IReadOnlyList<NumberSeries> series, bool imposed)
        {
            if (slots == null || slots.Count == 0 || series == null || series.Count == 0)
                yield break;

            var primary = series[0];

            // Group slot positions by the series they draw from, keeping template order
            // so the cut stack matches the physical layout.
            var slotsBySeries = new Dictionary<string, List<SlotSpec>>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in slots)
            {
                string id = string.IsNullOrEmpty(slot.SeriesId) ? primary.Id : slot.SeriesId!;
                if (!slotsBySeries.TryGetValue(id, out var list))
                    slotsBySeries[id] = list = new List<SlotSpec>();
                list.Add(slot);
            }

            // How many sheets each series needs, and therefore how many the job needs.
            long totalSheets = 0;
            var sheetsFor = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in series)
            {
                if (!slotsBySeries.TryGetValue(s.Id, out var mine) || mine.Count == 0) continue;
                long needed = (s.TotalNumbers + mine.Count - 1) / mine.Count;
                sheetsFor[s.Id] = needed;
                if (needed > totalSheets) totalSheets = needed;
            }

            for (long sheet = 0; sheet < totalSheets; sheet++)
            {
                var assignments = new List<SlotAssignment>();

                foreach (var s in series)
                {
                    if (!slotsBySeries.TryGetValue(s.Id, out var mine) || mine.Count == 0) continue;

                    // A series only spans its OWN sheet count. When a longer series
                    // extends the run, the imposed index formula would wrap this one
                    // back into piles it already printed — issuing the same number
                    // twice, which is the one thing a numbered book cannot survive.
                    if (sheet >= sheetsFor[s.Id]) continue;

                    for (int i = 0; i < mine.Count; i++)
                    {
                        // Linear fills a sheet then moves on; imposed walks down the
                        // stack so slot i of every sheet forms one cut pile.
                        long index = imposed
                            ? i * sheetsFor[s.Id] + sheet
                            : sheet * mine.Count + i;

                        // A series that runs out simply stops contributing; the sheet
                        // still prints for whichever series still has numbers left.
                        if (index >= s.TotalNumbers) continue;

                        assignments.Add(new SlotAssignment(mine[i].Id, s.ValueAt(index), s.Id));
                    }
                }

                if (assignments.Count > 0)
                    yield return new PageAssignment((int)sheet, assignments);
            }
        }
    }
}

