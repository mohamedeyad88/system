using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.Versioning;

namespace Apex.Services.Printing
{
    /// <summary>
    /// Represents a printer tray/input bin.
    /// </summary>
    public class PrinterTray
    {
        public string Name { get; set; } = "";
        public PaperSourceKind Kind { get; set; }
        public bool IsAvailable { get; set; }
        public int Index { get; set; }
    }

    /// <summary>
    /// Service for detecting available printer trays/input bins.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class PrinterTrayDetectionService
    {
        private readonly Dictionary<string, List<PrinterTray>> _cache = new();

        /// <summary>
        /// Gets all available trays for the specified printer.
        /// </summary>
        public List<PrinterTray> GetAvailableTrays(string printerName)
        {
            if (string.IsNullOrEmpty(printerName))
                return new List<PrinterTray>();

            // Check cache
            if (_cache.TryGetValue(printerName, out var cached))
                return cached;

            var trays = DetectTrays(printerName);
            _cache[printerName] = trays;
            return trays;
        }

        /// <summary>
        /// Checks if a printer supports multiple trays.
        /// </summary>
        public bool SupportsMultipleTrays(string printerName)
        {
            var trays = GetAvailableTrays(printerName);
            return trays.Count > 1;
        }

        /// <summary>
        /// Gets a tray by name or index.
        /// </summary>
        public PrinterTray? GetTray(string printerName, string trayName)
        {
            var trays = GetAvailableTrays(printerName);
            return trays.FirstOrDefault(t =>
                t.Name.Equals(trayName, StringComparison.OrdinalIgnoreCase) ||
                t.Kind.ToString().Equals(trayName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a tray by PaperSourceKind.
        /// </summary>
        public PrinterTray? GetTrayByKind(string printerName, PaperSourceKind kind)
        {
            var trays = GetAvailableTrays(printerName);
            return trays.FirstOrDefault(t => t.Kind == kind);
        }

        /// <summary>
        /// Clears the cache for a specific printer (useful after printer configuration changes).
        /// </summary>
        public void ClearCache(string printerName)
        {
            _cache.Remove(printerName);
        }

        /// <summary>
        /// Clears all cached tray information.
        /// </summary>
        public void ClearAllCache()
        {
            _cache.Clear();
        }

        private List<PrinterTray> DetectTrays(string printerName)
        {
            var trays = new List<PrinterTray>();

            try
            {
                using var printDoc = new PrintDocument();
                printDoc.PrinterSettings.PrinterName = printerName;

                // Check if printer is valid
                if (!printDoc.PrinterSettings.IsValid)
                {
                    return trays;
                }

                // Get all paper sources
                var paperSources = printDoc.PrinterSettings.PaperSources;

                for (int i = 0; i < paperSources.Count; i++)
                {
                    var source = paperSources[i];
                    var tray = new PrinterTray
                    {
                        Name = GetTrayDisplayName(source.Kind),
                        Kind = source.Kind,
                        IsAvailable = IsTrayAvailable(source.Kind),
                        Index = i
                    };
                    trays.Add(tray);
                }

                // If no trays detected, add default
                if (trays.Count == 0)
                {
                    trays.Add(new PrinterTray
                    {
                        Name = "Default Tray",
                        Kind = PaperSourceKind.AutomaticFeed,
                        IsAvailable = true,
                        Index = 0
                    });
                }
            }
            catch (Exception)
            {
                // If detection fails, return default tray
                trays.Add(new PrinterTray
                {
                    Name = "Default Tray",
                    Kind = PaperSourceKind.AutomaticFeed,
                    IsAvailable = true,
                    Index = 0
                });
            }

            return trays;
        }

        private string GetTrayDisplayName(PaperSourceKind kind)
        {
            return kind switch
            {
                PaperSourceKind.Upper => "Tray 1 (Upper)",
                PaperSourceKind.Lower => "Tray 2 (Lower)",
                PaperSourceKind.Middle => "Tray 3 (Middle)",
                PaperSourceKind.Manual => "Manual Feed",
                PaperSourceKind.Envelope => "Envelope",
                PaperSourceKind.Cassette => "Cassette",
                PaperSourceKind.AutomaticFeed => "Auto",
                PaperSourceKind.SmallFormat => "Small Format",
                PaperSourceKind.LargeFormat => "Large Format",
                PaperSourceKind.LargeCapacity => "Large Capacity",
                PaperSourceKind.FormSource => "Form Source",
                _ => $"Tray ({kind})"
            };
        }

        private bool IsTrayAvailable(PaperSourceKind kind)
        {
            // Most trays are considered available unless explicitly marked as unavailable
            // In a real implementation, you might query the printer status
            return kind != PaperSourceKind.Custom;
        }
    }
}
