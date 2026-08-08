using System.Collections.Generic;

namespace Apex.Core.Models.PaperCutting
{
    /// <summary>
    /// Result of one candidate cutting pattern evaluated by the optimizer.
    /// </summary>
    public class CuttingPatternResult
    {
        /// <summary>Pattern identifier, e.g. "A", "B", "C", "D", "E".</summary>
        public string PatternName { get; set; } = "";

        /// <summary>Total pieces cut from a single raw sheet using this pattern.</summary>
        public int Pieces { get; set; }

        /// <summary>Net area occupied by pieces (mm²).</summary>
        public double UsedArea { get; set; }

        /// <summary>Wasted area on the sheet (mm²).</summary>
        public double WasteArea { get; set; }

        /// <summary>UsedArea / (SheetWidth × SheetHeight) × 100.</summary>
        public double UtilizationPercentage { get; set; }

        /// <summary>True when this pattern combines two different orientations.</summary>
        public bool IsMixed { get; set; }

        /// <summary>True when the primary zone is rotated 90°.</summary>
        public bool IsRotated { get; set; }

        /// <summary>
        /// Relative complexity: lower = simpler cut sequence.
        /// Used as a tiebreaker or when <see cref="PaperOptimizationMode.SimpleCutFirst"/> is chosen.
        /// </summary>
        public int ComplexityScore { get; set; }

        /// <summary>All rectangular blocks that make up this pattern layout.</summary>
        public List<CuttingPlanBlock> Blocks { get; set; } = new();

        /// <summary>Guillotine cut instructions in the recommended cutting order.</summary>
        public List<CuttingInstruction> Instructions { get; set; } = new();
    }
}
