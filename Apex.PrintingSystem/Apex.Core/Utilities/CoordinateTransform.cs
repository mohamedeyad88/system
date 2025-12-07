namespace Apex.Core.Utilities
{
    /// <summary>
    /// Utility class for converting coordinates between UI (preview) and printer DPI.
    /// Slots use normalized coordinates (0..1) relative to template dimensions.
    /// </summary>
    public static class CoordinateTransform
    {
        /// <summary>
        /// Converts UI pixel X coordinate to normalized (0..1) coordinate.
        /// </summary>
        public static double NormalizeX(double uiPixelX, double templateWidthPx) 
            => templateWidthPx > 0 ? uiPixelX / templateWidthPx : 0;

        /// <summary>
        /// Converts UI pixel Y coordinate to normalized (0..1) coordinate.
        /// </summary>
        public static double NormalizeY(double uiPixelY, double templateHeightPx) 
            => templateHeightPx > 0 ? uiPixelY / templateHeightPx : 0;

        /// <summary>
        /// Converts normalized X coordinate to printer pixels.
        /// </summary>
        public static int ToPrinterX(double normalizedX, int templatePrintWidthPx) 
            => (int)Math.Round(normalizedX * templatePrintWidthPx);

        /// <summary>
        /// Converts normalized Y coordinate to printer pixels.
        /// </summary>
        public static int ToPrinterY(double normalizedY, int templatePrintHeightPx) 
            => (int)Math.Round(normalizedY * templatePrintHeightPx);

        /// <summary>
        /// Converts normalized X coordinate to UI pixels.
        /// </summary>
        public static double ToUiX(double normalizedX, double templateUiWidthPx) 
            => normalizedX * templateUiWidthPx;

        /// <summary>
        /// Converts normalized Y coordinate to UI pixels.
        /// </summary>
        public static double ToUiY(double normalizedY, double templateUiHeightPx) 
            => normalizedY * templateUiHeightPx;

        /// <summary>
        /// Calculates font size for printing based on DPI ratio.
        /// </summary>
        /// <param name="uiFontSizePt">Font size in points as displayed in UI</param>
        /// <param name="printDpi">Target printer DPI</param>
        /// <param name="previewDpi">Preview/screen DPI (typically 96)</param>
        /// <returns>Adjusted font size for printing</returns>
        public static double FontSizeForPrint(double uiFontSizePt, double printDpi, double previewDpi = 96)
            => uiFontSizePt * (printDpi / previewDpi);

        /// <summary>
        /// Calculates physical dimensions in inches from pixel dimensions.
        /// </summary>
        public static double PixelsToInches(double pixels, double dpi) 
            => dpi > 0 ? pixels / dpi : 0;

        /// <summary>
        /// Calculates pixel dimensions from physical inches.
        /// </summary>
        public static double InchesToPixels(double inches, double dpi) 
            => inches * dpi;

        /// <summary>
        /// Calculates physical dimensions in millimeters from pixel dimensions.
        /// </summary>
        public static double PixelsToMm(double pixels, double dpi) 
            => PixelsToInches(pixels, dpi) * 25.4;

        /// <summary>
        /// Calculates pixel dimensions from physical millimeters.
        /// </summary>
        public static double MmToPixels(double mm, double dpi) 
            => InchesToPixels(mm / 25.4, dpi);

        /// <summary>
        /// Clamps a normalized coordinate to valid range [0, 1].
        /// </summary>
        public static double ClampNormalized(double value) 
            => Math.Max(0, Math.Min(1, value));

        /// <summary>
        /// Validates that a normalized coordinate is within bounds.
        /// </summary>
        public static bool IsWithinBounds(double normalizedX, double normalizedY, double slotWidth, double slotHeight)
            => normalizedX >= 0 && normalizedY >= 0 && 
               normalizedX + slotWidth <= 1.0 && normalizedY + slotHeight <= 1.0;
    }
}
