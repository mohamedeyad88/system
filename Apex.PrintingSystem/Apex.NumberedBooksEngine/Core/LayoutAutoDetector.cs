using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Auto-detects the appropriate numbering mode based on slot layout analysis.
    /// </summary>
    public static class LayoutAutoDetector
    {
        /// <summary>
        /// Analyzes slot layout and returns the recommended numbering mode.
        /// </summary>
        public static NumberingMode DetectMode(IReadOnlyList<SlotSpec> slots)
        {
            if (slots == null || slots.Count == 0)
                return NumberingMode.Linear;

            // Heuristic 1: Many slots (>6) typically means linear/Shershara mode
            if (slots.Count > 6)
                return NumberingMode.Linear;

            // Heuristic 2: Check if slots form a regular grid
            var gridInfo = AnalyzeGrid(slots);

            if (gridInfo.IsGrid && gridInfo.Rows > 1 && gridInfo.Columns > 1)
            {
                // Regular grid suggests imposed/cutting mode
                return NumberingMode.Imposed;
            }

            // Heuristic 3: Check if slots are vertically aligned (column layout)
            if (AreVerticallyAligned(slots))
            {
                return NumberingMode.Linear; // Shershara
            }

            // Heuristic 4: Check if slots are horizontally distributed
            if (AreHorizontallyDistributed(slots))
            {
                return NumberingMode.Imposed; // Cutting
            }

            // Default to linear for ambiguous cases
            return NumberingMode.Linear;
        }

        /// <summary>
        /// Analyzes if slots form a regular grid pattern.
        /// </summary>
        public static (bool IsGrid, int Rows, int Columns) AnalyzeGrid(IReadOnlyList<SlotSpec> slots)
        {
            if (slots.Count < 2)
                return (false, 1, slots.Count);

            // Group by approximate Y position (rows)
            const float tolerance = 0.05f; // 5% tolerance
            var rowGroups = GroupByPosition(slots, s => s.Y, tolerance);

            // Group by approximate X position (columns)
            var colGroups = GroupByPosition(slots, s => s.X, tolerance);

            int rows = rowGroups.Count;
            int cols = colGroups.Count;

            // Check if it's a regular grid (rows * cols == slot count)
            bool isRegularGrid = rows * cols == slots.Count;

            // Also check if each row has the same number of slots
            if (isRegularGrid)
            {
                var slotsPerRow = rowGroups.Select(g => g.Count).Distinct().ToList();
                isRegularGrid = slotsPerRow.Count == 1;
            }

            return (isRegularGrid, rows, cols);
        }

        /// <summary>
        /// Checks if slots are primarily vertically aligned (single column or narrow columns).
        /// </summary>
        private static bool AreVerticallyAligned(IReadOnlyList<SlotSpec> slots)
        {
            if (slots.Count < 2) return true;

            // Check if X positions are mostly similar
            var avgX = slots.Average(s => s.X);
            var xVariance = slots.Average(s => Math.Abs(s.X - avgX));

            // Low X variance means vertical alignment
            return xVariance < 0.1f; // 10% of page width
        }

        /// <summary>
        /// Checks if slots are horizontally distributed across the page.
        /// </summary>
        private static bool AreHorizontallyDistributed(IReadOnlyList<SlotSpec> slots)
        {
            if (slots.Count < 2) return false;

            // Check X position spread
            var minX = slots.Min(s => s.X);
            var maxX = slots.Max(s => s.X);
            var xSpread = maxX - minX;

            // Wide X spread means horizontal distribution
            return xSpread > 0.5f; // More than 50% of page width
        }

        private static List<List<SlotSpec>> GroupByPosition(
            IReadOnlyList<SlotSpec> slots,
            Func<SlotSpec, float> positionSelector,
            float tolerance)
        {
            var groups = new List<List<SlotSpec>>();
            var sorted = slots.OrderBy(positionSelector).ToList();

            List<SlotSpec>? currentGroup = null;
            float? groupPosition = null;

            foreach (var slot in sorted)
            {
                var pos = positionSelector(slot);

                if (groupPosition == null || Math.Abs(pos - groupPosition.Value) > tolerance)
                {
                    currentGroup = new List<SlotSpec> { slot };
                    groups.Add(currentGroup);
                    groupPosition = pos;
                }
                else
                {
                    currentGroup!.Add(slot);
                }
            }

            return groups;
        }

        /// <summary>
        /// Returns a human-readable description of the detected mode.
        /// </summary>
        public static string GetModeDescription(NumberingMode mode)
        {
            return mode switch
            {
                NumberingMode.Linear or NumberingMode.Shershara =>
                    "Shershara (Perforated Pads) - Linear top-to-bottom numbering",
                NumberingMode.Imposed or NumberingMode.Cutting =>
                    "Cutting (Imposed Sheets) - Grid-based imposition numbering",
                NumberingMode.Custom =>
                    "Custom - User-defined numbering pattern",
                _ => "Automatic - Will detect based on layout"
            };
        }
    }
}
