using System.Collections.Generic;

namespace Apex.Core.Models.PaperCutting
{
    /// <summary>
    /// Top-level result returned by <c>PaperCuttingOptimizerService.Calculate</c>.
    /// </summary>
    public class PaperCuttingResult
    {
        // ── Validity ───────────────────────────────────────────────────────
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = "";

        // ── Best pattern ───────────────────────────────────────────────────
        /// <summary>The pattern chosen according to the requested optimization mode.</summary>
        public CuttingPatternResult? BestPattern { get; set; }

        /// <summary>All evaluated patterns (A–E), available for the user to compare.</summary>
        public List<CuttingPatternResult> AllPatterns { get; set; } = new();

        // ── Sheet quantities ───────────────────────────────────────────────
        /// <summary>Pieces per raw sheet (best pattern).</summary>
        public int PiecesPerSheet { get; set; }

        /// <summary>Net sheets needed to deliver <see cref="RequiredQuantity"/> pieces.</summary>
        public int SheetsNeeded { get; set; }

        /// <summary>Sheets needed after adding production waste.</summary>
        public int SheetsWithWaste { get; set; }

        /// <summary>Total pieces that will be produced (SheetsWithWaste × PiecesPerSheet).</summary>
        public int TotalPiecesProduced { get; set; }

        /// <summary>Extra pieces beyond <see cref="RequiredQuantity"/>.</summary>
        public int ExtraPieces { get; set; }

        // ── Geometry ───────────────────────────────────────────────────────
        /// <summary>Effective product width after adding bleed (mm).</summary>
        public double EffectiveProductWidth { get; set; }

        /// <summary>Effective product height after adding bleed (mm).</summary>
        public double EffectiveProductHeight { get; set; }

        /// <summary>Sheet width in mm (converted from input unit).</summary>
        public double SheetWidthMm { get; set; }

        /// <summary>Sheet height in mm (converted from input unit).</summary>
        public double SheetHeightMm { get; set; }

        // ── Area analysis ──────────────────────────────────────────────────
        public double TotalSheetArea { get; set; }
        public double UsedArea { get; set; }
        public double WasteArea { get; set; }
        public double UtilizationPercent { get; set; }

        // ── Human-readable output ──────────────────────────────────────────
        /// <summary>Full Arabic narrative description of the cutting plan.</summary>
        public string HumanReadableSummary { get; set; } = "";

        // ── Input echo ────────────────────────────────────────────────────
        public int RequiredQuantity { get; set; }
        public double ProductionWastePercentage { get; set; }
    }
}
