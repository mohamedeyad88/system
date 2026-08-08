namespace Apex.Services.Printing.VendorDetection
{
    /// <summary>
    /// Supported printer vendors with optimized profiles.
    /// </summary>
    public enum PrinterVendor
    {
        /// <summary>Generic/unknown vendor - uses balanced default settings.</summary>
        Generic = 0,

        /// <summary>HP printers - optimized for speed with RAW printing.</summary>
        HP = 1,

        /// <summary>Epson printers - optimized for reliability with careful pacing.</summary>
        Epson = 2,

        /// <summary>Canon printers - balanced approach.</summary>
        Canon = 3,

        /// <summary>Brother printers - similar to HP profile.</summary>
        Brother = 4,

        /// <summary>Xerox printers - enterprise optimization.</summary>
        Xerox = 5,

        /// <summary>Ricoh printers - enterprise optimization.</summary>
        Ricoh = 6
    }

    /// <summary>
    /// Print language/protocol supported by printer.
    /// </summary>
    public enum PrintLanguage
    {
        Unknown = 0,
        PCL = 1,        // HP Printer Command Language
        PostScript = 2, // Adobe PostScript
        ESCPOS = 3,     // Epson ESC/POS
        ESCPage = 4,    // Epson ESC/Page
        GDI = 5,        // Windows GDI rendering
        PDF = 6,        // Direct PDF support
        XPS = 7         // Microsoft XPS
    }

    /// <summary>
    /// Printer connection type.
    /// </summary>
    public enum PrinterConnectionType
    {
        Unknown = 0,
        USB = 1,
        Network = 2,    // TCP/IP
        WiFi = 3,
        Bluetooth = 4,
        Parallel = 5,   // LPT port
        Serial = 6,     // COM port
        Cloud = 7       // Cloud printing
    }
}
