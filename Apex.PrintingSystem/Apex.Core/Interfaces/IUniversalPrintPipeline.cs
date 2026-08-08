using Apex.Core.Models;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IUniversalPrintPipeline
    {
        // Main entry point for any file
        Task<PrintJob> ProcessAndQueueJobAsync(string filePath, string printerName, int copies = 1);

        // Main entry point with custom settings
        Task<PrintJob> ProcessAndQueueJobAsync(string filePath, string printerName, PrintJobSettings settings);

        // Validation check
        bool IsFileSupported(string filePath);
    }

    /// <summary>
    /// Settings for a print job
    /// </summary>
    public class PrintJobSettings
    {
        public int Copies { get; set; } = 1;
        public bool Duplex { get; set; } = false;
        public bool Color { get; set; } = true;
        public string PageRange { get; set; } = "All";
        public string PaperSize { get; set; } = "A4";
        public string Orientation { get; set; } = "Portrait";
        public string Quality { get; set; } = "Normal";
    }
}
