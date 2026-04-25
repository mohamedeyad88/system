using Apex.Core.Enums;
using System;

namespace Apex.Core.Models
{
    public class PrintJob
    {
        public int Id { get; set; }
        public string JobGuid { get; set; } = Guid.NewGuid().ToString();
        public int? SavedQueueId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? OriginalFileName { get; set; }
        public string? TargetPrinterName { get; set; }
        public string? TargetPoolName { get; set; }
        public string? OriginalFilePath { get; set; }
        public string Mode { get; set; } = "SeparateTask"; // 'MergedTask' | 'SeparateTask'
        public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;
        public int? Pages { get; set; }
        public string? OptionsJson { get; set; }
        public int Attempts { get; set; } = 0;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public string? ErrorMessage { get; set; }
        
        public int TotalCopies { get; set; } = 1;

        // === Print Settings ===
        
        /// <summary>
        /// Enable duplex (double-sided) printing.
        /// </summary>
        public bool Duplex { get; set; } = false;

        /// <summary>
        /// Enable color printing. False = grayscale.
        /// </summary>
        public bool Color { get; set; } = true;

        /// <summary>
        /// Page range to print (e.g., "1-5", "1,3,5", "All").
        /// </summary>
        public string PageRange { get; set; } = "All";

        /// <summary>
        /// Paper size (e.g., "A4", "Letter").
        /// </summary>
        public string PaperSize { get; set; } = "A4";

        /// <summary>
        /// Print orientation (Portrait or Landscape).
        /// </summary>
        public string Orientation { get; set; } = "Portrait";

        /// <summary>
        /// Print quality (Draft, Normal, High, Best).
        /// </summary>
        public string Quality { get; set; } = "Normal";
        
        /// <summary>
        /// Job priority for queue ordering. Higher priority jobs print first.
        /// </summary>
        public JobPriority Priority { get; set; } = JobPriority.Normal;

        /// <summary>
        /// Current page being printed (for progress tracking).
        /// </summary>
        public int CurrentPage { get; set; } = 0;

        /// <summary>
        /// Total pages to print.
        /// </summary>
        public int TotalPages { get; set; } = 0;

        /// <summary>
        /// Progress percentage (0-100).
        /// </summary>
        public double ProgressPercent => TotalPages > 0 ? (double)CurrentPage / TotalPages * 100 : 0;

        /// <summary>
        /// Scheduled start time for delayed jobs. Null means start immediately.
        /// </summary>
        public DateTime? ScheduledStartUtc { get; set; }

        /// <summary>
        /// Whether this job is scheduled for future execution.
        /// </summary>
        public bool IsScheduled => ScheduledStartUtc.HasValue && ScheduledStartUtc > DateTime.UtcNow;

        /// <summary>
        /// Whether this job is ready to be processed (not scheduled or schedule time has passed).
        /// </summary>
        public bool IsReadyToProcess => Status == PrintJobStatus.Pending && !IsScheduled;

        // Legacy/Compat properties (mapped or ignored)
        public string PrinterName 
        { 
            get => TargetPrinterName ?? string.Empty; 
            set => TargetPrinterName = value; 
        }
        
        public string FileName 
        { 
            get => OriginalFileName ?? System.IO.Path.GetFileName(FilePath); 
            set => OriginalFileName = value; 
        }

        public DateTime CreatedAt 
        { 
            get => CreatedAtUtc.ToLocalTime(); 
            set => CreatedAtUtc = value.ToUniversalTime(); 
        }
    }
}
