using System;

namespace Apex.Core.Interfaces
{
    public interface IPrintJobLogger
    {
        void LogJob(string printerName, string filePath, bool success, string message);
    }
}
