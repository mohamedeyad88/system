using System;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents an archived print job stored for historical records.
    /// </summary>
    public class ArchivedPrintJob
    {
        public int Id { get; set; }
        
        /// <summary>
        /// Original job ID before archiving.
        /// </summary>
        public int OriginalJobId { get; set; }
        
        public string JobGuid { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string? OriginalFileName { get; set; }
        public string? TargetPrinterName { get; set; }
        public string? TargetPoolName { get; set; }
        public string FinalStatus { get; set; } = "";
        public int? Pages { get; set; }
        public int TotalCopies { get; set; }
        public int Attempts { get; set; }
        public string? ErrorMessage { get; set; }
        
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public DateTime ArchivedAtUtc { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Duration of the print job in seconds.
        /// </summary>
        public double? DurationSeconds { get; set; }
        
        /// <summary>
        /// Optional notes or reason for archiving.
        /// </summary>
        public string? ArchiveNotes { get; set; }
    }
}
