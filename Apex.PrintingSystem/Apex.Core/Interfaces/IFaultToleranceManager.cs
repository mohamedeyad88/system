using System;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    /// <summary>
    /// 🛡️ FAULT TOLERANCE MANAGER
    /// 
    /// Monitors printer and network states.
    /// Automatically pauses when issues occur.
    /// Enables resumption from last successful page.
    /// </summary>
    public interface IFaultToleranceManager
    {
        /// <summary>
        /// Create a checkpoint for a print job.
        /// </summary>
        Task<PrintCheckpoint> CreateCheckpointAsync(Guid jobId, int lastSuccessfulPage);

        /// <summary>
        /// Get the last checkpoint for a job.
        /// </summary>
        Task<PrintCheckpoint?> GetCheckpointAsync(Guid jobId);

        /// <summary>
        /// Clear checkpoint after successful completion.
        /// </summary>
        Task ClearCheckpointAsync(Guid jobId);

        /// <summary>
        /// Monitor printer health and trigger appropriate actions.
        /// </summary>
        Task<PrinterHealthStatus> CheckPrinterHealthAsync(string printerName);

        /// <summary>
        /// Classify an error and determine recovery strategy.
        /// </summary>
        ErrorClassification ClassifyError(Exception exception, string printerName);

        /// <summary>
        /// Event raised when printer issue is detected.
        /// </summary>
        event EventHandler<PrinterIssueEventArgs> PrinterIssueDetected;

        /// <summary>
        /// Event raised when printer recovers.
        /// </summary>
        event EventHandler<PrinterRecoveryEventArgs> PrinterRecovered;
    }

    /// <summary>
    /// A checkpoint representing print job progress.
    /// </summary>
    public class PrintCheckpoint
    {
        public Guid JobId { get; set; }
        public Guid TicketId { get; set; }
        public string FilePath { get; set; } = "";
        public string PrinterName { get; set; } = "";
        public int LastSuccessfulPage { get; set; }
        public int TotalPages { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? SettingsJson { get; set; }
        public CheckpointState State { get; set; }
    }

    /// <summary>
    /// Checkpoint state.
    /// </summary>
    public enum CheckpointState
    {
        Active,
        Paused,
        Resumable,
        Completed,
        Failed
    }

    /// <summary>
    /// Printer health status.
    /// </summary>
    public class PrinterHealthStatus
    {
        public string PrinterName { get; set; } = "";
        public bool IsOnline { get; set; }
        public bool IsReady { get; set; }
        public bool HasError { get; set; }
        public PrinterErrorType? ErrorType { get; set; }
        public string? ErrorMessage { get; set; }
        public int QueueDepth { get; set; }
        public bool IsPaperLow { get; set; }
        public bool IsTonerLow { get; set; }
        public bool HasPaperJam { get; set; }
        public NetworkQuality NetworkQuality { get; set; }
        public DateTime CheckedAt { get; set; }
    }

    /// <summary>
    /// Types of printer errors.
    /// </summary>
    public enum PrinterErrorType
    {
        None,
        Offline,
        PaperJam,
        OutOfPaper,
        OutOfToner,
        CoverOpen,
        NetworkError,
        SpoolerError,
        DriverError,
        Unknown
    }

    /// <summary>
    /// Error classification for recovery strategy.
    /// </summary>
    public class ErrorClassification
    {
        public ErrorSeverity Severity { get; set; }
        public RecoveryStrategy Strategy { get; set; }
        public bool IsRecoverable { get; set; }
        public bool ShouldRetry { get; set; }
        public int RecommendedRetryCount { get; set; }
        public TimeSpan RecommendedRetryDelay { get; set; }
        public string UserFriendlyMessage { get; set; } = "";
        public string TechnicalDetails { get; set; } = "";
    }

    /// <summary>
    /// Error severity levels.
    /// </summary>
    public enum ErrorSeverity
    {
        Transient,      // Temporary, will likely resolve on retry
        Recoverable,    // Needs intervention but can continue
        Critical,       // Job cannot continue
        Fatal           // Affects entire printing system
    }

    /// <summary>
    /// Recovery strategies.
    /// </summary>
    public enum RecoveryStrategy
    {
        Retry,              // Retry immediately
        RetryWithDelay,     // Wait and retry
        Pause,              // Pause and wait for user/system action
        SwitchPrinter,      // Try alternate printer
        ResubmitJob,        // Resubmit entire job
        Abort               // Cannot recover
    }

    /// <summary>
    /// Event args for printer issues.
    /// </summary>
    public class PrinterIssueEventArgs : EventArgs
    {
        public string PrinterName { get; set; } = "";
        public PrinterErrorType ErrorType { get; set; }
        public string Message { get; set; } = "";
        public Guid? AffectedJobId { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    /// <summary>
    /// Event args for printer recovery.
    /// </summary>
    public class PrinterRecoveryEventArgs : EventArgs
    {
        public string PrinterName { get; set; } = "";
        public DateTime RecoveredAt { get; set; }
        public TimeSpan DowntimeDuration { get; set; }
    }
}
