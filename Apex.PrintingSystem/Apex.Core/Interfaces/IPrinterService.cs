using Apex.Core.Enums;
using Apex.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IPrinterService
    {
        Task<IEnumerable<Printer>> GetAllPrintersAsync();
        Task AddPrinterAsync(Printer printer);
        Task CheckPrintersStatusAsync();

        // Restored methods
        Task<bool> PrintFileAsync(string printerName, string filePath, int copies);
        Task<bool> PrintFileAsync(string printerName, string filePath, PrintJob jobWithSettings);
        PrinterType GetPrinterType(string printerName);

        // Capabilities
        PrinterCapabilities GetCapabilities(Printer printer);
        Task UpdateCapabilitiesAsync(int printerId, PrinterCapabilities capabilities);
    }
}
