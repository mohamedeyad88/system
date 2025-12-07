using System.Collections.Generic;

namespace Apex.Core.Models
{
    public class PrinterCapabilities
    {
        public bool CanPrintColor { get; set; } = false;
        public bool CanDuplex { get; set; } = false;
        public List<string> SupportedPaperSizes { get; set; } = new List<string>();
        public List<string> Resolutions { get; set; } = new List<string>();
    }
}
