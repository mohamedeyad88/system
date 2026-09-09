using System.Collections.Generic;
using System.Drawing.Printing;

namespace Apex.Services.Numbering
{
    /// <summary>
    /// Abstraction over tray-availability verification.
    /// Allows unit tests to inject a stub that bypasses WMI calls.
    /// </summary>
    public interface ITrayVerificationService
    {
        TrayVerificationResult Verify(string printerName, Dictionary<int, int> trayMapping);
    }
}
