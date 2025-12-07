using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IPrinterDiscoveryService
    {
        Task<IEnumerable<PrinterInfo>> ScanAsync();
        IEnumerable<PrinterInfo> GetPrinters();
        event EventHandler<IEnumerable<PrinterInfo>> PrintersUpdated;
        void StartMonitoring();
        void StopMonitoring();
    }
}
