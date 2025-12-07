using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// Engine for printing one file to multiple printers in parallel.
    /// </summary>
    public class ParallelPrintEngine
    {
        private readonly IPrintEngine _printEngine;
        private readonly IPrinterDiscoveryService _printerDiscovery;

        public ParallelPrintEngine(IPrintEngine printEngine, IPrinterDiscoveryService printerDiscovery)
        {
            _printEngine = printEngine;
            _printerDiscovery = printerDiscovery;
        }

        /// <summary>
        /// Prints a file to multiple printers in parallel.
        /// </summary>
        public async Task<List<PrintResult>> PrintToMultiplePrintersAsync(
            string filePath,
            List<string> printerNames,
            int copies = 1,
            IProgress<PrinterProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<PrintResult>();
            var tasks = new List<Task<PrintResult>>();

            // Create a task for each printer
            foreach (var printerName in printerNames)
            {
                var task = PrintToPrinterAsync(filePath, printerName, copies, progress, cancellationToken);
                tasks.Add(task);
            }

            // Wait for all tasks to complete
            var completedResults = await Task.WhenAll(tasks);
            results.AddRange(completedResults);

            return results;
        }

        /// <summary>
        /// Prints to a single printer and returns the result.
        /// </summary>
        private async Task<PrintResult> PrintToPrinterAsync(
            string filePath,
            string printerName,
            int copies,
            IProgress<PrinterProgress>? progress,
            CancellationToken cancellationToken)
        {
            var result = new PrintResult
            {
                PrinterName = printerName,
                StartTime = DateTime.Now
            };

            try
            {
                // Report start
                progress?.Report(new PrinterProgress
                {
                    PrinterName = printerName,
                    Status = "Starting...",
                    Progress = 0
                });

                // Check if cancelled
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Success = false;
                    result.ErrorMessage = "Cancelled by user";
                    result.EndTime = DateTime.Now;
                    return result;
                }

                // Report printing
                progress?.Report(new PrinterProgress
                {
                    PrinterName = printerName,
                    Status = "Printing...",
                    Progress = 50
                });

                // Execute print
                bool success = await _printEngine.PrintAsync(printerName, filePath);

                result.Success = success;
                result.EndTime = DateTime.Now;

                if (success)
                {
                    result.JobId = Guid.NewGuid().ToString().Substring(0, 8);
                    progress?.Report(new PrinterProgress
                    {
                        PrinterName = printerName,
                        Status = "Completed",
                        Progress = 100
                    });
                }
                else
                {
                    result.ErrorMessage = "Print failed";
                    progress?.Report(new PrinterProgress
                    {
                        PrinterName = printerName,
                        Status = "Failed",
                        Progress = 0
                    });
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.Now;

                progress?.Report(new PrinterProgress
                {
                    PrinterName = printerName,
                    Status = $"Error: {ex.Message}",
                    Progress = 0
                });
            }

            return result;
        }

        /// <summary>
        /// Gets capabilities of a specific printer.
        /// </summary>
        public async Task<PrinterMonitorInfo> GetPrinterInfoAsync(string printerName)
        {
            var info = new PrinterMonitorInfo
            {
                Name = printerName
            };

            try
            {
                var printers = await _printerDiscovery.ScanAsync();
                var printer = printers.FirstOrDefault(p => p.Name == printerName);

                if (printer != null)
                {
                    info.Status = printer.IsOnline ? "Online" : "Offline";
                    info.SupportsColor = printer.Capabilities?.CanPrintColor ?? false;
                    info.SupportsDuplex = printer.Capabilities?.CanDuplex ?? false;
                    // Driver and Location not available in PrinterInfo model
                }
                else
                {
                    info.Status = "Not Found";
                }
            }
            catch
            {
                info.Status = "Error";
            }

            return info;
        }
    }

    /// <summary>
    /// Progress information for a single printer during distribution printing.
    /// </summary>
    public class PrinterProgress
    {
        public string PrinterName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Progress { get; set; } // 0-100
    }
}
