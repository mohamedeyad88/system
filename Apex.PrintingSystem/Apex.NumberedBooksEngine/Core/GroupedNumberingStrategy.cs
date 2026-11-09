using Apex.NumberedBooksEngine.Models;
using System.Collections.Generic;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Wraps another numbering strategy so that slots sharing a <see cref="SlotSpec.GroupId"/>
    /// are ONE numbering unit — they all print the SAME serial. This is what ticket/stub
    /// ("كعب") layouts need: the counterfoil and the ticket on the same sheet carry one number,
    /// and a sheet can hold several such tickets (3–4 booklets up), each its own running serial.
    ///
    /// <para>The inner strategy only ever reads <c>options.Slots.Count</c>, so it is run against
    /// a placeholder slot list of size <see cref="UnitCount"/> (one per unit); each unit's number
    /// is then fanned back out to every slot in that unit, and the usual one-number-per-slot
    /// array is returned so every downstream consumer (printer, composer, preview) is unchanged.</para>
    ///
    /// <para>Slots with GroupId 0 are ungrouped — each is its own unit — so a template with no
    /// groups produces exactly the same numbers it did before this existed.</para>
    /// </summary>
    public sealed class GroupedNumberingStrategy : INumberingStrategy
    {
        private readonly INumberingStrategy _inner;
        private readonly int[] _slotToUnit;

        /// <summary>Distinct numbering units on a sheet — the effective "numbers per sheet".</summary>
        public int UnitCount { get; }

        public GroupedNumberingStrategy(INumberingStrategy inner, IReadOnlyList<SlotSpec> slots)
        {
            _inner = inner;
            (_slotToUnit, UnitCount) = ComputeUnits(slots);
        }

        /// <summary>
        /// Maps each slot to a numbering-unit index (slots sharing a GroupId &gt; 0 share a unit;
        /// every ungrouped slot is its own unit), and returns the unit count. Units are numbered
        /// in order of first appearance so the imposition/stacking order is stable.
        /// </summary>
        public static (int[] slotToUnit, int unitCount) ComputeUnits(IReadOnlyList<SlotSpec> slots)
        {
            var slotToUnit = new int[slots.Count];
            var keyToUnit = new Dictionary<string, int>();
            int next = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                string key = slots[i].GroupId > 0 ? "g" + slots[i].GroupId : "s" + i;
                if (!keyToUnit.TryGetValue(key, out int unit)) { unit = next++; keyToUnit[key] = unit; }
                slotToUnit[i] = unit;
            }
            return (slotToUnit, next);
        }

        /// <summary>The effective numbering-unit count for a slot list (for page-count math).</summary>
        public static int UnitCountFor(IReadOnlyList<SlotSpec> slots) => ComputeUnits(slots).unitCount;

        public IEnumerable<long[]> GeneratePageNumbers(BookJobOptions options)
        {
            if (UnitCount == 0) yield break;

            // The inner strategy needs only the unit COUNT; feed it one placeholder per unit.
            var placeholders = new List<SlotSpec>(UnitCount);
            var sample = options.Slots.Count > 0 ? options.Slots[0] : null;
            for (int u = 0; u < UnitCount && sample != null; u++) placeholders.Add(sample);
            var reduced = options with { Slots = placeholders };

            foreach (var unitNumbers in _inner.GeneratePageNumbers(reduced))
            {
                var full = new long[_slotToUnit.Length];
                for (int i = 0; i < full.Length; i++)
                {
                    int u = _slotToUnit[i];
                    full[i] = (unitNumbers != null && u < unitNumbers.Length) ? unitNumbers[u] : -1;
                }
                yield return full;
            }
        }
    }
}
