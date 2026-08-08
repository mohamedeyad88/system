using System;

namespace Apex.Core.Models.Imposition
{
    /// <summary>
    /// A reusable, saveable imposition preset: the full input configuration plus the
    /// printer's-marks options, identified by name. Serialized as JSON for the template
    /// library and for sharing settings between machines.
    /// </summary>
    public class ImpositionTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        public ImpositionInput Input { get; set; } = new();
        public PrintMarksOptions Marks { get; set; } = new();
    }

    /// <summary>PDF/X identification level applied to exported files (best-effort).</summary>
    public enum PdfXConformance
    {
        /// <summary>Standard high-quality PDF, no PDF/X identification.</summary>
        None,

        /// <summary>Stamp PDF/X-1a:2001 identification metadata.</summary>
        PdfX1a,

        /// <summary>Stamp PDF/X-3:2002 identification metadata.</summary>
        PdfX3
    }

    /// <summary>Options applied when exporting the imposed PDF.</summary>
    public class ImpositionExportOptions
    {
        public PdfXConformance PdfX { get; set; } = PdfXConformance.None;
        public string Title { get; set; } = "";
        public string Author { get; set; } = "Apex Print OS";
    }
}
