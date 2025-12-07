using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents a distribution print job - one file sent to multiple printers in parallel.
    /// </summary>
    public class DistributionPrintJob
    {
        public string JobId { get; set; } = Guid.NewGuid().ToString();
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileType { get; set; } = string.Empty;
        public int PageCount { get; set; }
        
        public List<string> SelectedPrinters { get; set; } = new();
        public Dictionary<string, PrintResult> PrinterResults { get; set; } = new();
        
        public string Status { get; set; } = "Pending"; // Pending, Printing, Completed, Failed
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        
        // Print Settings
        public int Copies { get; set; } = 1;
        public bool ColorMode { get; set; } = true;
        public bool DuplexEnabled { get; set; } = false;
        public string PageRange { get; set; } = "All";
        public bool ParallelMode { get; set; } = true;
        
        // Statistics
        public int SuccessCount => PrinterResults.Values.Count(r => r.Success);
        public int FailedCount => PrinterResults.Values.Count(r => !r.Success);
        public int TotalPrinters => SelectedPrinters.Count;
        
        public TimeSpan TotalDuration => EndTime.HasValue && StartTime.HasValue 
            ? EndTime.Value - StartTime.Value 
            : TimeSpan.Zero;
    }
}
