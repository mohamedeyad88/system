namespace Apex.Core.Models.PaperCutting
{
    /// <summary>
    /// All inputs required by the paper-cutting optimizer.
    /// All length values must be supplied in the unit specified by <see cref="Unit"/>.
    /// </summary>
    public class PaperCuttingInput
    {
        // ── Product dimensions ─────────────────────────────────────────────
        /// <summary>Finished product width (after trim).</summary>
        public double ProductWidth { get; set; }

        /// <summary>Finished product height (after trim).</summary>
        public double ProductHeight { get; set; }

        // ── Raw sheet dimensions ───────────────────────────────────────────
        public double RawSheetWidth { get; set; }
        public double RawSheetHeight { get; set; }

        // ── Job parameters ─────────────────────────────────────────────────
        /// <summary>Total finished products required.</summary>
        public int RequiredQuantity { get; set; }

        /// <summary>Production waste/spoilage percentage (0–50).</summary>
        public double ProductionWastePercentage { get; set; }

        // ── Pre-press extras ───────────────────────────────────────────────
        /// <summary>Bleed amount added around the product.</summary>
        public double Bleed { get; set; }

        /// <summary>Cutting margin / gutter between pieces.</summary>
        public double CuttingMargin { get; set; }

        /// <summary>When true, <see cref="Bleed"/> applies to all four sides; otherwise it is the total.</summary>
        public bool IsBleedPerSide { get; set; } = true;

        /// <summary>When true, <see cref="CuttingMargin"/> applies between every adjacent pair; otherwise it is the total.</summary>
        public bool IsCuttingMarginPerSide { get; set; } = true;

        // ── Optimization switches ──────────────────────────────────────────
        /// <summary>Allow 90° rotation of the product to try a landscape fit.</summary>
        public bool AllowRotation { get; set; } = true;

        /// <summary>Unit for all dimension fields.</summary>
        public MeasurementUnit Unit { get; set; } = MeasurementUnit.Millimeter;

        /// <summary>Strategy used to pick the best pattern.</summary>
        public PaperOptimizationMode OptimizationMode { get; set; } = PaperOptimizationMode.MaximumPieces;
    }
}
