using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Represents the settings for a print job.
    /// </summary>
    public class PrintJobSettings
    {
        public string PrinterName { get; set; } = "";
        public int Copies { get; set; } = 1;
        public bool Collate { get; set; } = true;
        public int Dpi { get; set; } = 300;

        /// <summary>
        /// Tray selection for each copy (index 0 = Original, 1 = Copy 1, 2 = Copy 2, etc.)
        /// If null or empty, uses default tray.
        ///
        /// <para>The value is the printer's own source id — <c>PaperSource.RawKind</c> — not
        /// a <c>PaperSourceKind</c>. Windows reports most vendor drawers as <c>Custom</c>,
        /// so keying by kind made every drawer on a machine look like the same drawer and
        /// each copy was routed to whichever one happened to come first. RawKind is exact:
        /// on an EPSON WF-C5210 the paper tray is 258 and the rear feed is 261.</para>
        /// </summary>
        public Dictionary<int, int>? CopyTrayMapping { get; set; }

        /// <summary>
        /// Fit to page - scales the content to fit the printable area of the page.
        /// </summary>
        [Obsolete("Use ScaleMode instead")]
        public bool FitToPage { get; set; } = true;

        /// <summary>
        /// Print scaling mode. Default is ActualSize (100%).
        /// </summary>
        public PrintScaleMode ScaleMode { get; set; } = PrintScaleMode.ActualSize;
    }

    /// <summary>
    /// Represents the status of a print job.
    /// </summary>
    public class PrintJobStatus
    {
        public long CurrentPage { get; set; }
        public long TotalPages { get; set; }
        public double PagesPerSecond { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public string Status { get; set; } = "Idle";
        public string? Error { get; set; }
        public bool IsPaused { get; set; }
        public bool IsCancelled { get; set; }
    }

    /// <summary>
    /// Service for direct printing to the Windows Print Spooler.
    /// No intermediate files are created.
    /// </summary>
    public interface IPrintOutputService
    {
        /// <summary>
        /// Gets the current status of the print job.
        /// </summary>
        PrintJobStatus Status { get; }

        /// <summary>
        /// Starts a print job with the given settings.
        /// </summary>
        Task StartJobAsync(PrintJobSettings settings, CancellationToken ct);

        /// <summary>
        /// Sends a single page to the print spooler.
        /// </summary>
        Task PrintPageAsync(SKImage page);

        /// <summary>
        /// Ends the current print job.
        /// </summary>
        Task EndJobAsync();

        /// <summary>
        /// Pauses the current print job.
        /// </summary>
        void Pause();

        /// <summary>
        /// Resumes a paused print job.
        /// </summary>
        void Resume();

        /// <summary>
        /// Cancels the current print job.
        /// </summary>
        void Cancel();

        /// <summary>
        /// Event raised when job status changes.
        /// </summary>
        event EventHandler<PrintJobStatus>? StatusChanged;
    }
}
