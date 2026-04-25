using Apex.Services.Printing.VendorDetection;

namespace Apex.Services.Printing.RIP.Models
{
    /// <summary>
    /// Rendering strategy decision made by RIP Decision Engine.
    /// </summary>
    public enum RenderStrategy
    {
        /// <summary>
        /// Native vector rendering - text and vectors preserved as-is.
        /// Best quality, no rasterization.
        /// </summary>
        NativeVector,

        /// <summary>
        /// High DPI rasterization for images only.
        /// Used when images are present but text/vectors can be preserved.
        /// </summary>
        HighDpiRaster,

        /// <summary>
        /// Hybrid rendering - combine native vector with rasterized images.
        /// Best for mixed content.
        /// </summary>
        Hybrid,

        /// <summary>
        /// Fallback rasterization - last resort when native rendering is not possible.
        /// Should be avoided when possible.
        /// </summary>
        FallbackRaster
    }

    /// <summary>
    /// Decision made by RIP Decision Engine for a page.
    /// </summary>
    public class RenderDecision
    {
        /// <summary>
        /// Selected rendering strategy.
        /// </summary>
        public RenderStrategy Strategy { get; set; }

        /// <summary>
        /// Required DPI for rasterization (if needed).
        /// 300 DPI for standard printing, 600+ for professional.
        /// </summary>
        public int RequiredDpi { get; set; } = 300;

        /// <summary>
        /// Whether to preserve text as native/vector.
        /// </summary>
        public bool PreserveText { get; set; } = true;

        /// <summary>
        /// Whether to preserve vector graphics as native.
        /// </summary>
        public bool PreserveVectors { get; set; } = true;

        /// <summary>
        /// Whether rasterization is required.
        /// </summary>
        public bool RequiresRasterization { get; set; }

        /// <summary>
        /// Output format/language for the printer.
        /// </summary>
        public PrintLanguage OutputFormat { get; set; } = PrintLanguage.PostScript;

        /// <summary>
        /// Printer vendor for vendor-specific optimizations.
        /// </summary>
        public PrinterVendor PrinterVendor { get; set; } = PrinterVendor.Generic;

        /// <summary>
        /// Reason for the decision (for debugging/logging).
        /// </summary>
        public string DecisionReason { get; set; } = string.Empty;

        /// <summary>
        /// Quality level (Standard, Professional, Industrial).
        /// </summary>
        public QualityLevel QualityLevel { get; set; } = QualityLevel.Professional;
    }

    /// <summary>
    /// Quality level for rendering.
    /// </summary>
    public enum QualityLevel
    {
        /// <summary>
        /// Standard quality - 300 DPI for images.
        /// </summary>
        Standard = 300,

        /// <summary>
        /// Professional quality - 600 DPI for images.
        /// </summary>
        Professional = 600,

        /// <summary>
        /// Industrial quality - 1200 DPI for images.
        /// </summary>
        Industrial = 1200
    }
}
