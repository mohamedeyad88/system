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
    /// <summary>
    /// Answers "can this printer print right now?" — the question the print path
    /// has to ask before it feeds a station, and the seam that lets a test say
    /// "pretend printer B has its door open".
    /// </summary>
    public interface IPrinterHealthProbe
    {
        PrinterStatusEventArgs? GetCurrentStatus(string printerName);
    }

    public class PrinterMonitoringService : IHostedService, IDisposable, IPrinterHealthProbe
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
                    var condition = PrinterCondition.Ready;
                    bool isOffline = false;

                    // Check WorkOffline
                    if (bool.TryParse(printer["WorkOffline"]?.ToString(), out bool offline) && offline)
                    {
                        condition = PrinterCondition.Offline;
                        isOffline = true;
                    }

                    // DetectedErrorState carries the CIM_Printer enumeration. It used
                    // to be read as "4 is out of paper, 5 is low toner, anything else
                    // above 2 is an error" — which swept Low Paper (3) in with the
                    // faults. Harmless while nothing read the status; not harmless now
                    // that a fault holds the job, because a tray with sheets still in
                    // it would stop the station.
                    if (int.TryParse(printer["DetectedErrorState"]?.ToString(), out int errorState)
                        && errorState != 65535)
                    {
                        var detected = errorState switch
                        {
                            3  => PrinterCondition.LowPaper,
                            4  => PrinterCondition.OutOfPaper,
                            5  => PrinterCondition.LowToner,
                            6  => PrinterCondition.OutOfToner,
                            7  => PrinterCondition.DoorOpen,
                            8  => PrinterCondition.PaperJam,
                            9  => PrinterCondition.NeedsAttention,   // service requested
                            10 => PrinterCondition.OutputBinFull,
                            11 => PrinterCondition.PaperProblem,
                            12 => PrinterCondition.NeedsAttention,   // cannot print page
                            13 => PrinterCondition.NeedsAttention,   // user intervention
                            14 => PrinterCondition.NeedsAttention,   // out of memory
                            _  => PrinterCondition.Ready             // 0 unknown, 1 other, 2 no error
                        };

                        // A device fault is more actionable than the offline flag,
                        // which is often just a stale checkbox.
                        if (detected != PrinterCondition.Ready)
                            condition = detected;
                    }

                    string status = condition switch
                    {
                        PrinterCondition.Ready         => "Online",
                        PrinterCondition.LowPaper      => "Low Paper",
                        PrinterCondition.LowToner      => "Low Toner",
                        PrinterCondition.OutOfPaper    => "Out of Paper",
                        PrinterCondition.OutOfToner    => "Out of Toner",
                        PrinterCondition.DoorOpen      => "Door Open",
                        PrinterCondition.PaperJam      => "Paper Jam",
                        PrinterCondition.OutputBinFull => "Output Bin Full",
                        PrinterCondition.PaperProblem  => "Paper Problem",
                        PrinterCondition.Offline       => "Offline",
                        _                              => "Needs Attention"
                    };

                    int queueLength = jobCounts.TryGetValue(name, out int cnt) ? cnt : 0;

                    var args = new PrinterStatusEventArgs(
                        name, condition, status, isOffline,
                        hasError: condition is not (PrinterCondition.Ready
                                                    or PrinterCondition.LowPaper
                                                    or PrinterCondition.LowToner),
                        queueLength);
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

    /// <summary>
    /// What a printer is actually doing, from the WMI DetectedErrorState value.
    ///
    /// The split that matters is between conditions a person has to walk over and
    /// fix, and conditions that are merely worth saying out loud. A tray with fifty
    /// sheets left still prints; a tray with none does not.
    /// </summary>
    public enum PrinterCondition
    {
        Ready = 0,

        // ── warnings: the printer still prints ──
        LowPaper,
        LowToner,

        // ── faults: the printer needs a person ──
        OutOfPaper,
        OutOfToner,
        DoorOpen,
        PaperJam,
        OutputBinFull,
        PaperProblem,
        NeedsAttention,
        Offline
    }

    public class PrinterStatusEventArgs : EventArgs
    {
        public string PrinterName { get; }
        public PrinterCondition Condition { get; }
        public string Status { get; }
        public bool IsOffline { get; }
        public bool HasError { get; }
        public int QueueLength { get; }

        /// <summary>
        /// True when the printer cannot print until somebody attends to it.
        ///
        /// This is the flag the print path holds on. It is deliberately false for
        /// LowPaper and LowToner: treating those as faults would stop a station
        /// that is still perfectly able to finish the run.
        /// </summary>
        public bool RequiresIntervention => Condition is
            PrinterCondition.OutOfPaper or
            PrinterCondition.OutOfToner or
            PrinterCondition.DoorOpen or
            PrinterCondition.PaperJam or
            PrinterCondition.OutputBinFull or
            PrinterCondition.PaperProblem or
            PrinterCondition.NeedsAttention or
            PrinterCondition.Offline;

        public PrinterStatusEventArgs(
            string printerName, PrinterCondition condition, string status,
            bool isOffline, bool hasError, int queueLength)
        {
            PrinterName = printerName;
            Condition = condition;
            Status = status;
            IsOffline = isOffline;
            HasError = hasError;
            QueueLength = queueLength;
        }
    }
}
