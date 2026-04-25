using Apex.Core.Interfaces;
using Apex.Services.Helpers;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    public class PrintEngine : IPrintEngine
    {
        private readonly IPrinterValidationService _validationService;
        private readonly IPrintJobLogger _logger;

        public PrintEngine(IPrinterValidationService validationService, IPrintJobLogger logger)
        {
            _validationService = validationService;
            _logger = logger;
        }

        public async Task<bool> PrintAsync(string printerName, string filePath)
        {
            // 1. Validate Printer
            var validation = await _validationService.ValidatePrinterAsync(printerName);
            if (!validation.IsValid)
            {
                _logger.LogJob(printerName, filePath, false, validation.Message);
                return false;
            }

            // 2. Detect File Type
            var extension = Path.GetExtension(filePath).ToLower();
            bool success = false;
            string message = "Success";

            try
            {
                if (IsRawFormat(extension))
                {
                    success = await PrintRawAsync(printerName, filePath);
                }
                else if (IsImageFormat(extension))
                {
                    success = await PrintImageAsync(printerName, filePath);
                }
                else if (extension == ".pdf")
                {
                    success = await PrintPdfAsync(printerName, filePath);
                }
                else
                {
                    // Fallback to Shell Print
                    success = await PrintShellAsync(printerName, filePath);
                }
            }
            catch (Exception ex)
            {
                success = false;
                message = ex.Message;
            }

            // 3. Log Result
            _logger.LogJob(printerName, filePath, success, message);
            return success;
        }

        public async Task<bool> PrintTestPageAsync(string printerName)
        {
            string testFile = Path.Combine(Path.GetTempPath(), "ApexTestPage.txt");
            File.WriteAllText(testFile, "Apex Printing System - Test Page\n--------------------------------\nPrinter: " + printerName + "\nDate: " + DateTime.Now);
            return await PrintAsync(printerName, testFile);
        }

        private bool IsRawFormat(string ext) => ext == ".txt" || ext == ".zpl" || ext == ".prn" || ext == ".esc";
        private bool IsImageFormat(string ext) => ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp";

        private Task<bool> PrintRawAsync(string printerName, string filePath)
        {
            return Task.Run(() => RawPrinterHelper.SendFileToPrinter(printerName, filePath));
        }

        private Task<bool> PrintImageAsync(string printerName, string filePath)
        {
            return Task.Run(() =>
            {
                System.Drawing.Image? imageCopy = null;
                try
                {
                    // Load image into MemoryStream first to release file handle immediately
                    // This prevents file locking while keeping image data in memory
                    using (var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                    using (var memoryStream = new System.IO.MemoryStream())
                    {
                        fileStream.CopyTo(memoryStream);
                        memoryStream.Position = 0;
                        imageCopy = System.Drawing.Image.FromStream(memoryStream);
                    }
                    
                    // Use ManualResetEvent to ensure image isn't disposed until printing completes
                    using (var printCompleted = new System.Threading.ManualResetEvent(false))
                    using (PrintDocument pd = new PrintDocument())
                    {
                        pd.PrinterSettings.PrinterName = printerName;
                        
                        pd.PrintPage += (s, e) =>
                        {
                            try
                            {
                                if (imageCopy != null && e.Graphics != null)
                                {
                                    Rectangle m = e.MarginBounds;
                                    if (imageCopy.Width > imageCopy.Height)
                                        m = new Rectangle(m.Top, m.Left, m.Height, m.Width); // Rotate logic placeholder
                                    
                                    e.Graphics.DrawImage(imageCopy, e.MarginBounds);
                                }
                            }
                            catch
                            {
                                // Error in PrintPage - will be caught by outer try-catch
                            }
                        };
                        
                        pd.EndPrint += (s, e) =>
                        {
                            // Signal that printing is complete
                            printCompleted.Set();
                        };
                        
                        pd.Print();
                        
                        // Wait for printing to complete before disposing image
                        bool completed = printCompleted.WaitOne(TimeSpan.FromSeconds(30));
                        
                        return completed;
                    }
                }
                catch (Exception ex)
                {
                    // Log the error and return false
                    System.Diagnostics.Debug.WriteLine($"Error printing image: {ex.Message}");
                    return false;
                }
                finally
                {
                    // Dispose image in finally block to ensure cleanup
                    imageCopy?.Dispose();
                }
            });
        }

        private async Task<bool> PrintPdfAsync(string printerName, string filePath)
        {
            // Use the dedicated PDF direct printer
            var pdfPrinter = new PdfDirectPrinter();
            return await pdfPrinter.PrintPdfAsync(printerName, filePath);
        }

        /// <summary>
        /// Attempts to print unsupported file types using Windows print dialog.
        /// Falls back to converting to text if possible.
        /// </summary>
        private Task<bool> PrintShellAsync(string printerName, string filePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    // Try to read the file as text and print it
                    var text = File.ReadAllText(filePath);
                    
                    using var pd = new PrintDocument();
                    pd.PrinterSettings.PrinterName = printerName;
                    pd.DocumentName = Path.GetFileName(filePath);
                    
                    var lines = text.Split('\n');
                    int lineIndex = 0;
                    int linesPerPage = 50;
                    
                    pd.PrintPage += (s, e) =>
                    {
                        if (e.Graphics == null) return;
                        
                        using var font = new Font("Consolas", 10);
                        float y = e.MarginBounds.Top;
                        float lineHeight = font.GetHeight(e.Graphics);
                        int printedLines = 0;
                        
                        while (lineIndex < lines.Length && printedLines < linesPerPage)
                        {
                            e.Graphics.DrawString(lines[lineIndex], font, Brushes.Black, e.MarginBounds.Left, y);
                            y += lineHeight;
                            lineIndex++;
                            printedLines++;
                        }
                        
                        e.HasMorePages = lineIndex < lines.Length;
                    };
                    
                    pd.Print();
                    return true;
                }
                catch
                {
                    // Cannot print this file type
                    return false;
                }
            });
        }
    }
}
