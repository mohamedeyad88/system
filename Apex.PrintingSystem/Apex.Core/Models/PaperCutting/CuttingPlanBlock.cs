namespace Apex.Core.Models.PaperCutting
{
    /// <summary>
    /// A rectangular zone on the raw sheet that is filled with a grid of identical pieces.
    /// </summary>
    public class CuttingPlanBlock
    {
        /// <summary>X offset from the sheet origin (mm).</summary>
        public double X { get; set; }

        /// <summary>Y offset from the sheet origin (mm).</summary>
        public double Y { get; set; }

        /// <summary>Total width of this block (mm).</summary>
        public double Width { get; set; }

        /// <summary>Total height of this block (mm).</summary>
        public double Height { get; set; }

        /// <summary>Width of a single piece in this block (product + bleed, mm).</summary>
        public double PieceWidth { get; set; }

        /// <summary>Height of a single piece in this block (product + bleed, mm).</summary>
        public double PieceHeight { get; set; }

        /// <summary>Number of columns.</summary>
        public int Columns { get; set; }

        /// <summary>Number of rows.</summary>
        public int Rows { get; set; }

        /// <summary>Total pieces in this block (Columns × Rows).</summary>
        public int PiecesCount => Columns * Rows;

        /// <summary>True when this block's pieces are rotated 90° relative to the primary orientation.</summary>
        public bool IsRotated { get; set; }

        /// <summary>Display label, e.g. "Primary", "Fill-Right", "Fill-Bottom".</summary>
        public string BlockName { get; set; } = "";
    }
}
