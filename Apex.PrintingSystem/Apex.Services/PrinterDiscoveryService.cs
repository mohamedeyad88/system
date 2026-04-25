using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Management;

namespace Apex.Services
{
    public class PrinterDiscoveryService : IPrinterDiscoveryService
    {
        private readonly DispatcherTimer _timer;
        public event EventHandler<IEnumerable<PrinterInfo>>? PrintersUpdated;

        public PrinterDiscoveryService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _timer.Tick += (s, e) => { _ = BroadcastPrinterStatus(); };
        }

        public void StartMonitoring()
        {
            _timer.Start();
            _ = BroadcastPrinterStatus(); // Initial broadcast
        }

        public void StopMonitoring()
        {
            _timer.Stop();
        }

        private async Task BroadcastPrinterStatus()
        {
            var printers = await ScanAsync();
            PrintersUpdated?.Invoke(this, printers);
        }

        private List<PrinterInfo> _cachedPrinters = new();

        public IEnumerable<PrinterInfo> GetPrinters()
        {
            // Return cached copy to avoid blocking UI thread
            lock (_cachedPrinters)
            {
                return new List<PrinterInfo>(_cachedPrinters);
            }
        }

        public async Task<IEnumerable<PrinterInfo>> ScanAsync()
        {
            var printers = await Task.Run(() =>
            {
                var list = new List<PrinterInfo>();

                try
                {
                    // Method 1: Use System.Printing API
                    try
                    {
                        using (var server = new LocalPrintServer())
                        {
                            var queueTypes = new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections };
                            var queues = server.GetPrintQueues(queueTypes);

                            foreach (var queue in queues)
                            {
                                try
                                {
                                    queue.Refresh();
                                    var info = new PrinterInfo
                                    {
                                        Name = queue.Name,
                                        Type = queue.FullName.Contains("\\") ? "Network" : "Local",
                                        QueueLength = queue.NumberOfJobs,
                                        IsOnline = !queue.IsOffline,
                                        StatusText = GetStatusString(queue),
                                        HasPaperJam = queue.IsPaperJammed,
                                        IsOutOfPaper = queue.IsOutOfPaper,
                                        IsTonerLow = queue.IsTonerLow,
                                        IsDefault = IsDefaultPrinter(queue.Name)
                                    };

                                    // Try to get capabilities safely
                                    try
                                    {
                                        var caps = queue.GetPrintCapabilities();
                                        info.CanDuplex = caps.DuplexingCapability?.Contains(Duplexing.TwoSidedLongEdge) ?? false;
                                        info.IsColor = caps.OutputColorCapability?.Contains(OutputColor.Color) ?? false;
                                        info.SupportedPapers = caps.PageMediaSizeCapability?
                                            .Select(p => p.PageMediaSizeName?.ToString() ?? "Unknown")
                                            .ToList() ?? new List<string>();
                                    }
                                    catch { /* Capabilities not available */ }

                                    list.Add(info);
                                }
                                catch { /* Ignore individual queue errors */ }
                            }
                        }
                    }
                    catch { /* LocalPrintServer failed */ }

                    // Method 2: Use WMI for additional/offline printers
                    try
                    {
                        var wmiPrinters = GetPrintersFromWMI();
                        foreach (var wmiPrinter in wmiPrinters)
                        {
                            // Add only if not already in list
                            if (!list.Any(p => p.Name.Equals(wmiPrinter.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                list.Add(wmiPrinter);
                            }
                            else
                            {
                                // Update existing with WMI status if available
                                var existing = list.First(p => p.Name.Equals(wmiPrinter.Name, StringComparison.OrdinalIgnoreCase));
                                if (existing.StatusText == "Ready" && wmiPrinter.StatusText != "Ready")
                                {
                                    existing.StatusText = wmiPrinter.StatusText;
                                    existing.IsOnline = wmiPrinter.IsOnline;
                                }
                            }
                        }
                    }
                    catch { /* WMI failed */ }

                    // Method 3: Use PrinterSettings for any missed printers
                    try
                    {
                        foreach (string printerName in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                        {
                            if (!list.Any(p => p.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase)))
                            {
                                var settings = new System.Drawing.Printing.PrinterSettings { PrinterName = printerName };
                                list.Add(new PrinterInfo
                                {
                                    Name = printerName,
                                    Type = printerName.Contains("\\") ? "Network" : "Local",
                                    IsOnline = settings.IsValid,
                                    StatusText = settings.IsValid ? "Ready" : "Offline",
                                    IsDefault = settings.IsDefaultPrinter
                                });
                            }
                        }
                    }
                    catch { /* PrinterSettings failed */ }
                }
                catch (Exception)
                {
                    // Log error
                }

                return list;
            });

            lock (_cachedPrinters)
            {
                _cachedPrinters = new List<PrinterInfo>(printers);
            }

            return printers;
        }

        /// <summary>
        /// Get printers using WMI - can detect offline printers
        /// </summary>
        private List<PrinterInfo> GetPrintersFromWMI()
        {
            var list = new List<PrinterInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
                foreach (ManagementObject printer in searcher.Get())
                {
                    try
                    {
                        var name = printer["Name"]?.ToString() ?? "";
                        var status = Convert.ToUInt16(printer["PrinterStatus"] ?? 0);
                        var workOffline = Convert.ToBoolean(printer["WorkOffline"] ?? false);
                        var isNetwork = Convert.ToBoolean(printer["Network"] ?? false);
                        var isLocal = Convert.ToBoolean(printer["Local"] ?? true);
                        var isDefault = Convert.ToBoolean(printer["Default"] ?? false);
                        var portName = printer["PortName"]?.ToString() ?? "";

                        // Determine if printer is truly online
                        bool isOnline = !workOffline && status != 7; // 7 = Offline in WMI

                        // Detect USB printers that are disconnected
                        if (portName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                        {
                            // USB printers - check if port is accessible
                            isOnline = isOnline && !workOffline;
                        }

                        list.Add(new PrinterInfo
                        {
                            Name = name,
                            Type = isNetwork ? "Network" : (portName.StartsWith("USB") ? "USB" : "Local"),
                            IsOnline = isOnline,
                            StatusText = GetWMIStatusString(status, workOffline),
                            IsDefault = isDefault,
                            QueueLength = Convert.ToInt32(printer["JobCountSinceLastReset"] ?? 0)
                        });
                    }
                    catch { /* Ignore individual printer errors */ }
                }
            }
            catch { /* WMI query failed */ }

            return list;
        }

        private string GetWMIStatusString(ushort status, bool workOffline)
        {
            if (workOffline) return "Offline";

            return status switch
            {
                1 => "Other",
                2 => "Unknown",
                3 => "Ready",
                4 => "Printing",
                5 => "Warming Up",
                6 => "Stopped",
                7 => "Offline",
                8 => "Paused",
                9 => "Error",
                10 => "Busy",
                11 => "Not Available",
                12 => "Waiting",
                13 => "Processing",
                14 => "Initialization",
                15 => "Power Save",
                16 => "Pending Deletion",
                17 => "I/O Active",
                18 => "Manual Feed",
                _ => "Ready"
            };
        }

        private bool IsDefaultPrinter(string printerName)
        {
            try
            {
                var settings = new System.Drawing.Printing.PrinterSettings();
                return settings.PrinterName.Equals(printerName, StringComparison.OrdinalIgnoreCase) && settings.IsDefaultPrinter;
            }
            catch { return false; }
        }

        private string GetStatusString(PrintQueue queue)
        {
            if (queue.IsOffline) return "Offline";
            if (queue.IsPaperJammed) return "Paper Jam";
            if (queue.IsOutOfPaper) return "Out of Paper";
            if (queue.IsTonerLow) return "Toner Low";
            if (queue.IsPrinting) return "Printing";
            if (queue.IsBusy) return "Busy";
            if (queue.IsPaused) return "Paused";
            if (queue.IsWaiting) return "Waiting";
            if (queue.HasPaperProblem) return "Paper Problem";
            return "Ready";
        }
    }
}
