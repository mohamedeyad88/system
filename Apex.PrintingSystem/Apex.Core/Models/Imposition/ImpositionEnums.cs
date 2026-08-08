namespace Apex.Core.Models.Imposition
{
    /// <summary>The imposition scheme used to arrange source pages onto press sheets.</summary>
    public enum ImpositionType
    {
        /// <summary>Simple grid of pages per sheet side (2/4/6/8/16-Up).</summary>
        NUp,

        /// <summary>Booklet folded and stitched at the spine (pages reordered into signatures).</summary>
        SaddleStitch,

        /// <summary>Booklet bound at a glued spine; pages grouped into multi-leaf signatures.</summary>
        PerfectBinding,

        /// <summary>Same page repeated so that, after cutting and stacking, pads collate in order.</summary>
        CutStack,

        /// <summary>A single design repeated to fill the sheet (cards, stickers, labels).</summary>
        StepRepeat,

        /// <summary>
        /// Work-and-turn: front and back of the piece are imposed on ONE plate. The
        /// sheet is printed, turned left↔right (same gripper edge), then printed again
        /// with the same plate. Cutting the sheet yields TWO finished copies.
        /// </summary>
        WorkAndTurn,

        /// <summary>
        /// Work-and-tumble: same single-plate idea as <see cref="WorkAndTurn"/>, but the
        /// sheet is flipped end-over-end (top↔bottom), which moves the gripper to the
        /// opposite edge — so the sheet must be square-trimmed on both ends.
        /// </summary>
        WorkAndTumble
    }

    /// <summary>Finishing / binding method — drives signature size and page reordering.</summary>
    public enum BindingType
    {
        /// <summary>Folded sheets nested and stapled at the spine.</summary>
        SaddleStitch,

        /// <summary>Signatures stacked and glued at the spine.</summary>
        PerfectBinding,

        /// <summary>No binding — loose sheets (e.g. N-Up flyers).</summary>
        None
    }

    /// <summary>Number of source pages placed per sheet side in an N-Up layout.</summary>
    public enum NUpLayout
    {
        TwoUp = 2,
        FourUp = 4,
        SixUp = 6,
        EightUp = 8,
        SixteenUp = 16
    }

    /// <summary>
    /// Where the page grid sits on the sheet when it does not fill the printable area
    /// (inside the margins). Default is <see cref="Centre"/> (previous fixed behaviour).
    /// </summary>
    public enum SheetAlignment
    {
        TopLeft, TopCentre, TopRight,
        Left, Centre, Right,
        BottomLeft, BottomCentre, BottomRight
    }

    /// <summary>Sheet / page orientation.</summary>
    public enum SheetOrientation
    {
        Portrait,
        Landscape
    }
}
