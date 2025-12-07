using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// Provides direct PDF printing without requiring external applications.
    /// Uses Windows API for raw printing when supported, falls back to shell print.
    /// </summary>
    public class PdfDirectPrinter
    {
        /// <summary>
        /// Prints a PDF file directly to the specified printer.
        /// </summary>
        /// <param name="printerName">Target printer name.</param>
        /// <param name="pdfPath">Path to the PDF file.</param>
        /// <param name="copies">Number of copies to print.</param>
        /// <returns>True if printing was initiated successfully.</returns>
        public async Task<bool> PrintPdfAsync(string printerName, string pdfPath, int copies = 1)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF file not found.", pdfPath);

            // Try different methods in order of preference
            
            // 1. Try raw PDF printing (for PDF-native printers)
            if (await TryRawPdfPrintAsync(printerName, pdfPath))
                return true;

            // 2. Try using Windows Print API with PDF-XChange or similar installed renderer
            if (TryRegisteredPdfHandler(printerName, pdfPath, copies))
                return true;

            // 3. Fallback to silent shell print with Adobe/Edge
            return await TrySilentShellPrintAsync(printerName, pdfPath);
        }

        /// <summary>
        /// Attempts to send PDF directly to printer (works for PDF-native printers).
        /// </summary>
        private async Task<bool> TryRawPdfPrintAsync(string printerName, string pdfPath)
        {
            try
            {
                // Some printers support direct PDF - send raw bytes
                var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
                
                // Check if first bytes are PDF magic number
                if (pdfBytes.Length < 4) return false;
                if (pdfBytes[0] != '%' || pdfBytes[1] != 'P' || pdfBytes[2] != 'D' || pdfBytes[3] != 'F')
                    return false;

                // Try sending to printer as raw
                return SendRawDataToPrinter(printerName, pdfBytes, "RAW");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Uses Windows shell to find registered PDF handler and print.
        /// </summary>
        private bool TryRegisteredPdfHandler(string printerName, string pdfPath, int copies)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pdfPath,
                    Verb = "printto",
                    Arguments = $"\"{printerName}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;
                
                // Wait briefly for process to start, but don't block
                process.WaitForExit(5000);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Silent shell print using the default PDF application.
        /// </summary>
        private async Task<bool> TrySilentShellPrintAsync(string printerName, string pdfPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Use printto verb for silent printing
                    var psi = new ProcessStartInfo
                    {
                        FileName = pdfPath,
                        Verb = "printto",
                        Arguments = $"\"{printerName}\"",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    return process != null;
                }
                catch
                {
                    return false;
                }
            });
        }

        #region Raw Printer Helper (P/Invoke)

        [StructLayout(LayoutKind.Sequential)]
        private struct DOCINFO
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDocName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDataType;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int StartDocPrinter(IntPtr hPrinter, int level, ref DOCINFO pDocInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);

        private bool SendRawDataToPrinter(string printerName, byte[] data, string dataType)
        {
            IntPtr hPrinter = IntPtr.Zero;
            try
            {
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                    return false;

                var docInfo = new DOCINFO
                {
                    pDocName = "PDF Direct Print",
                    pOutputFile = null,
                    pDataType = dataType
                };

                if (StartDocPrinter(hPrinter, 1, ref docInfo) == 0)
                    return false;

                if (!StartPagePrinter(hPrinter))
                {
                    EndDocPrinter(hPrinter);
                    return false;
                }

                if (!WritePrinter(hPrinter, data, data.Length, out int written) || written != data.Length)
                {
                    EndPagePrinter(hPrinter);
                    EndDocPrinter(hPrinter);
                    return false;
                }

                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (hPrinter != IntPtr.Zero)
                    ClosePrinter(hPrinter);
            }
        }

        #endregion
    }
}
