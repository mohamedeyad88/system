using System;

namespace Apex.Services.SmartVariables
{
    /// <summary>
    /// Measures a string at a given font size. Supplied by the caller so the fitting
    /// rules can be reasoned about and tested without a rendering stack.
    /// </summary>
    /// <returns>Width and height in the same units as the box.</returns>
    public delegate (double Width, double Height) MeasureText(string text, double fontSize);

    /// <summary>What fitting decided, and whether the text actually fits.</summary>
    /// <param name="FontSize">The size to render at.</param>
    /// <param name="Fits">
    /// False when even the minimum size overflows. The caller must not treat this as a
    /// detail: on a variable-data run it means that record will print wrong.
    /// </param>
    /// <param name="OverflowRatio">
    /// How far past the box the text still runs at the chosen size (1.0 = exactly full).
    /// </param>
    public sealed record CopyFitResult(double FontSize, bool Fits, double OverflowRatio);

    /// <summary>
    /// Shrinks text to fit its box.
    ///
    /// The field model has offered <c>ShrinkToFit</c> and <c>MinFontSize</c> since the
    /// beginning and nothing implemented them — the mode was the DEFAULT, so on a run of
    /// several thousand certificates every unusually long name silently spilled out of
    /// its box or was clipped mid-word. Nobody sees it until the job is on paper.
    ///
    /// Shrinking has a floor: text below the minimum stops being legible, so past that
    /// point the honest answer is "this does not fit" rather than a smaller unreadable
    /// line. The caller decides what to do — flag the record, wrap, or truncate.
    /// </summary>
    public static class CopyFitCalculator
    {
        /// <summary>Smallest step worth searching, in points.</summary>
        private const double Precision = 0.25;

        /// <summary>
        /// Largest size at or below <paramref name="startSize"/> whose text fits the box.
        /// </summary>
        public static CopyFitResult Fit(
            string? text,
            double boxWidth,
            double boxHeight,
            double startSize,
            double minSize,
            MeasureText measure)
        {
            if (measure == null) throw new ArgumentNullException(nameof(measure));

            // Nothing to fit: an empty field is not an overflow.
            if (string.IsNullOrEmpty(text))
                return new CopyFitResult(startSize, true, 0);

            if (boxWidth <= 0 || boxHeight <= 0)
                return new CopyFitResult(minSize, false, double.PositiveInfinity);

            if (minSize <= 0) minSize = Precision;
            if (startSize < minSize) startSize = minSize;

            // Common case first: it already fits, so do not shrink it needlessly.
            double ratioAtStart = Ratio(measure(text, startSize), boxWidth, boxHeight);
            if (ratioAtStart <= 1.0)
                return new CopyFitResult(startSize, true, ratioAtStart);

            // Even the floor overflows — say so instead of returning a size that lies.
            double ratioAtMin = Ratio(measure(text, minSize), boxWidth, boxHeight);
            if (ratioAtMin > 1.0)
                return new CopyFitResult(minSize, false, ratioAtMin);

            // Binary search the largest fitting size. Text width is monotonic in font
            // size, so the search is safe.
            double lo = minSize, hi = startSize;
            while (hi - lo > Precision)
            {
                double mid = (lo + hi) / 2.0;
                if (Ratio(measure(text, mid), boxWidth, boxHeight) <= 1.0) lo = mid;
                else hi = mid;
            }

            double chosen = Math.Floor(lo / Precision) * Precision;
            if (chosen < minSize) chosen = minSize;

            return new CopyFitResult(chosen, true, Ratio(measure(text, chosen), boxWidth, boxHeight));
        }

        /// <summary>
        /// Fits against a field's own box and limits, allowing for its padding — the
        /// text lives inside the padding, not the full box.
        /// </summary>
        public static CopyFitResult Fit(
            Models.SmartTemplateField field, string? text, MeasureText measure)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            double w = field.Width - field.PaddingLeft - field.PaddingRight;
            double h = field.Height - field.PaddingTop - field.PaddingBottom;

            return Fit(text, w, h, field.FontSize, field.MinFontSize, measure);
        }

        private static double Ratio((double Width, double Height) size, double boxW, double boxH)
        {
            double byWidth = size.Width / boxW;
            double byHeight = size.Height / boxH;
            return Math.Max(byWidth, byHeight);
        }
    }
}
