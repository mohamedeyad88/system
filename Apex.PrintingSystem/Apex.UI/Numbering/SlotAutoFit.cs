using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Apex.UI.Models;

namespace Apex.UI.Numbering
{
    /// <summary>
    /// Sizes a numbering field's box to the number inside it.
    ///
    /// <para>A new field used to be born 30% of the sheet wide and 8% tall whatever the
    /// number was, so on an A4 the frame was ~7&#160;cm of box around ~2&#160;cm of type.
    /// Reported from the floor as "الفريم الأزرق كبير جدًا مش عارف أتحكم في الرقم": the
    /// operator drags the frame over the printed "No." box on the design, but the number
    /// sits somewhere inside that large frame according to its alignment, so what lands on
    /// paper is a guess. The canvas has no resize handles either, and the properties panel
    /// offers only type size — there was no way to make the box smaller at all.</para>
    ///
    /// <para>The box now measures the number it holds, with a little air around it. What
    /// the operator drags is the number.</para>
    /// </summary>
    public static class SlotAutoFit
    {
        /// <summary>Air around the number, as a fraction of the type size.</summary>
        public const double Padding = 0.25;

        private const double A4WidthInches = 8.27;
        private const double DesignDpi = 96.0;

        /// <summary>The canvas draws type at this multiple of the stored size (see SlotFontScaleConverter).</summary>
        public static double CanvasFontScale(double canvasWidth) =>
            canvasWidth <= 0 ? 1.0 : Math.Clamp(canvasWidth / (A4WidthInches * DesignDpi), 1.0, 5.0);

        /// <summary>Measured text (canvas pixels) → slot size (fraction of the sheet).</summary>
        public static (float Width, float Height) Normalized(
            double textWidth, double textHeight, double canvasWidth, double canvasHeight)
        {
            if (textWidth <= 0 || textHeight <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
                return (0f, 0f);

            double width = (textWidth + textHeight * Padding) / canvasWidth;
            double height = textHeight * (1 + Padding) / canvasHeight;
            return ((float)Math.Clamp(width, 0.005, 1.0), (float)Math.Clamp(height, 0.005, 1.0));
        }

        /// <summary>
        /// Resizing must not move the number: the edge it is aligned to stays put, and it
        /// stays vertically centred where it was. Otherwise tightening the box would shift
        /// every number on a saved design.
        /// </summary>
        public static (float X, float Y) KeepAnchor(
            string? alignment, float x, float y, float oldWidth, float oldHeight, float newWidth, float newHeight)
        {
            float nx = (alignment ?? "").Trim().ToLowerInvariant() switch
            {
                "left" => x,
                "right" => x + (oldWidth - newWidth),
                _ => x + (oldWidth - newWidth) / 2f,
            };
            float ny = y + (oldHeight - newHeight) / 2f;
            return (Math.Clamp(nx, 0f, 1f), Math.Clamp(ny, 0f, 1f));
        }

        /// <summary>The size this slot's box should have, or null when it is not text.</summary>
        public static (float Width, float Height)? MeasureFor(NumberSlot? slot, double canvasWidth, double canvasHeight)
        {
            if (slot == null) return null;
            // A barcode is not type: its box is its printed size and stays as the operator set it.
            if (!string.Equals(slot.SlotKind, "Text", StringComparison.OrdinalIgnoreCase)) return null;

            var text = string.IsNullOrWhiteSpace(slot.PreviewNumber) ? "000000" : slot.PreviewNumber;
            try
            {
                var typeface = new Typeface(
                    new FontFamily(string.IsNullOrWhiteSpace(slot.FontFamily) ? "Arial" : slot.FontFamily),
                    FontStyles.Normal,
                    slot.IsBold ? FontWeights.Bold : FontWeights.Normal,
                    FontStretches.Normal);

                double em = Math.Max(1.0, slot.FontSize * CanvasFontScale(canvasWidth));
                var formatted = new FormattedText(
                    text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, em, Brushes.Black, 1.0);

                return Normalized(formatted.WidthIncludingTrailingWhitespace, formatted.Height, canvasWidth, canvasHeight);
            }
            catch
            {
                // A font that will not load must not stop the operator adding a field.
                return null;
            }
        }
    }
}
