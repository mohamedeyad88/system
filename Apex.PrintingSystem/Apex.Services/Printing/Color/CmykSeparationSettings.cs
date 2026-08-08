namespace Apex.Services.Printing.Color
{
    /// <summary>
    /// How RGB is separated into CMYK for a given press and stock.
    ///
    /// Total Area Coverage is the sum of all four inks at one point. Exceed what the
    /// press and paper can hold and the sheet will not dry: it set-offs onto the next
    /// sheet, picks in the nip, and the job is scrapped. Every press has a number and
    /// it is not optional — coated sheetfed offset takes ~340%, digital ~280%,
    /// newsprint ~240%.
    ///
    /// Values are fractions, not percentages: 3.00 means 300%.
    /// </summary>
    public sealed class CmykSeparationSettings
    {
        /// <summary>Maximum C+M+Y+K at any single point. 3.00 = 300%.</summary>
        public double TotalAreaCoverage { get; init; } = 3.00;

        /// <summary>
        /// Neutral level at which black ink starts being generated (0–1). Below this
        /// the shadow is built from CMY alone, so light greys stay smooth instead of
        /// breaking into visible black dots.
        /// </summary>
        public double BlackStart { get; init; } = 0.20;

        /// <summary>Maximum black ink (0–1). Presses that cannot hold solid black on
        /// a given stock cap this below 1.</summary>
        public double BlackMax { get; init; } = 1.00;

        /// <summary>
        /// How much of the neutral component is replaced by black (0–1).
        /// 1 = full GCR (cheapest, most stable on press), 0 = no black generation.
        /// </summary>
        public double GcrAmount { get; init; } = 1.00;

        /// <summary>Coated stock on a sheetfed offset press.</summary>
        public static CmykSeparationSettings SheetfedOffsetCoated => new()
        {
            TotalAreaCoverage = 3.40,
        };

        /// <summary>Toner/inkjet digital press — less ink than offset can hold.</summary>
        public static CmykSeparationSettings Digital => new()
        {
            TotalAreaCoverage = 2.80,
        };

        /// <summary>Uncoated / newsprint — absorbs badly, so both TAC and black are capped.</summary>
        public static CmykSeparationSettings Newsprint => new()
        {
            TotalAreaCoverage = 2.40,
            BlackMax = 0.90,
        };

        /// <summary>The conservative default: safe on nearly every press and stock.</summary>
        public static CmykSeparationSettings Default => new();
    }
}
