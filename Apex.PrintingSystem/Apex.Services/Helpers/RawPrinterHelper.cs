using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Apex.Services.Logging;

namespace Apex.Services.Helpers
{
    public static class RawPrinterHelper
    {
        // Structure and API declarions:
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, Int32 level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, Int32 dwCount, out Int32 dwWritten);

        public static bool SendBytesToPrinter(string szPrinterName, IntPtr pBytes, Int32 dwCount)
        {
            return SendBytesToPrinterWithRetry(szPrinterName, pBytes, dwCount, 3);
        }

        public static bool SendBytesToPrinterWithRetry(string szPrinterName, IntPtr pBytes, Int32 dwCount, int maxRetries)
        {
            int retryCount = 0;
            while (retryCount <= maxRetries)
            {
                if (TrySendBytesToPrinter(szPrinterName, pBytes, dwCount))
                {
                    return true;
                }
                retryCount++;
                System.Threading.Thread.Sleep(500 * retryCount); // Exponential backoff
            }
            return false;
        }

        private static bool TrySendBytesToPrinter(string szPrinterName, IntPtr pBytes, Int32 dwCount)
        {
            Int32 dwWritten = 0;
            IntPtr hPrinter = IntPtr.Zero;
            DOCINFOA di = new DOCINFOA();

            di.pDocName = "Apex Print Job";
            di.pDataType = "RAW";

            try
            {
                // CRITICAL: Check each Win32 API call and throw detailed exception on failure
                
                if (!OpenPrinter(szPrinterName.Normalize(), out hPrinter, IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    PrintLogger.Win32Error(error, "OpenPrinter", szPrinterName, 
                        "Failed to open printer handle. Printer may not exist, be offline, or insufficient permissions.");
                    throw new Win32Exception(error, 
                        $"OpenPrinter failed for '{szPrinterName}': {GetWin32ErrorMessage(error)}");
                }

                if (!StartDocPrinter(hPrinter, 1, di))
                {
                    int error = Marshal.GetLastWin32Error();
                    PrintLogger.Win32Error(error, "StartDocPrinter", szPrinterName, 
                        "Failed to start print job. Spooler may be stopped or printer busy.");
                    throw new Win32Exception(error, 
                        $"StartDocPrinter failed for '{szPrinterName}': {GetWin32ErrorMessage(error)}");
                }

                if (!StartPagePrinter(hPrinter))
                {
                    int error = Marshal.GetLastWin32Error();
                    EndDocPrinter(hPrinter); // Clean up document
                    PrintLogger.Win32Error(error, "StartPagePrinter", szPrinterName);
                    throw new Win32Exception(error, 
                        $"StartPagePrinter failed for '{szPrinterName}': {GetWin32ErrorMessage(error)}");
                }

                if (!WritePrinter(hPrinter, pBytes, dwCount, out dwWritten))
                {
                    int error = Marshal.GetLastWin32Error();
                    EndPagePrinter(hPrinter);
                    EndDocPrinter(hPrinter);
                    PrintLogger.Win32Error(error, "WritePrinter", szPrinterName, 
                        $"Attempted to write {dwCount} bytes, wrote {dwWritten} bytes");
                    throw new Win32Exception(error, 
                        $"WritePrinter failed for '{szPrinterName}': {GetWin32ErrorMessage(error)}. Bytes to write: {dwCount}, Written: {dwWritten}");
                }

                if (dwWritten != dwCount)
                {
                    EndPagePrinter(hPrinter);
                    EndDocPrinter(hPrinter);
                    PrintLogger.Error(null, 
                        "WritePrinter incomplete write. Printer: {Printer}, Expected: {Expected}, Written: {Written}", 
                        szPrinterName, dwCount, dwWritten);
                    throw new IOException(
                        $"WritePrinter incomplete write for '{szPrinterName}'. Expected: {dwCount}, Written: {dwWritten}");
                }

                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
                
                PrintLogger.Info("Print job submitted successfully. Printer: '{Printer}', Bytes: {Bytes}", 
                    szPrinterName, dwWritten);
                
                return true;
            }
            finally
            {
                if (hPrinter != IntPtr.Zero)
                {
                    ClosePrinter(hPrinter);
                }
            }
        }

        private static string GetWin32ErrorMessage(int errorCode)
        {
            return errorCode switch
            {
                1801 => "Invalid printer name or printer not found (ERROR_INVALID_PRINTER_NAME)",
                5 => "Access denied - check permissions (ERROR_ACCESS_DENIED)",
                1722 => "RPC server unavailable - printer offline or network issue (ERROR_RPC_S_SERVER_UNAVAILABLE)",
                2 => "File not found (ERROR_FILE_NOT_FOUND)",
                1814 => "Printer driver not installed (ERROR_UNKNOWN_PRINTER_DRIVER)",
                1804 => "Invalid datatype (ERROR_INVALID_DATATYPE)",
                3 => "Path not found (ERROR_PATH_NOT_FOUND)",
                1117 => "Spooler service timeout (ERROR_SERVICE_REQUEST_TIMEOUT)",
                31 => "Device attached to system not functioning (ERROR_GEN_FAILURE)",
                _ => $"Win32 error code {errorCode}"
            };
        }

        public static bool SendFileToPrinter(string szPrinterName, string szFileName)
        {
            if (!File.Exists(szFileName)) return false;

            FileStream fs = new FileStream(szFileName, FileMode.Open);
            BinaryReader br = new BinaryReader(fs);
            Byte[] bytes = new Byte[fs.Length];
            bool bSuccess = false;
            IntPtr pUnmanagedBytes = new IntPtr(0);
            int nLength;

            nLength = Convert.ToInt32(fs.Length);
            bytes = br.ReadBytes(nLength);
            pUnmanagedBytes = Marshal.AllocCoTaskMem(nLength);
            Marshal.Copy(bytes, 0, pUnmanagedBytes, nLength);
            
            bSuccess = SendBytesToPrinter(szPrinterName, pUnmanagedBytes, nLength);
            
            Marshal.FreeCoTaskMem(pUnmanagedBytes);
            fs.Close();
            return bSuccess;
        }

        public static bool SendStringToPrinter(string szPrinterName, string szString)
        {
            IntPtr pBytes;
            Int32 dwCount;
            dwCount = szString.Length;
            // Use ANSI for compatibility, but consider Unicode for Arabic if printer supports it
            pBytes = Marshal.StringToCoTaskMemAnsi(szString);
            bool success = SendBytesToPrinter(szPrinterName, pBytes, dwCount);
            Marshal.FreeCoTaskMem(pBytes);
            return success;
        }
        
        #region Extended API for Vendor-Aware Streaming
        
        /// <summary>
        /// Open printer and return handle (for chunked writing).
        /// </summary>
        public static bool OpenPrinter(string printerName, out IntPtr hPrinter)
        {
            return OpenPrinter(printerName.Normalize(), out hPrinter, IntPtr.Zero);
        }
        
        /// <summary>
        /// Start a document for chunked writing.
        /// </summary>
        public static bool StartDocument(IntPtr hPrinter, string documentName, string dataType = "RAW")
        {
            var di = new DOCINFOA
            {
                pDocName = documentName,
                pDataType = dataType
            };
            
            if (!StartDocPrinter(hPrinter, 1, di))
                return false;
            
            return StartPagePrinter(hPrinter);
        }
        
        /// <summary>
        /// Write a chunk of data to printer.
        /// </summary>
        public static bool WritePrinter(IntPtr hPrinter, byte[] data)
        {
            var pBytes = Marshal.AllocCoTaskMem(data.Length);
            try
            {
                Marshal.Copy(data, 0, pBytes, data.Length);
                return WritePrinter(hPrinter, pBytes, data.Length, out _);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pBytes);
            }
        }
        
        /// <summary>
        /// End document after chunked writing.
        /// </summary>
        public static bool EndDocument(IntPtr hPrinter)
        {
            EndPagePrinter(hPrinter);
            return EndDocPrinter(hPrinter);
        }
        
        #endregion
    }
}
