using Apex.Core.Interfaces;
using Apex.Core.Models;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class PrinterMonitoringService : IHostedService, IDisposable
    {
        private readonly ILoggerService _logger;
        private readonly IPrinterService _printerService;
        private Timer? _timer;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PrinterStatusEventArgs> _currentStatuses = new();

        // Event for UI updates
        public event EventHandler<PrinterStatusEventArgs>? PrinterStatusChanged;

        public PrinterMonitoringService(ILoggerService logger, IPrinterService printerService)
        {
            _logger = logger;
            _printerService = printerService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Log(LogLevel.Info, "Printer Monitoring Service starting.", "PrinterMonitoringService", "StartAsync");
            // Start the timer with infinite period (one-shot), first run immediately
            _timer = new Timer(DoWork, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            return Task.CompletedTask;
        }

        private async void DoWork(object? state)
        {
            try
            {
                // Run update on background thread
                await Task.Run(() => UpdatePrinterStatuses());
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "Error in monitoring loop", "PrinterMonitoringService", "DoWork", ex);
            }
            finally
            {
                // Schedule next run only after completion (prevent overlap)
                try
                {
                    _timer?.Change(_interval, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException) { /* Service stopped */ }
            }
        }

        private void UpdatePrinterStatuses()
        {
            // Windows only
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            try
            {
                // WMI can be slow, ensure we dispose resources
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
                using var printers = searcher.Get();

                // Get Job Counts
                var jobCounts = new Dictionary<string, int>();
                try
                {
                    using var jobSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PrintJob");
                    using var jobs = jobSearcher.Get();
                    foreach (ManagementObject job in jobs)
                    {
                        // Name format is usually "PrinterName, JobId"
                        string jobName = job["Name"]?.ToString() ?? "";
                        var parts = jobName.Split(',');
                        if (parts.Length > 0)
                        {
                            string printerName = parts[0].Trim();
                            if (!jobCounts.ContainsKey(printerName)) jobCounts[printerName] = 0;
                            jobCounts[printerName]++;
                        }
                    }
                }
                catch { /* Ignore job query errors */ }

                foreach (ManagementObject printer in printers)
                {
                    string name = printer["Name"]?.ToString() ?? string.Empty;
                    string status = "Online";
                    bool isOffline = false;
                    bool hasError = false;

                    // Check WorkOffline
                    if (bool.TryParse(printer["WorkOffline"]?.ToString(), out bool offline) && offline)
                    {
                        status = "Offline";
                        isOffline = true;
                    }

                    // Check DetectedErrorState
                    if (int.TryParse(printer["DetectedErrorState"]?.ToString(), out int errorState))
                    {
                        if (errorState == 4) { status = "Out of Paper"; hasError = true; }
                        else if (errorState == 5) { status = "Low Toner"; }
                        else if (errorState > 2 && errorState != 65535) { status = "Error"; hasError = true; }
                    }

                    int queueLength = jobCounts.ContainsKey(name) ? jobCounts[name] : 0;

                    // Fire event
                    var args = new PrinterStatusEventArgs(name, status, isOffline, hasError, queueLength);
                    PrinterStatusChanged?.Invoke(this, args);
                    
                    // Update cache
                    _currentStatuses[name] = args;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, "Failed to query Win32_Printer", "PrinterMonitoringService", "UpdatePrinterStatuses", ex);
            }
        }

        public PrinterStatusEventArgs? GetCurrentStatus(string printerName)
        {
            if (_currentStatuses.TryGetValue(printerName, out var status))
            {
                return status;
            }
            return null;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Log(LogLevel.Info, "Printer Monitoring Service stopping.", "PrinterMonitoringService", "StopAsync");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }

    public class PrinterStatusEventArgs : EventArgs
    {
        public string PrinterName { get; }
        public string Status { get; }
        public bool IsOffline { get; }
        public bool HasError { get; }
        public int QueueLength { get; }

        public PrinterStatusEventArgs(string printerName, string status, bool isOffline, bool hasError, int queueLength)
        {
            PrinterName = printerName;
            Status = status;
            IsOffline = isOffline;
            HasError = hasError;
            QueueLength = queueLength;
        }
    }
}
