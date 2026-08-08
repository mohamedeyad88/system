using System;

namespace Apex.Services.Printing.Queue
{
    /// <summary>
    /// Print job state machine - clearly defined states for reliable tracking.
    /// Each job MUST progress through these states sequentially.
    /// </summary>
    public enum PrintJobState
    {
        /// <summary>Job is in queue, waiting for execution.</summary>
        Queued = 0,

        /// <summary>Job is being prepared for printing (loading file, analyzing, etc.).</summary>
        Preparing = 1,

        /// <summary>Job is actively sending data to printer.</summary>
        Sending = 2,

        /// <summary>Data sent, printer is processing.</summary>
        Printing = 3,

        /// <summary>Job completed successfully.</summary>
        Completed = 4,

        /// <summary>Job failed (network error, printer error, etc.).</summary>
        Failed = 5,

        /// <summary>Job is being retried after a failure.</summary>
        Retrying = 6,

        /// <summary>Job was cancelled by user.</summary>
        Cancelled = 7,

        /// <summary>Job is paused (manual or automatic).</summary>
        Paused = 8,

        /// <summary>Job is waiting for dependency to be completed.</summary>
        WaitingForDependency = 9,

        /// <summary>Job was skipped (logically complete for dependents).</summary>
        Skipped = 10
    }

    /// <summary>
    /// Represents a single print job in the queue system.
    /// Contains all metadata needed for reliable execution, tracking, and recovery.
    /// </summary>
    public class PrintJob
    {
        /// <summary>Unique identifier for this job.</summary>
        public string JobId { get; init; } = Guid.NewGuid().ToString();

        /// <summary>User-friendly job name.</summary>
        public string JobName { get; set; } = string.Empty;

        /// <summary>Target printer name.</summary>
        public string PrinterName { get; set; } = string.Empty;

        /// <summary>Path to file to print.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Number of copies requested.</summary>
        public int Copies { get; init; } = 1;

        /// <summary>Current state of the job.</summary>
        public PrintJobState State { get; set; } = PrintJobState.Queued;

        /// <summary>Time when job was created/queued.</summary>
        public DateTime QueuedAt { get; init; } = DateTime.UtcNow;

        /// <summary>Time when job started executing.</summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>Time when job completed (success or failure).</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>Current progress (0-100).</summary>
        public int Progress { get; set; } = 0;

        /// <summary>Number of retry attempts made.</summary>
        public int RetryCount { get; set; } = 0;

        /// <summary>Maximum retry attempts allowed.</summary>
        public int MaxRetries { get; init; } = 3;

        /// <summary>Error message if job failed.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Last exception that occurred.</summary>
        public Exception? LastException { get; set; }

        /// <summary>Current status message for UI display.</summary>
        public string StatusMessage { get; set; } = "Queued";

        /// <summary>Priority (higher = more important). Default: 5.</summary>
        public int Priority { get; init; } = 5;

        /// <summary>Total pages in document (if known).</summary>
        public int? TotalPages { get; set; }

        /// <summary>Pages printed so far.</summary>
        public int PagesPrinted { get; set; } = 0;

        /// <summary>File size in bytes.</summary>
        public long FileSize { get; set; }

        /// <summary>Bytes sent so far.</summary>
        public long BytesSent { get; set; }

        /// <summary>Additional metadata for custom handling.</summary>
        public Dictionary<string, object> Metadata { get; init; } = new();

        /// <summary>
        /// When true, the job must be sent to the printer as a single document
        /// without per-page rendering/loops (driver decides trays/settings).
        /// </summary>
        public bool UseRawDocumentMode { get; set; } = false;

        /// <summary>Estimated time remaining (calculated dynamically).</summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        /// <summary>
        /// Checks if job can be retried.
        /// </summary>
        public bool CanRetry => RetryCount < MaxRetries && State == PrintJobState.Failed;

        /// <summary>
        /// Checks if job is in a terminal state (completed, failed, cancelled, skipped).
        /// </summary>
        public bool IsTerminal => State is PrintJobState.Completed
                                       or PrintJobState.Failed
                                       or PrintJobState.Cancelled
                                       or PrintJobState.Skipped;

        /// <summary>
        /// Checks if job is currently active (preparing, sending, printing).
        /// </summary>
        public bool IsActive => State is PrintJobState.Preparing
                                     or PrintJobState.Sending
                                     or PrintJobState.Printing
                                     or PrintJobState.Retrying;

        /// <summary>Optional dependency on another job (by JobId).</summary>
        public string? DependsOnJobId { get; set; }

        /// <summary>If true, parent Skipped counts as completed for dependency resolution.</summary>
        public bool TreatSkippedAsComplete { get; set; } = true;

        /// <summary>
        /// Determines if dependency is satisfied given parent job state.
        /// CompletedLogical = Completed OR (Skipped && TreatSkippedAsComplete)
        /// </summary>
        public bool IsDependencySatisfied(PrintJob? parent)
        {
            if (string.IsNullOrEmpty(DependsOnJobId))
                return true;

            if (parent == null)
                return false;

            if (parent.State == PrintJobState.Completed)
                return true;

            if (TreatSkippedAsComplete && parent.State == PrintJobState.Skipped)
                return true;

            return false;
        }

        /// <summary>
        /// Calculate elapsed time since job started.
        /// </summary>
        public TimeSpan? ElapsedTime => StartedAt.HasValue
            ? (CompletedAt ?? DateTime.UtcNow) - StartedAt.Value
            : null;
    }
}
