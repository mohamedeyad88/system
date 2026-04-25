using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Apex.Services.Printing.VendorDetection
{
    /// <summary>
    /// Silent vendor detection engine.
    /// Automatically identifies printer manufacturer and capabilities.
    /// Runs entirely in the background with no user interaction.
    /// </summary>
    public class VendorDetectionEngine
    {
        private static readonly Lazy<VendorDetectionEngine> _instance = 
            new(() => new VendorDetectionEngine());
        
        public static VendorDetectionEngine Instance => _instance.Value;
        
        // Cache of detected printers (thread-safe)
        private readonly ConcurrentDictionary<string, PrinterMetadata> _printerCache = new();
        
        // Vendor detection patterns (case-insensitive)
        private static readonly Dictionary<PrinterVendor, string[]> VendorPatterns = new()
        {
            [PrinterVendor.HP] = new[] 
            { 
                "hp", "hewlett", "packard", "laserjet", "deskjet", "officejet", 
                "photosmart", "envy", "pagewide", "designjet"
            },
            [PrinterVendor.Epson] = new[] 
            { 
                "epson", "workforce", "ecotank", "surecolor", "stylus", 
                "expression", "picturemate"
            },
            [PrinterVendor.Canon] = new[] 
            { 
                "canon", "pixma", "imagerunner", "imageclass", "selphy",
                "maxify", "megatank"
            },
            [PrinterVendor.Brother] = new[] 
            { 
                "brother", "mfc", "hl-", "dcp-"
            },
            [PrinterVendor.Xerox] = new[] 
            { 
                "xerox", "phaser", "workcentre", "versalink", "altalink"
            },
            [PrinterVendor.Ricoh] = new[] 
            { 
                "ricoh", "aficio", "mp c", "sp "
            }
        };
        
        // Print language detection patterns
        private static readonly Dictionary<PrintLanguage, string[]> LanguagePatterns = new()
        {
            [PrintLanguage.PCL] = new[] { "pcl", "pcl6", "pcl5", "pcl 6", "pcl 5" },
            [PrintLanguage.PostScript] = new[] { "postscript", "ps", "ps3", "adobe" },
            [PrintLanguage.ESCPOS] = new[] { "esc/pos", "escpos", "pos printer" },
            [PrintLanguage.ESCPage] = new[] { "esc/page", "escpage", "esc/p" },
            [PrintLanguage.PDF] = new[] { "pdf", "direct pdf" },
            [PrintLanguage.XPS] = new[] { "xps", "xml paper" }
        };
        
        private VendorDetectionEngine() { }
        
        /// <summary>
        /// Detect all printers silently and cache results.
        /// </summary>
        public async Task<IReadOnlyList<PrinterMetadata>> DetectAllPrintersAsync()
        {
            return await Task.Run(() =>
            {
                var results = new List<PrinterMetadata>();
                
                try
                {
                    // Get printers via WMI for detailed info
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT * FROM Win32_Printer");
                    
                    foreach (ManagementObject printer in searcher.Get())
                    {
                        try
                        {
                            var metadata = ExtractMetadata(printer);
                            _printerCache[metadata.Name] = metadata;
                            results.Add(metadata);
                        }
                        catch
                        {
                            // Skip problematic printers silently
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WMI detection failed: {ex.Message}");
                    
                    // Fallback to basic detection
                    foreach (string printerName in PrinterSettings.InstalledPrinters)
                    {
                        var metadata = DetectFromNameOnly(printerName);
                        _printerCache[metadata.Name] = metadata;
                        results.Add(metadata);
                    }
                }
                
                return results;
            });
        }
        
        /// <summary>
        /// Get cached metadata for a specific printer.
        /// If not cached, performs quick detection.
        /// </summary>
        public PrinterMetadata GetPrinterMetadata(string printerName)
        {
            if (_printerCache.TryGetValue(printerName, out var cached))
            {
                // Refresh if older than 5 minutes
                if ((DateTime.UtcNow - cached.LastUpdated).TotalMinutes < 5)
                    return cached;
            }
            
            // Quick detection for single printer
            var metadata = DetectSinglePrinter(printerName);
            _printerCache[printerName] = metadata;
            return metadata;
        }
        
        /// <summary>
        /// Detect vendor from printer name/driver.
        /// </summary>
        public PrinterVendor DetectVendor(string printerName, string? driverName = null)
        {
            var searchText = $"{printerName} {driverName ?? ""}".ToLowerInvariant();
            
            foreach (var (vendor, patterns) in VendorPatterns)
            {
                if (patterns.Any(p => searchText.Contains(p)))
                    return vendor;
            }
            
            return PrinterVendor.Generic;
        }
        
        /// <summary>
        /// Extract complete metadata from WMI object.
        /// </summary>
        private PrinterMetadata ExtractMetadata(ManagementObject printer)
        {
            var name = GetWmiString(printer, "Name");
            var driverName = GetWmiString(printer, "DriverName");
            var portName = GetWmiString(printer, "PortName");
            
            var metadata = new PrinterMetadata
            {
                Name = name,
                DriverName = driverName,
                PortName = portName,
                Model = GetWmiString(printer, "Caption"),
                ShareName = GetWmiString(printer, "ShareName"),
                IsOnline = !GetWmiBool(printer, "WorkOffline"),
                SupportsColor = GetWmiBool(printer, "Color"),
                LastUpdated = DateTime.UtcNow
            };
            
            // Detect vendor
            metadata.Vendor = DetectVendor(name, driverName);
            metadata.DetectionConfidence = CalculateConfidence(metadata.Vendor, name, driverName);
            
            // Detect connection type
            metadata.ConnectionType = DetectConnectionType(portName);
            metadata.IsNetworkPrinter = metadata.ConnectionType == PrinterConnectionType.Network ||
                                        metadata.ConnectionType == PrinterConnectionType.WiFi;
            
            // Detect print language
            metadata.PrintLanguage = DetectPrintLanguage(driverName);
            
            // Check for duplex support
            metadata.SupportsDuplex = GetWmiBool(printer, "Duplex") || 
                                      driverName.ToLowerInvariant().Contains("duplex");
            
            // Check for direct PDF support (HP/Xerox enterprise printers)
            metadata.SupportsDirectPdf = DetectDirectPdfSupport(metadata);
            
            return metadata;
        }
        
        /// <summary>
        /// Quick detection from printer name only (fallback).
        /// </summary>
        private PrinterMetadata DetectFromNameOnly(string printerName)
        {
            var vendor = DetectVendor(printerName);
            
            return new PrinterMetadata
            {
                Name = printerName,
                Vendor = vendor,
                DetectionConfidence = 50, // Lower confidence without WMI
                ConnectionType = PrinterConnectionType.Unknown,
                PrintLanguage = PrintLanguage.GDI,
                IsOnline = true, // Assume online
                LastUpdated = DateTime.UtcNow
            };
        }
        
        /// <summary>
        /// Detect single printer via WMI.
        /// </summary>
        private PrinterMetadata DetectSinglePrinter(string printerName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Printer WHERE Name = '{printerName.Replace("'", "''")}'");
                
                foreach (ManagementObject printer in searcher.Get())
                {
                    return ExtractMetadata(printer);
                }
            }
            catch
            {
                // Fallback silently
            }
            
            return DetectFromNameOnly(printerName);
        }
        
        /// <summary>
        /// Detect connection type from port name.
        /// </summary>
        private PrinterConnectionType DetectConnectionType(string portName)
        {
            if (string.IsNullOrEmpty(portName))
                return PrinterConnectionType.Unknown;
            
            var port = portName.ToUpperInvariant();
            
            // USB ports
            if (port.StartsWith("USB") || port.Contains("DOT4"))
                return PrinterConnectionType.USB;
            
            // Network ports (IP address pattern)
            if (Regex.IsMatch(port, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}"))
                return PrinterConnectionType.Network;
            
            // WSD (Web Services for Devices) - network
            if (port.StartsWith("WSD") || port.Contains("WSDPRINT"))
                return PrinterConnectionType.Network;
            
            // TCP/IP port
            if (port.Contains("TCP") || port.Contains("IP_"))
                return PrinterConnectionType.Network;
            
            // Parallel/LPT
            if (port.StartsWith("LPT"))
                return PrinterConnectionType.Parallel;
            
            // Serial/COM
            if (port.StartsWith("COM"))
                return PrinterConnectionType.Serial;
            
            // File/PDF printer
            if (port.Contains("FILE") || port.Contains("PDF") || port.Contains("XPS"))
                return PrinterConnectionType.Unknown;
            
            return PrinterConnectionType.Unknown;
        }
        
        /// <summary>
        /// Detect print language from driver name.
        /// </summary>
        private PrintLanguage DetectPrintLanguage(string driverName)
        {
            if (string.IsNullOrEmpty(driverName))
                return PrintLanguage.GDI;
            
            var driver = driverName.ToLowerInvariant();
            
            foreach (var (language, patterns) in LanguagePatterns)
            {
                if (patterns.Any(p => driver.Contains(p)))
                    return language;
            }
            
            // Default to GDI for Windows printers
            return PrintLanguage.GDI;
        }
        
        /// <summary>
        /// Check if printer supports direct PDF printing.
        /// </summary>
        private bool DetectDirectPdfSupport(PrinterMetadata metadata)
        {
            // Enterprise HP printers often support direct PDF
            if (metadata.Vendor == PrinterVendor.HP)
            {
                var name = metadata.Name.ToLowerInvariant();
                if (name.Contains("enterprise") || name.Contains("mfp") || 
                    name.Contains("m6") || name.Contains("m5"))
                    return true;
            }
            
            // Xerox enterprise printers
            if (metadata.Vendor == PrinterVendor.Xerox)
                return true;
            
            // Check driver name
            if (metadata.DriverName.ToLowerInvariant().Contains("pdf"))
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Calculate detection confidence score.
        /// </summary>
        private int CalculateConfidence(PrinterVendor vendor, string name, string driverName)
        {
            if (vendor == PrinterVendor.Generic)
                return 30;
            
            var searchText = $"{name} {driverName}".ToLowerInvariant();
            var patterns = VendorPatterns[vendor];
            var matchCount = patterns.Count(p => searchText.Contains(p));
            
            // More matches = higher confidence
            return Math.Min(100, 50 + (matchCount * 15));
        }
        
        private static string GetWmiString(ManagementObject obj, string property)
        {
            try
            {
                return obj[property]?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        
        private static bool GetWmiBool(ManagementObject obj, string property)
        {
            try
            {
                return obj[property] as bool? ?? false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Clear the printer cache (force re-detection).
        /// </summary>
        public void ClearCache()
        {
            _printerCache.Clear();
        }
    }
}
