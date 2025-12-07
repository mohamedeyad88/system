using System;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents the result of printing to a single printer in a distribution job.
    /// </summary>
    public class PrintResult
    {
        public string PrinterName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? JobId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;
        public string? ErrorMessage { get; set; }
        public int PagesCompleted { get; set; }
        
        public string StatusText => Success ? "✓ Completed" : "✗ Failed";
        public string DurationText => Duration.TotalSeconds > 0 ? $"{Duration.TotalSeconds:F1}s" : "—";
    }
}
