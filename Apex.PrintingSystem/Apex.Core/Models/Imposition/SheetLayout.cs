namespace Apex.Core.Models.Imposition
{
    /// <summary>
    /// A single placed source page on a press sheet: which source page goes where,
    /// at what position/size (mm), on which side, and at what rotation.
    /// </summary>
    public class PageSlot
    {
        /// <summary>
        /// 1-based source page number placed in this slot, or 0 for an intentionally
        /// blank slot (e.g. padding pages in a booklet signature).
        /// </summary>
        public int SourcePageNumber { get; set; }

        /// <summary>True when this slot is a deliberate blank (no source page).</summary>
        public bool IsBlank => SourcePageNumber <= 0;

        /// <summary>Left position on the sheet (mm), origin at top-left.</summary>
        public double X { get; set; }

        /// <summary>Top position on the sheet (mm), origin at top-left.</summary>
        public double Y { get; set; }

        /// <summary>Placed width (mm), including bleed.</summary>
        public double Width { get; set; }

        /// <summary>Placed height (mm), including bleed.</summary>
        public double Height { get; set; }

        /// <summary>Rotation in degrees (0 or 90/180/270).</summary>
        public int Rotation { get; set; }
    }

    /// <summary>One press sheet side with its placed page slots.</summary>
    public class SheetSide
    {
        /// <summary>True for the back (verso) side of a duplex sheet.</summary>
        public bool IsBack { get; set; }

        public System.Collections.Generic.List<PageSlot> Slots { get; set; } = new();
    }

    /// <summary>
    /// One physical press sheet — front side, and back side when duplex.
    /// </summary>
    public class SheetLayout
    {
        /// <summary>1-based sheet index in the run.</summary>
        public int SheetIndex { get; set; }

        /// <summary>Signature this sheet belongs to (1-based), for booklet schemes.</summary>
        public int SignatureIndex { get; set; }

        public SheetSide Front { get; set; } = new();

        /// <summary>Back side, or null for single-sided layouts.</summary>
        public SheetSide? Back { get; set; }
    }
}
