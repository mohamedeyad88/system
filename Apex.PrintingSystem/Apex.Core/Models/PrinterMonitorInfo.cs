using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents a printer with live monitoring information for distribution view.
    /// </summary>
    public partial class PrinterMonitorInfo : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _status = "Unknown"; // Online, Offline, Busy

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private int _queueLength;

        [ObservableProperty]
        private bool _supportsColor;

        [ObservableProperty]
        private bool _supportsDuplex;

        [ObservableProperty]
        private List<string> _paperSizes = new();

        [ObservableProperty]
        private DateTime? _lastPrintTime;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private string? _driver;

        [ObservableProperty]
        private string? _location;

        public string StatusIcon => Status switch
        {
            "Online" => "🟢",
            "Offline" => "🔴",
            "Busy" => "🟡",
            _ => "⚪"
        };

        public string CapabilitiesText
        {
            get
            {
                var caps = new List<string>();
                if (SupportsColor) caps.Add("Color");
                if (SupportsDuplex) caps.Add("Duplex");
                return caps.Count > 0 ? string.Join(" • ", caps) : "Basic";
            }
        }
    }
}
