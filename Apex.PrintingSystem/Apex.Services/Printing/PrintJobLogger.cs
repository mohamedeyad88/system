using Apex.Core.Interfaces;
using Serilog;
using System;

namespace Apex.Services.Printing
{
    public class PrintJobLogger : IPrintJobLogger
    {
        public void LogJob(string printerName, string filePath, bool success, string message)
        {
            if (success)
            {
                Log.Information("Print Job Success | Printer: {PrinterName} | File: {FilePath} | Msg: {Message}", printerName, filePath, message);
            }
            else
            {
                Log.Error("Print Job Failed | Printer: {PrinterName} | File: {FilePath} | Error: {Message}", printerName, filePath, message);
            }
        }
    }
}
