using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    public class PrinterStatusService
    {
        private readonly IPrinterDiscoveryService _discoveryService;
        private readonly System.Threading.Timer _timer;
        private readonly Dictionary<string, string> _lastKnownStatus = new();

        public event EventHandler<string> OnPrinterStatusChanged;

        public PrinterStatusService(IPrinterDiscoveryService discoveryService)
        {
            _discoveryService = discoveryService;
            _timer = new System.Threading.Timer(CheckStatus, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void StartMonitoring(int intervalMs = 2000)
        {
            _timer.Change(0, intervalMs);
        }

        public void StopMonitoring()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private async void CheckStatus(object? state)
        {
            var printers = await _discoveryService.ScanAsync();
            foreach (var printer in printers)
            {
                if (_lastKnownStatus.TryGetValue(printer.Name, out var oldStatus))
                {
                    if (oldStatus != printer.StatusText)
                    {
                        _lastKnownStatus[printer.Name] = printer.StatusText;
                        OnPrinterStatusChanged?.Invoke(this, $"{printer.Name}: {printer.StatusText}");
                    }
                }
                else
                {
                    _lastKnownStatus[printer.Name] = printer.StatusText;
                }
            }
        }
    }
}
