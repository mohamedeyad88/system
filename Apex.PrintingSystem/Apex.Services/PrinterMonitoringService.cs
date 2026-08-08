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
                // EnumerationOptions with a timeout prevents WMI from hanging and
                // reduces the chance of native AccessViolationException on slow systems.
                var wmiOptions = new EnumerationOptions
                {
                    Timeout = TimeSpan.FromSeconds(8),
                    ReturnImmediately = false
                };

                // Get Job Counts first (separate searcher — dispose before printer loop)
                var jobCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var jobSearcher = new ManagementObjectSearcher(
                        "root\\cimv2", "SELECT Name FROM Win32_PrintJob", wmiOptions);
                    using var jobs = jobSearcher.Get();
                    foreach (ManagementBaseObject baseObj in jobs)
                    {
                        using var job = (ManagementObject)baseObj;
                        string jobName = job["Name"]?.ToString() ?? "";
                        var parts = jobName.Split(',');
                        if (parts.Length > 0)
                        {
                            string printerName = parts[0].Trim();
                            jobCounts.TryAdd(printerName, 0);
                            jobCounts[printerName]++;
                        }
                    }
                }
                catch (ManagementException) { /* Ignore job query errors */ }
                catch (COMException) { /* WMI COM error — skip job counts */ }

                using var searcher = new ManagementObjectSearcher(
                    "root\\cimv2", "SELECT * FROM Win32_Printer", wmiOptions);
                using var printers = searcher.Get();

                foreach (ManagementBaseObject baseObj in printers)
                {
                    // Each ManagementObject wraps a COM RCW — must be disposed individually.
                    using var printer = (ManagementObject)baseObj;

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

                    int queueLength = jobCounts.TryGetValue(name, out int cnt) ? cnt : 0;

                    var args = new PrinterStatusEventArgs(name, status, isOffline, hasError, queueLength);
                    PrinterStatusChanged?.Invoke(this, args);
                    _currentStatuses[name] = args;
                }
            }
            catch (ManagementException ex)
            {
                _logger.Log(LogLevel.Warning, $"WMI ManagementException: {ex.ErrorCode}", "PrinterMonitoringService", "UpdatePrinterStatuses", ex);
            }
            catch (COMException ex)
            {
                _logger.Log(LogLevel.Warning, $"WMI COMException: 0x{ex.HResult:X8}", "PrinterMonitoringService", "UpdatePrinterStatuses", ex);
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
