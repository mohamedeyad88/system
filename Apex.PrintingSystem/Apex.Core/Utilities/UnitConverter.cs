using System;

namespace Apex.Core.Utilities
{
    /// <summary>
    /// Single source of truth for physical-unit conversions.
    ///
    /// Two different "point-like" units are easy to confuse, and mixing them up
    /// silently rescales printed output by 75% (72/96):
    ///
    ///   • <b>Point (pt)</b> — 1/72 inch. The unit of PDF page geometry
    ///     (PdfSharp, SkiaSharp <c>SKDocument.BeginPage</c>).
    ///   • <b>DIP</b> — 1/96 inch. The WPF device-independent unit. Everything
    ///     drawn into a <c>DrawingVisual</c> / laid out in XAML is in DIPs, and
    ///     <c>RenderTargetBitmap</c> scales DIPs by <c>dpi / 96</c>.
    ///
    /// Always convert from millimetres with the method named for the target
    /// unit — never reuse a "pt" value where DIPs are expected, or vice versa.
    /// </summary>
    public static class UnitConverter
    {
        public const double MmPerInch = 25.4;
        public const double PointsPerInch = 72.0;
        public const double DipPerInch = 96.0;

        /// <summary>Exact millimetres → PDF points (1/72"). Use for PDF page geometry.</summary>
        public const double MmToPoint = PointsPerInch / MmPerInch;   // 2.834645669…

        /// <summary>Exact millimetres → WPF DIPs (1/96"). Use for WPF layout and drawing.</summary>
        public const double MmToDip = DipPerInch / MmPerInch;        // 3.779527559…

        // ── Millimetres ↔ PDF points ──────────────────────────────────────────
        public static double MmToPoints(double mm) => mm * MmToPoint;
        public static double PointsToMm(double points) => points / MmToPoint;

        // ── Millimetres ↔ WPF DIPs ────────────────────────────────────────────
        public static double MmToDips(double mm) => mm * MmToDip;
        public static double DipsToMm(double dips) => dips / MmToDip;

        // ── Points ↔ DIPs ─────────────────────────────────────────────────────
        public static double PointsToDips(double points) => points * (DipPerInch / PointsPerInch);
        public static double DipsToPoints(double dips) => dips * (PointsPerInch / DipPerInch);

        // ── Raster output ─────────────────────────────────────────────────────

        /// <summary>
        /// Millimetres → device pixels at <paramref name="dpi"/>. This is the size a
        /// raster export must have for the image to print at its true physical size.
        /// </summary>
        public static double MmToPixels(double mm, double dpi) => mm / MmPerInch * dpi;

        public static double PixelsToMm(double pixels, double dpi) =>
            dpi <= 0 ? 0 : pixels / dpi * MmPerInch;

        /// <summary>Millimetres → inches.</summary>
        public static double MmToInches(double mm) => mm / MmPerInch;

        /// <summary>Inches → millimetres.</summary>
        public static double InchesToMm(double inches) => inches * MmPerInch;
    }
}
