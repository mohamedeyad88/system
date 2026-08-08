using System;

namespace Apex.Services.Printing.VendorDetection
{
    /// <summary>
    /// Complete metadata about a detected printer.
    /// Collected silently in the background.
    /// </summary>
    public class PrinterMetadata
    {
        /// <summary>Raw printer name as reported by Windows.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Detected vendor/manufacturer.</summary>
        public PrinterVendor Vendor { get; set; } = PrinterVendor.Generic;

        /// <summary>Original manufacturer string from driver.</summary>
        public string Manufacturer { get; set; } = string.Empty;

        /// <summary>Printer model name.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Driver name.</summary>
        public string DriverName { get; set; } = string.Empty;

        /// <summary>Driver version.</summary>
        public string DriverVersion { get; set; } = string.Empty;

        /// <summary>Port name (e.g., USB001, 192.168.1.100).</summary>
        public string PortName { get; set; } = string.Empty;

        /// <summary>Detected connection type.</summary>
        public PrinterConnectionType ConnectionType { get; set; } = PrinterConnectionType.Unknown;

        /// <summary>Detected print language support.</summary>
        public PrintLanguage PrintLanguage { get; set; } = PrintLanguage.Unknown;

        /// <summary>Is this a network printer?</summary>
        public bool IsNetworkPrinter { get; set; }

        /// <summary>Is the printer currently online/ready?</summary>
        public bool IsOnline { get; set; }

        /// <summary>Does printer support duplex?</summary>
        public bool SupportsDuplex { get; set; }

        /// <summary>Does printer support color?</summary>
        public bool SupportsColor { get; set; }

        /// <summary>Does printer support direct PDF?</summary>
        public bool SupportsDirectPdf { get; set; }

        /// <summary>Printer share name if shared.</summary>
        public string? ShareName { get; set; }

        /// <summary>When was this metadata last updated?</summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>Confidence score of vendor detection (0-100).</summary>
        public int DetectionConfidence { get; set; } = 0;

        /// <summary>Printer capabilities for RIP decision making.</summary>
        public PrinterCapabilities Capabilities { get; set; } = new();

        /// <summary>
        /// Get a user-friendly description (without technical details).
        /// </summary>
        public string GetFriendlyDescription()
        {
            var status = IsOnline ? "متصل" : "غير متصل";
            var connection = IsNetworkPrinter ? "شبكة" : "محلي";
            return $"{Name} ({connection}) - {status}";
        }

        public override string ToString()
        {
            return $"{Name} [{Vendor}] via {ConnectionType} ({PrintLanguage})";
        }
    }

    /// <summary>
    /// Printer capabilities for RIP decision engine.
    /// </summary>
    public class PrinterCapabilities
    {
        public bool SupportsPostScript { get; set; }
        public bool SupportsPcl { get; set; }
        public bool SupportsPdf { get; set; }
        public bool SupportsEscP { get; set; }
        public int MaxDpi { get; set; } = 600;
        public bool SupportsColor { get; set; }
        public bool SupportsDuplex { get; set; }
    }
}
