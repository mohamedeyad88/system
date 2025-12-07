using Apex.Core.Models;
using System.Collections.Generic;

namespace Apex.Core.Interfaces
{
    public interface ILoadBalancer
    {
        PrinterInfo? SelectPrinterForJob(PrintJob job, IEnumerable<PrinterInfo> candidates);
    }
}
