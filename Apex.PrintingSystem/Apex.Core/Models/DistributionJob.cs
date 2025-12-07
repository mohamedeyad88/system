using System;

namespace Apex.Core.Models
{
    public class DistributionJob
    {
        public string PrinterName { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Pending, Printing, Completed, Failed
        public string JobId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public int RetryCount { get; set; } = 0;
        public TimeSpan TimeTaken { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
    }
}
