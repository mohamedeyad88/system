namespace Apex.Core.Models
{
    public class DistributionSettings
    {
        public int Copies { get; set; } = 1;
        public bool Duplex { get; set; } = false;
        public bool ColorMode { get; set; } = true; // true = Color, false = Mono
        public string PageRange { get; set; } = "All";
        public bool ParallelMode { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 15;
        public int RetryCount { get; set; } = 0;
        public bool SkipOffline { get; set; } = false;
        public string LogLevel { get; set; } = "Normal";
    }
}
