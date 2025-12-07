using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Printer capabilities for determining optimal print path.
    /// </summary>
    public record PrinterCapabilities(
        string PrinterName,
        bool SupportsPclMacros,
        bool SupportsPostScript,
        bool SupportsIpp,
        bool SupportsStoredForms,
        long MemoryBytes,
        int MaxDpi,
        string PrinterLanguage
    );

    /// <summary>
    /// Detects printer capabilities to determine optimal print strategy.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class PrinterCapabilityDetector
    {
        private readonly Dictionary<string, PrinterCapabilities> _cache = new();

        /// <summary>
        /// Detects capabilities for the specified printer.
        /// </summary>
        public PrinterCapabilities Detect(string printerName)
        {
            if (_cache.TryGetValue(printerName, out var cached))
                return cached;

            var capabilities = DetectInternal(printerName);
            _cache[printerName] = capabilities;
            return capabilities;
        }

        /// <summary>
        /// Gets all installed printers with their capabilities.
        /// </summary>
        public IEnumerable<PrinterCapabilities> GetAllPrinters()
        {
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                yield return Detect(printer);
            }
        }

        /// <summary>
        /// Checks if a printer supports native template storage (macros/forms).
        /// </summary>
        public bool SupportsNativeTemplates(string printerName)
        {
            var caps = Detect(printerName);
            return caps.SupportsStoredForms || caps.SupportsPclMacros || caps.SupportsPostScript;
        }

        private PrinterCapabilities DetectInternal(string printerName)
        {
            bool supportsPcl = false;
            bool supportsPs = false;
            bool supportsIpp = false;
            bool supportsStoredForms = false;
            long memoryBytes = 0;
            int maxDpi = 300;
            string language = "Unknown";

            try
            {
                // Query WMI for printer info
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Printer WHERE Name = '{printerName.Replace("\\", "\\\\")}'");

                foreach (ManagementObject printer in searcher.Get())
                {
                    // Get printer language
                    var capabilities = printer["CapabilityDescriptions"] as string[];
                    var languageSupported = printer["LanguagesSupported"] as ushort[];

                    if (languageSupported != null)
                    {
                        // Language codes: 1=Other, 2=Unknown, 3=PCL, 4=HPGL, 5=PJL, 
                        // 6=PS, 7=PSPrinter, 8=IPDS, 9=PPDS, etc.
                        supportsPcl = languageSupported.Contains((ushort)3) || languageSupported.Contains((ushort)5);
                        supportsPs = languageSupported.Contains((ushort)6) || languageSupported.Contains((ushort)7);
                    }

                    // Check for specific keywords in driver name or capabilities
                    var driverName = printer["DriverName"]?.ToString() ?? "";
                    if (driverName.Contains("PCL", StringComparison.OrdinalIgnoreCase))
                        supportsPcl = true;
                    if (driverName.Contains("PostScript", StringComparison.OrdinalIgnoreCase) ||
                        driverName.Contains("PS", StringComparison.OrdinalIgnoreCase))
                        supportsPs = true;

                    // Get resolution
                    var horizontalRes = printer["HorizontalResolution"];
                    if (horizontalRes != null)
                        maxDpi = Convert.ToInt32(horizontalRes);

                    // Determine language string
                    if (supportsPcl && supportsPs)
                        language = "PCL/PostScript";
                    else if (supportsPcl)
                        language = "PCL";
                    else if (supportsPs)
                        language = "PostScript";
                    else
                        language = "GDI";

                    // Check for stored forms support (heuristic)
                    // Production printers typically have more memory and support macros
                    supportsStoredForms = (supportsPcl || supportsPs) && memoryBytes > 64 * 1024 * 1024;
                }
            }
            catch (Exception)
            {
                // WMI query failed, return defaults
            }

            // Also check PrinterSettings for additional info
            try
            {
                var settings = new PrinterSettings { PrinterName = printerName };
                if (settings.IsValid)
                {
                    maxDpi = Math.Max(maxDpi, settings.DefaultPageSettings.PrinterResolution.X);
                }
            }
            catch { }

            return new PrinterCapabilities(
                PrinterName: printerName,
                SupportsPclMacros: supportsPcl,
                SupportsPostScript: supportsPs,
                SupportsIpp: supportsIpp,
                SupportsStoredForms: supportsStoredForms,
                MemoryBytes: memoryBytes,
                MaxDpi: maxDpi,
                PrinterLanguage: language
            );
        }

        /// <summary>
        /// Clears the capability cache for a printer.
        /// </summary>
        public void ClearCache(string? printerName = null)
        {
            if (printerName != null)
                _cache.Remove(printerName);
            else
                _cache.Clear();
        }
    }
}
