using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents a discovered printer with status and capabilities.
    /// </summary>
    public class PrinterInfo : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isChecked;
        private bool _isOnline;
        private int _queueLength;
        private string _statusText = "Unknown";
        private int _jobsToday;
        private double _loadPercent;

        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Local"; // Local, Network, Virtual
        public string PoolName { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether this printer is selected as the active printer for Quick Print.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this printer is checked for batch printing (multi-select).
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(); }
        }

        public bool IsOnline
        {
            get => _isOnline;
            set { _isOnline = value; OnPropertyChanged(); }
        }

        public int QueueLength
        {
            get => _queueLength;
            set { _queueLength = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Number of jobs printed today.
        /// </summary>
        public int JobsToday
        {
            get => _jobsToday;
            set { _jobsToday = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Current load percentage (0-100) for progress visualization.
        /// </summary>
        public double LoadPercent
        {
            get => _loadPercent;
            set { _loadPercent = value; OnPropertyChanged(); }
        }

        // Alert flags
        public bool HasPaperJam { get; set; }
        public bool IsOutOfPaper { get; set; }
        public bool IsTonerLow { get; set; }

        // Capabilities
        public bool IsDefault { get; set; }
        public bool CanDuplex { get; set; }
        public bool IsColor { get; set; }
        public List<string> SupportedPapers { get; set; } = new();

        public PrinterCapabilities Capabilities { get; set; } = new();

        // Helpers
        public string TypeBadge => Type switch
        {
            "Network" => "🌐 Network",
            "Virtual" => "💻 Virtual",
            _ => "🖨️ Local"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
