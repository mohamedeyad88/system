using Apex.Core.Interfaces;
using System;
using System.Printing;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    public class PrinterValidationService : IPrinterValidationService
    {
        public async Task<(bool IsValid, string Message)> ValidatePrinterAsync(string printerName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var server = new LocalPrintServer())
                    {
                        var queue = server.GetPrintQueue(printerName);

                        // Optional: Check if we can access properties (permission check)
                        var status = queue.QueueStatus; 

                        return (true, "Printer is ready.");
                    }
                }
                catch (Exception ex)
                {
                    return (false, $"Validation failed: {ex.Message}");
                }
            });
        }
    }
}
