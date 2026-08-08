using System.Collections.Generic;

namespace Apex.Core.Models.PaperCutting
{
    /// <summary>
    /// One guillotine pass (horizontal or vertical) describing where to cut.
    /// </summary>
    public class CuttingInstruction
    {
        /// <summary>'H' = horizontal, 'V' = vertical.</summary>
        public char Axis { get; set; }

        /// <summary>Full length of the cut along the sheet (mm).</summary>
        public double TotalLength { get; set; }

        /// <summary>Positions of each individual cut within this pass (mm from origin).</summary>
        public List<double> CutSegments { get; set; } = new();

        /// <summary>Waste strip length at the end of this pass (mm).</summary>
        public double WasteLength { get; set; }

        /// <summary>Human-readable Arabic description of the cut.</summary>
        public string Description { get; set; } = "";
    }
}
