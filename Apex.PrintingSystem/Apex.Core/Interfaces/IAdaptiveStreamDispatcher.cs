using System;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    /// <summary>
    /// 🔄 ADAPTIVE STREAMING DISPATCHER
    /// 
    /// Sends page data to printers with intelligent flow control.
    /// Adapts to network conditions, printer feedback, and buffer availability.
    /// </summary>
    public interface IAdaptiveStreamDispatcher
    {
        /// <summary>
        /// Stream pages to the printer with adaptive throttling.
        /// </summary>
        /// <param name="pageSource">Source of pages to stream</param>
        /// <param name="printerName">Target printer</param>
        /// <param name="settings">Print settings</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<StreamingResult> StreamToPrinterAsync(
            IPageSource pageSource,
            string printerName,
            StreamingSettings settings,
            IProgress<StreamingProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resume streaming from a specific page.
        /// </summary>
        Task<StreamingResult> ResumeStreamingAsync(
            IPageSource pageSource,
            string printerName,
            int resumeFromPage,
            StreamingSettings settings,
            IProgress<StreamingProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get current network/printer metrics.
        /// </summary>
        StreamingMetrics GetCurrentMetrics(string printerName);
    }

    /// <summary>
    /// Settings for streaming.
    /// </summary>
    public class StreamingSettings
    {
        /// <summary>
        /// Initial chunk size in bytes.
        /// </summary>
        public int InitialChunkSize { get; set; } = 64 * 1024; // 64KB

        /// <summary>
        /// Maximum chunk size in bytes.
        /// </summary>
        public int MaxChunkSize { get; set; } = 1024 * 1024; // 1MB

        /// <summary>
        /// Minimum chunk size in bytes.
        /// </summary>
        public int MinChunkSize { get; set; } = 16 * 1024; // 16KB

        /// <summary>
        /// Number of pages to buffer ahead.
        /// </summary>
        public int BufferAheadPages { get; set; } = 3;

        /// <summary>
        /// Timeout for printer response.
        /// </summary>
        public TimeSpan PrinterTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum retry attempts per page.
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Delay between retries.
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Enable adaptive chunk sizing based on network conditions.
        /// </summary>
        public bool AdaptiveChunkSizing { get; set; } = true;

        /// <summary>
        /// Print settings (duplex, color, etc).
        /// </summary>
        public PrintRequest? PrintSettings { get; set; }
    }

    /// <summary>
    /// Result of a streaming operation.
    /// </summary>
    public class StreamingResult
    {
        public bool Success { get; set; }
        public int PagesPrinted { get; set; }
        public int TotalPages { get; set; }
        public int LastSuccessfulPage { get; set; }
        public TimeSpan Duration { get; set; }
        public long BytesTransferred { get; set; }
        public string? ErrorMessage { get; set; }
        public Exception? Exception { get; set; }
        public bool CanResume { get; set; }
    }

    /// <summary>
    /// Progress during streaming.
    /// </summary>
    public class StreamingProgress
    {
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public double PercentComplete => TotalPages > 0 ? (CurrentPage / (double)TotalPages) * 100 : 0;
        public long BytesSent { get; set; }
        public double TransferRateBytesPerSecond { get; set; }
        public TimeSpan Elapsed { get; set; }
        public TimeSpan? EstimatedRemaining { get; set; }
        public StreamingState State { get; set; }
        public string? StatusMessage { get; set; }
    }

    /// <summary>
    /// Current streaming state.
    /// </summary>
    public enum StreamingState
    {
        Initializing,
        Streaming,
        Buffering,
        WaitingForPrinter,
        Paused,
        Retrying,
        Completed,
        Failed
    }

    /// <summary>
    /// Metrics for monitoring streaming performance.
    /// </summary>
    public class StreamingMetrics
    {
        public string PrinterName { get; set; } = "";
        public bool IsAvailable { get; set; }
        public int QueueDepth { get; set; }
        public double AverageTransferRateBps { get; set; }
        public double CurrentTransferRateBps { get; set; }
        public TimeSpan AveragePagePrintTime { get; set; }
        public int SuccessfulPages { get; set; }
        public int FailedPages { get; set; }
        public int RetryCount { get; set; }
        public DateTime LastActivityTime { get; set; }
        public NetworkQuality NetworkQuality { get; set; }
    }

    /// <summary>
    /// Network quality assessment.
    /// </summary>
    public enum NetworkQuality
    {
        Excellent,  // < 10ms latency, no packet loss
        Good,       // < 50ms latency, < 1% packet loss
        Fair,       // < 100ms latency, < 5% packet loss
        Poor,       // > 100ms latency or > 5% packet loss
        Unavailable
    }
}
