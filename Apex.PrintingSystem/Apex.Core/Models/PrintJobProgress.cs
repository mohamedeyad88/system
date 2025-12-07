using System;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents progress information for a print job.
    /// </summary>
    public class PrintJobProgress
    {
        /// <summary>
        /// The job ID this progress relates to.
        /// </summary>
        public int JobId { get; set; }

        /// <summary>
        /// Current page being printed (1-based).
        /// </summary>
        public int CurrentPage { get; set; }

        /// <summary>
        /// Total pages in the job.
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Progress percentage (0-100).
        /// </summary>
        public double PercentComplete => TotalPages > 0 ? (double)CurrentPage / TotalPages * 100 : 0;

        /// <summary>
        /// Estimated time remaining.
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        /// <summary>
        /// Pages printed per second.
        /// </summary>
        public double PagesPerSecond { get; set; }

        /// <summary>
        /// Current status message.
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// When the job started.
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Whether the job is currently paused.
        /// </summary>
        public bool IsPaused { get; set; }
    }
}
