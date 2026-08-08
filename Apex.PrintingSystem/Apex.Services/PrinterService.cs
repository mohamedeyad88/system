using Apex.Core.Enums;
using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class PrinterService : IPrinterService
    {
        private readonly IRepository<Printer> _printerRepository;

        public PrinterService(IRepository<Printer> printerRepository)
        {
            _printerRepository = printerRepository;
        }

        public async Task<IEnumerable<Printer>> GetAllPrintersAsync()
        {
            return await _printerRepository.GetAllAsync();
        }

        public async Task AddPrinterAsync(Printer printer)
        {
            await _printerRepository.AddAsync(printer);
        }

        public async Task CheckPrintersStatusAsync()
        {
            // Status checking is now handled by PrinterMonitoringService via WMI
            await Task.CompletedTask;
        }

        public async Task<bool> PrintFileAsync(string printerName, string filePath, int copies)
        {
            // Create a basic job without custom settings
            var job = new PrintJob { TotalCopies = copies };
            return await PrintFileAsync(printerName, filePath, job);
        }

        public async Task<bool> PrintFileAsync(string printerName, string filePath, PrintJob jobWithSettings)
        {
            // ═══════════════════════════════════════════════════════════════════
            // VENDOR-AWARE PRINT GATEWAY (MANDATORY)
            // All print operations MUST pass through the vendor gateway for:
            // - Automatic HP/Epson/Generic optimization
            // - Silent vendor detection
            // - Intelligent error recovery
            // ═══════════════════════════════════════════════════════════════════

            var gateway = Printing.VendorDetection.VendorAwarePrintGateway.Instance;

            var result = await gateway.PrintAsync(
                printerName,
                filePath,
                jobWithSettings.TotalCopies,
                jobWithSettings);

            return result.Success;
        }

        private bool PrintImageWithSettings(string printerName, string filePath, PrintJob settings)
        {
            try
            {
                using var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                using var ms = new System.IO.MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                using var image = System.Drawing.Image.FromStream(ms);

                using var pd = new System.Drawing.Printing.PrintDocument();
                pd.PrinterSettings.PrinterName = printerName;
                pd.PrinterSettings.Copies = (short)settings.TotalCopies;

                // Apply custom settings to a cloned page settings instance (job scope only)
                var jobPageSettings = (System.Drawing.Printing.PageSettings)pd.DefaultPageSettings.Clone();

                if (settings.Duplex && pd.PrinterSettings.CanDuplex)
                {
                    pd.PrinterSettings.Duplex = System.Drawing.Printing.Duplex.Vertical;
                }

                jobPageSettings.Color = settings.Color;
                jobPageSettings.Landscape = settings.Orientation?.Equals("Landscape", StringComparison.OrdinalIgnoreCase) ?? false;

                pd.QueryPageSettings += (s, e) =>
                {
                    e.PageSettings = (System.Drawing.Printing.PageSettings)jobPageSettings.Clone();
                };

                pd.PrintPage += (s, e) =>
                {
                    if (e.Graphics != null)
                    {
                        e.Graphics.DrawImage(image, e.MarginBounds);
                    }
                };

                pd.Print();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public PrinterType GetPrinterType(string printerName)
        {
            // Simple logic based on name or default
            if (printerName.ToLower().Contains("laser")) return PrinterType.Laser;
            if (printerName.ToLower().Contains("ink")) return PrinterType.Inkjet;
            return PrinterType.Other;
        }

        public PrinterCapabilities GetCapabilities(Printer printer)
        {
            if (string.IsNullOrEmpty(printer.CapabilitiesJson))
            {
                return new PrinterCapabilities();
            }

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<PrinterCapabilities>(printer.CapabilitiesJson)
                       ?? new PrinterCapabilities();
            }
            catch
            {
                return new PrinterCapabilities();
            }
        }

        public async Task UpdateCapabilitiesAsync(int printerId, PrinterCapabilities capabilities)
        {
            var printer = await _printerRepository.GetByIdAsync(printerId);
            if (printer != null)
            {
                printer.CapabilitiesJson = System.Text.Json.JsonSerializer.Serialize(capabilities);
                await _printerRepository.UpdateAsync(printer);
            }
        }
    }
}
