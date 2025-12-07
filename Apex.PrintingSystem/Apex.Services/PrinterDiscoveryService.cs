using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows.Threading;

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
            _timer.Tick += async (s, e) => await BroadcastPrinterStatus();
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
                                    // New Capabilities
                                    IsDefault = IsDefaultPrinter(queue.Name),
                                    CanDuplex = queue.GetPrintCapabilities().DuplexingCapability.Contains(Duplexing.TwoSidedLongEdge),
                                    IsColor = queue.GetPrintCapabilities().OutputColorCapability.Contains(OutputColor.Color),
                                    SupportedPapers = queue.GetPrintCapabilities().PageMediaSizeCapability.Select(p => p.PageMediaSizeName.ToString() ?? "Unknown").ToList()
                                };
                                list.Add(info);
                            }
                            catch { /* Ignore individual queue errors */ }
                        }
                    }
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
            return "Ready";
        }
    }
}
