namespace Apex.Core.Models
{
    public class BatchSettings
    {
        public int Copies { get; set; } = 1;
        public bool Duplex { get; set; } = false;
        public bool ColorMode { get; set; } = true; // true = Color, false = Mono
        public string PageRange { get; set; } = "All";
        public string PrintOrder { get; set; } = "Original"; // Original, Name, Size
        public int DelayBetweenJobsMs { get; set; } = 1000;
        public int RetryCount { get; set; } = 0;
        public bool StopOnError { get; set; } = true;
        public bool OptimizedMode { get; set; } = false;
    }
}
