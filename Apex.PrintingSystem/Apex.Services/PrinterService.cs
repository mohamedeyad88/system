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
            return await Task.Run(() =>
            {
                try
                {
                    // Uses RawPrinterHelper to send file directly to the spooler
                    // Future: Add rendering logic for non-raw formats (PDF, Images) if needed
                    
                    // Simple loop for copies (RawPrinterHelper handles one copy at a time)
                    for (int i = 0; i < copies; i++)
                    {
                        if (!Helpers.RawPrinterHelper.SendFileToPrinter(printerName, filePath))
                        {
                            return false; 
                        }
                    }
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
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
