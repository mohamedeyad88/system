using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents a single file in a batch print job.
    /// </summary>
    public class BatchJob : INotifyPropertyChanged
    {
        private string _status = "Pending";
        private int _currentPage;
        private double _progressPercent;
        private double _pagesPerSecond;
        private string _eta = "";
        private ConversionStatus _conversionStatus = ConversionStatus.Pending;

        public string JobId { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string FilePath { get; set; } = string.Empty;
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public string OriginalExtension => System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
        
        // File info
        public string FileType { get; set; } = "Unknown";
        public string FileSize { get; set; } = "0 KB";
        public long FileSizeBytes { get; set; }
        public int EstimatedPages { get; set; } = 0;
        
        // Conversion
        public ConversionStatus ConversionStatus
        {
            get => _conversionStatus;
            set { _conversionStatus = value; OnPropertyChanged(); }
        }
        public string? ConvertedFilePath { get; set; }
        public string? ConversionError { get; set; }
        
        // Print status
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }
        
        public int CurrentPage
        {
            get => _currentPage;
            set { _currentPage = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); }
        }
        
        public int TotalPages { get; set; } = 0;
        
        public double ProgressPercent
        {
            get => TotalPages > 0 ? (double)CurrentPage / TotalPages * 100 : _progressPercent;
            set { _progressPercent = value; OnPropertyChanged(); }
        }
        
        public double PagesPerSecond
        {
            get => _pagesPerSecond;
            set { _pagesPerSecond = Math.Round(value, 1); OnPropertyChanged(); }
        }
        
        public string ETA
        {
            get => _eta;
            set { _eta = value; OnPropertyChanged(); }
        }
        
        // Error handling
        public string ErrorMessage { get; set; } = string.Empty;
        public int RetryCount { get; set; } = 0;
        public int MaxRetries { get; set; } = 3;
        
        // Timing
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan TimeTaken { get; set; }
        
        // Computed states
        public bool IsPending => Status == "Pending" || ConversionStatus == ConversionStatus.Pending;
        public bool IsConverting => ConversionStatus == ConversionStatus.Converting;
        public bool IsPrinting => Status == "Printing";
        public bool IsCompleted => Status == "Completed";
        public bool IsFailed => Status == "Failed" || ConversionStatus == ConversionStatus.Failed;
        
        /// <summary>
        /// Gets the file path to use for printing (converted or original for PDFs).
        /// </summary>
        public string PrintFilePath => ConvertedFilePath ?? FilePath;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
