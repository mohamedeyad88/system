namespace Apex.Core.Models
{
    /// <summary>
    /// Result of a document conversion operation.
    /// </summary>
    public class ConversionResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public int EstimatedPages { get; set; }
        public string? ErrorMessage { get; set; }
        public ConversionStatus Status { get; set; } = ConversionStatus.Pending;
        public long FileSizeBytes { get; set; }
        public TimeSpan ConversionTime { get; set; }

        public static ConversionResult Failed(string originalPath, string error) => new()
        {
            Success = false,
            OriginalPath = originalPath,
            ErrorMessage = error,
            Status = ConversionStatus.Failed
        };

        public static ConversionResult Succeeded(string originalPath, string outputPath, int pages = 0) => new()
        {
            Success = true,
            OriginalPath = originalPath,
            OutputPath = outputPath,
            EstimatedPages = pages,
            Status = ConversionStatus.Ready
        };
    }

    /// <summary>
    /// Status of document conversion.
    /// </summary>
    public enum ConversionStatus
    {
        Pending,
        Converting,
        Ready,
        Failed,
        Skipped // For already-PDF files
    }
}
