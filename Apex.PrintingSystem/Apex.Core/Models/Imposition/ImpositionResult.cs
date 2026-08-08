using System.Collections.Generic;

namespace Apex.Core.Models.Imposition
{
    /// <summary>
    /// Output of <c>ImpositionService.Plan</c>: the computed sheet layouts plus
    /// production statistics (sheets, signatures, paper usage) and an Arabic summary.
    /// </summary>
    public class ImpositionResult
    {
        // ── Validity ───────────────────────────────────────────────────────
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = "";

        /// <summary>Non-fatal warnings (e.g. page count padded to a multiple of 4).</summary>
        public List<string> Warnings { get; set; } = new();

        // ── Layout ─────────────────────────────────────────────────────────
        /// <summary>Computed press-sheet layouts in print order.</summary>
        public List<SheetLayout> Sheets { get; set; } = new();

        /// <summary>Pages placed per sheet side (the N in N-Up, or booklet slots/side).</summary>
        public int PagesPerSide { get; set; }

        /// <summary>Grid columns used on the sheet.</summary>
        public int Columns { get; set; }

        /// <summary>Grid rows used on the sheet.</summary>
        public int Rows { get; set; }

        // ── Quantities ─────────────────────────────────────────────────────
        /// <summary>Physical press sheets required.</summary>
        public int SheetsRequired { get; set; }

        /// <summary>Signatures produced (booklet schemes); 0 otherwise.</summary>
        public int SignatureCount { get; set; }

        /// <summary>Blank padding pages added to complete the last signature.</summary>
        public int PaddingPages { get; set; }

        /// <summary>True when the layout prints on both sides.</summary>
        public bool IsDuplex { get; set; }

        // ── Geometry (mm) ──────────────────────────────────────────────────
        public double SheetWidthMm { get; set; }
        public double SheetHeightMm { get; set; }

        /// <summary>Effective placed page width including bleed (mm).</summary>
        public double PlacedPageWidthMm { get; set; }

        /// <summary>Effective placed page height including bleed (mm).</summary>
        public double PlacedPageHeightMm { get; set; }

        /// <summary>Bleed applied around each placed page (mm) — used by the PDF
        /// engine for crop-mark placement and TrimBox computation.</summary>
        public double BleedMm { get; set; }

        /// <summary>
        /// Left/right bleed actually used when placing pages (mm). The input allows
        /// per-axis bleed, so a single figure cannot describe the placed size — and
        /// a TrimBox inset by the wrong amount tells the cutter to cut into live art.
        /// </summary>
        public double BleedHorizontalMm { get; set; }

        /// <summary>Top/bottom bleed actually used when placing pages (mm).</summary>
        public double BleedVerticalMm { get; set; }

        /// <summary>
        /// Creep compensation applied to the innermost sheet of a signature (mm).
        /// 0 when compensation is off. Pages are shifted toward the spine by this
        /// much at the deepest nesting level so trimmed margins stay even.
        /// </summary>
        public double MaxCreepMm { get; set; }

        // ── Paper usage ────────────────────────────────────────────────────
        /// <summary>Sheet-area utilization of the best layout (%, 0–100).</summary>
        public double UtilizationPercent { get; set; }

        /// <summary>Human-readable Arabic summary of the imposition plan.</summary>
        public string HumanReadableSummary { get; set; } = "";
    }
}
