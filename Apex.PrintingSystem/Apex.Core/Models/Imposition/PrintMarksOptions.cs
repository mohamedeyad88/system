namespace Apex.Core.Models.Imposition
{
    /// <summary>
    /// Configuration for the printer's marks drawn around imposed pages:
    /// crop marks, fold marks, registration marks, colour bars, and job-info slug.
    /// All length values are in millimetres.
    /// </summary>
    public class PrintMarksOptions
    {
        public bool CropMarks { get; set; } = true;
        public bool FoldMarks { get; set; } = false;
        public bool RegistrationMarks { get; set; } = false;
        public bool ColorBars { get; set; } = false;
        public bool JobInfo { get; set; } = true;

        /// <summary>Length of each crop/fold mark line (mm).</summary>
        public double MarkLength { get; set; } = 4.0;

        /// <summary>Gap between the trim edge and the start of a crop mark (mm).</summary>
        public double MarkOffset { get; set; } = 2.0;

        /// <summary>Stroke width for marks (mm).</summary>
        public double MarkThickness { get; set; } = 0.25;

        // ── Job-info slug content ──────────────────────────────────────────
        public string JobName { get; set; } = "";
        public string FileName { get; set; } = "";

        /// <summary>When true, stamps the current date in the job-info slug.</summary>
        public bool IncludeDate { get; set; } = true;
    }
}
