using System;

namespace Apex.Core.Models
{
    public class JobHistory
    {
        public int Id { get; set; }
        public int? PrintJobId { get; set; }
        public DateTime EventUtc { get; set; } = DateTime.UtcNow;
        public string EventType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
