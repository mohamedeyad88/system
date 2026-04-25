using System;
using System.Drawing.Printing;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents a single page within a Cycle (Original, Copy1, Copy2, etc.)
    /// </summary>
    public class CyclePage
    {
        /// <summary>
        /// The number to print on this page.
        /// </summary>
        public long Number { get; set; }

        /// <summary>
        /// Type of copy (Original, Copy1, Copy2, Copy3).
        /// </summary>
        public Apex.NumberedBooksEngine.Models.CopyType Type { get; set; }

        /// <summary>
        /// Tray/PaperSource to use for this page.
        /// </summary>
        public PaperSourceKind Tray { get; set; }

        /// <summary>
        /// Page index within the cycle (0 = Original, 1 = Copy1, 2 = Copy2, etc.).
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// Slot specifications for numbering on this page.
        /// </summary>
        public System.Collections.Generic.List<Apex.NumberedBooksEngine.Models.SlotSpec>? Slots { get; set; }
    }
}

