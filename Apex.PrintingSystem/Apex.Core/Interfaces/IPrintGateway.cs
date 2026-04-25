using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    /// <summary>
    /// 🔒 UNIFIED PRINT GATEWAY - Mandatory Entry Point
    /// 
    /// ALL print operations MUST go through this interface.
    /// Direct printing to OS/Printer is PROHIBITED.
    /// 
    /// This is the single enforced interface for the entire application.
    /// </summary>
    public interface IPrintGateway
    {
        /// <summary>
        /// Submit a print request to the unified pipeline.
        /// This is the ONLY way to print in the application.
        /// </summary>
        /// <param name="request">The print request containing file and settings</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>A ticket representing the queued print job</returns>
        Task<PrintTicket> SubmitAsync(PrintRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Submit multiple files to print as a single virtual job.
        /// </summary>
        Task<PrintTicket> SubmitBatchAsync(IEnumerable<PrintRequest> requests, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the current status of a print job.
        /// </summary>
        Task<GatewayJobStatus> GetStatusAsync(Guid ticketId);

        /// <summary>
        /// Cancel a pending or in-progress print job.
        /// </summary>
        Task<bool> CancelAsync(Guid ticketId);

        /// <summary>
        /// Pause a print job (can be resumed later).
        /// </summary>
        Task<bool> PauseAsync(Guid ticketId);

        /// <summary>
        /// Resume a paused print job from the last successful page.
        /// </summary>
        Task<bool> ResumeAsync(Guid ticketId);

        /// <summary>
        /// Event raised when job status changes.
        /// </summary>
        event EventHandler<PrintJobStatusChangedEventArgs> StatusChanged;

        /// <summary>
        /// Event raised for progress updates (pages printed, etc).
        /// </summary>
        event EventHandler<PrintProgressEventArgs> ProgressUpdated;
    }

    /// <summary>
    /// Represents a print request submitted to the gateway.
    /// </summary>
    public class PrintRequest
    {
        /// <summary>
        /// Path to the file to print.
        /// </summary>
        public required string FilePath { get; set; }

        /// <summary>
        /// Target printer name.
        /// </summary>
        public required string PrinterName { get; set; }

        /// <summary>
        /// Number of copies.
        /// </summary>
        public int Copies { get; set; } = 1;

        /// <summary>
        /// Page range (e.g., "1-5", "1,3,5", "All").
        /// </summary>
        public string PageRange { get; set; } = "All";

        /// <summary>
        /// Enable duplex printing.
        /// </summary>
        public bool Duplex { get; set; } = false;

        /// <summary>
        /// Enable color printing.
        /// </summary>
        public bool Color { get; set; } = true;

        /// <summary>
        /// Paper size (A4, Letter, etc).
        /// </summary>
        public string PaperSize { get; set; } = "A4";

        /// <summary>
        /// Orientation (Portrait/Landscape).
        /// </summary>
        public string Orientation { get; set; } = "Portrait";

        /// <summary>
        /// Print quality (Draft, Normal, High, Best).
        /// </summary>
        public string Quality { get; set; } = "Normal";

        /// <summary>
        /// Priority level for queue ordering.
        /// </summary>
        public PrintPriority Priority { get; set; } = PrintPriority.Normal;

        /// <summary>
        /// Optional: Scheduled time for printing.
        /// </summary>
        public DateTime? ScheduledTime { get; set; }

        /// <summary>
        /// Source module that initiated the request.
        /// </summary>
        public string SourceModule { get; set; } = "Unknown";

        /// <summary>
        /// Optional metadata for tracking.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// A ticket representing a queued print job.
    /// </summary>
    public class PrintTicket
    {
        /// <summary>
        /// Unique identifier for this print job.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// When the job was submitted.
        /// </summary>
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Current status of the job.
        /// </summary>
        public PrintJobState State { get; set; } = PrintJobState.Queued;

        /// <summary>
        /// Estimated total pages.
        /// </summary>
        public int EstimatedPages { get; set; }

        /// <summary>
        /// Position in queue.
        /// </summary>
        public int QueuePosition { get; set; }
    }

    /// <summary>
    /// Print job states.
    /// </summary>
    public enum PrintJobState
    {
        Queued,
        Preparing,      // Document processing
        Streaming,      // Sending to printer
        Printing,       // Actively printing
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Print priority levels.
    /// </summary>
    public enum PrintPriority
    {
        Low = 0,
        Normal = 50,
        High = 75,
        Urgent = 100
    }

    /// <summary>
    /// Event args for status changes.
    /// </summary>
    public class PrintJobStatusChangedEventArgs : EventArgs
    {
        public Guid TicketId { get; set; }
        public PrintJobState OldState { get; set; }
        public PrintJobState NewState { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Event args for progress updates.
    /// </summary>
    public class PrintProgressEventArgs : EventArgs
    {
        public Guid TicketId { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public double PercentComplete => TotalPages > 0 ? (CurrentPage / (double)TotalPages) * 100 : 0;
        public string PrinterName { get; set; } = "";
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan? EstimatedRemaining { get; set; }
    }

    /// <summary>
    /// Status of a print job from the gateway.
    /// </summary>
    public class GatewayJobStatus
    {
        public Guid TicketId { get; set; }
        public PrintJobState State { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public DateTime? StartedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
