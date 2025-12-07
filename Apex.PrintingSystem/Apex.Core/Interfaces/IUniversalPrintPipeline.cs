using Apex.Core.Models;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IUniversalPrintPipeline
    {
        // Main entry point for any file
        Task<PrintJob> ProcessAndQueueJobAsync(string filePath, string printerName, int copies = 1);
        
        // Validation check
        bool IsFileSupported(string filePath);
    }
}
