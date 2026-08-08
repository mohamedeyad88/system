using System.Collections.Generic;

namespace Apex.Core.Models.PaperCutting
{
    /// <summary>
    /// One product's plan inside a multi-product job: the product spec paired with
    /// the full optimizer result computed for it on its own sheets.
    /// </summary>
    public class ProductPlanItem
    {
        public ProductSpec Product { get; set; } = new();
        public PaperCuttingResult Result { get; set; } = new();
    }

    /// <summary>
    /// Aggregate result for a multi-product cutting job (separate mode: each product
    /// is cut on its own raw sheets). Holds the per-product breakdown plus rolled-up
    /// grand totals across all products.
    /// </summary>
    public class MultiProductResult
    {
        // ── Validity ───────────────────────────────────────────────────────
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = "";

        /// <summary>Per-product plans, in input order.</summary>
        public List<ProductPlanItem> Items { get; set; } = new();

        // ── Grand totals ───────────────────────────────────────────────────
        /// <summary>Number of distinct products in the job.</summary>
        public int ProductCount { get; set; }

        /// <summary>Sum of required quantities across all products.</summary>
        public int TotalRequiredQuantity { get; set; }

        /// <summary>Sum of net sheets (before production waste) across all products.</summary>
        public int TotalSheetsNet { get; set; }

        /// <summary>Sum of sheets including production waste across all products.</summary>
        public int TotalSheetsWithWaste { get; set; }

        /// <summary>Sum of pieces actually produced across all products.</summary>
        public int TotalPiecesProduced { get; set; }

        // ── Combined area analysis (mm²) ───────────────────────────────────
        public double TotalSheetArea { get; set; }
        public double TotalUsedArea { get; set; }
        public double TotalWasteArea { get; set; }

        /// <summary>Combined utilization = TotalUsedArea / TotalSheetArea × 100.</summary>
        public double CombinedUtilizationPercent { get; set; }

        /// <summary>Full Arabic narrative covering all products and the grand total.</summary>
        public string HumanReadableSummary { get; set; } = "";
    }
}
