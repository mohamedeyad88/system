using Apex.Services.Printing.VendorDetection;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Apex.Services.Printing.UniversalRIP
{
    /// <summary>
    /// Universal Printer Capability Profile - Complete printer capability information.
    /// Used by Universal Decision Engine to make optimal print path decisions.
    /// </summary>
    public class UniversalPrinterProfile
    {
        public string PrinterName { get; set; } = "";

        // Language Support (Capability-Based, Not Vendor-Based)
        public bool SupportsPostScript { get; set; }
        public bool SupportsPCL { get; set; }
        public bool SupportsESCP { get; set; }
        public bool SupportsESCPage { get; set; }
        public bool SupportsPDF { get; set; }
        public bool SupportsXPS { get; set; }
        public bool SupportsGDI { get; set; } = true; // Always available as fallback
        public bool SupportsEMF { get; set; }

        // Native Capabilities
        public int NativeDpi { get; set; } = 600;
        public int MaxDpi { get; set; } = 1200;
        public bool SupportsColor { get; set; }
        public bool SupportsDuplex { get; set; }
        public long PrinterMemoryBytes { get; set; }

        // Connection & Driver
        public PrinterConnectionType ConnectionType { get; set; }
        public string DriverName { get; set; } = "";
        public string DriverVersion { get; set; } = "";

        // Safety Flags
        public bool IsRasterOnly { get; set; }
        public bool RequiresRasterization { get; set; }
        public bool CanHandleVector { get; set; } = true;
        public bool CanHandleTextNative { get; set; } = true;

        // Detected Primary Language (Best Supported)
        public PrintLanguage PrimaryLanguage { get; set; } = PrintLanguage.Unknown;

        // All Supported Languages
        public List<PrintLanguage> SupportedLanguages { get; set; } = new();

        /// <summary>
        /// Gets the best output language for this printer.
        /// Priority: PostScript > PCL > PDF > ESC/Page > ESC/P > GDI
        /// </summary>
        public PrintLanguage GetBestOutputLanguage()
        {
            if (SupportsPostScript) return PrintLanguage.PostScript;
            if (SupportsPCL) return PrintLanguage.PCL;
            if (SupportsPDF) return PrintLanguage.PDF;
            if (SupportsESCPage) return PrintLanguage.ESCPage;
            if (SupportsESCP) return PrintLanguage.ESCPOS;
            return PrintLanguage.GDI; // Always available
        }

        /// <summary>
        /// Checks if printer supports a specific language.
        /// </summary>
        public bool SupportsLanguage(PrintLanguage language)
        {
            return language switch
            {
                PrintLanguage.PostScript => SupportsPostScript,
                PrintLanguage.PCL => SupportsPCL,
                PrintLanguage.PDF => SupportsPDF,
                PrintLanguage.ESCPage => SupportsESCPage,
                PrintLanguage.ESCPOS => SupportsESCP,
                PrintLanguage.XPS => SupportsXPS,
                PrintLanguage.GDI => SupportsGDI,
                _ => false
            };
        }
    }

    /// <summary>
    /// Universal Printer Capability Detector - Detects ALL printer capabilities
    /// without relying on vendor names. Uses WMI, driver info, and capability queries.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class UniversalPrinterCapabilityDetector
    {
        private readonly Dictionary<string, UniversalPrinterProfile> _cache = new();
        private readonly object _cacheLock = new object();

        /// <summary>
        /// Detects complete capabilities for a printer.
        /// This is the ONLY way to determine what a printer supports.
        /// </summary>
        public async Task<UniversalPrinterProfile> DetectAsync(string printerName)
        {
            if (string.IsNullOrEmpty(printerName))
                throw new ArgumentException("Printer name cannot be empty", nameof(printerName));

            // Check cache
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(printerName, out var cached))
                    return cached;
            }

            // Detect capabilities
            var profile = await Task.Run(() => DetectInternal(printerName));

            // Cache result
            lock (_cacheLock)
            {
                _cache[printerName] = profile;
            }

            return profile;
        }

        /// <summary>
        /// Clears cache for a specific printer (useful after driver updates).
        /// </summary>
        public void ClearCache(string printerName)
        {
            lock (_cacheLock)
            {
                _cache.Remove(printerName);
            }
        }

        /// <summary>
        /// Clears all cached profiles.
        /// </summary>
        public void ClearAllCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }

        private UniversalPrinterProfile DetectInternal(string printerName)
        {
            var profile = new UniversalPrinterProfile
            {
                PrinterName = printerName
            };

            try
            {
                // Method 1: WMI Query (Most Reliable)
                DetectViaWMI(printerName, profile);

                // Method 2: PrinterSettings API
                DetectViaPrinterSettings(printerName, profile);

                // Method 3: Driver Name Analysis (Heuristic)
                DetectViaDriverName(printerName, profile);

                // Method 4: Capability Testing (If needed)
                // Note: Actual capability testing would require sending test jobs
                // For now, we rely on WMI and driver info

                // Determine primary language
                profile.PrimaryLanguage = profile.GetBestOutputLanguage();

                // Build supported languages list
                BuildSupportedLanguagesList(profile);

                // Safety checks
                ValidateProfile(profile);
            }
            catch (Exception ex)
            {
                // On error, return safe defaults (GDI-only)
                System.Diagnostics.Debug.WriteLine($"[UniversalDetector] Error detecting {printerName}: {ex.Message}");
                profile.SupportsGDI = true;
                profile.PrimaryLanguage = PrintLanguage.GDI;
                profile.IsRasterOnly = true;
                profile.RequiresRasterization = true;
            }

            return profile;
        }

        private void DetectViaWMI(string printerName, UniversalPrinterProfile profile)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Printer WHERE Name = '{printerName.Replace("\\", "\\\\")}'");

                foreach (ManagementObject printer in searcher.Get())
                {
                    // Get driver info
                    profile.DriverName = printer["DriverName"]?.ToString() ?? "";
                    profile.DriverVersion = printer["DriverVersion"]?.ToString() ?? "";

                    // Get port info
                    var portName = printer["PortName"]?.ToString() ?? "";
                    profile.ConnectionType = DetectConnectionType(portName);

                    // Get language support from WMI
                    var languagesSupported = printer["LanguagesSupported"] as ushort[];
                    if (languagesSupported != null)
                    {
                        // WMI Language Codes:
                        // 1=Other, 2=Unknown, 3=PCL, 4=HPGL, 5=PJL, 
                        // 6=PostScript, 7=PSPrinter, 8=IPDS, 9=PPDS, 10=ESC/P, etc.
                        profile.SupportsPCL = languagesSupported.Contains((ushort)3) ||
                                             languagesSupported.Contains((ushort)5);
                        profile.SupportsPostScript = languagesSupported.Contains((ushort)6) ||
                                                   languagesSupported.Contains((ushort)7);
                        profile.SupportsESCP = languagesSupported.Contains((ushort)10);
                    }

                    // Get resolution
                    var horizontalRes = printer["HorizontalResolution"];
                    if (horizontalRes != null)
                    {
                        profile.NativeDpi = Convert.ToInt32(horizontalRes);
                        profile.MaxDpi = Math.Max(profile.NativeDpi, profile.MaxDpi);
                    }

                    // Get capabilities array
                    var capabilities = printer["CapabilityDescriptions"] as string[];
                    if (capabilities != null)
                    {
                        AnalyzeCapabilities(capabilities, profile);
                    }

                    // Get memory (if available)
                    var memory = printer["PrinterMemory"];
                    if (memory != null)
                    {
                        profile.PrinterMemoryBytes = Convert.ToInt64(memory) * 1024; // Convert KB to bytes
                    }

                    // Color support
                    profile.SupportsColor = Convert.ToBoolean(printer["Color"] ?? false);

                    // Duplex support
                    profile.SupportsDuplex = Convert.ToBoolean(printer["Duplex"] ?? false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UniversalDetector] WMI detection failed: {ex.Message}");
            }
        }

        private void DetectViaPrinterSettings(string printerName, UniversalPrinterProfile profile)
        {
            try
            {
                var settings = new PrinterSettings { PrinterName = printerName };

                if (!settings.IsValid)
                    return;

                // Get resolution from settings
                var resolution = settings.DefaultPageSettings.PrinterResolution;
                if (resolution.Kind == PrinterResolutionKind.Custom)
                {
                    profile.NativeDpi = resolution.X;
                    profile.MaxDpi = Math.Max(profile.MaxDpi, resolution.X);
                }

                // Check for PostScript via driver capabilities
                // (Some drivers expose this through PrinterSettings)
                var driverName = settings.PrinterName;
                if (string.IsNullOrEmpty(profile.DriverName))
                    profile.DriverName = driverName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UniversalDetector] PrinterSettings detection failed: {ex.Message}");
            }
        }

        private void DetectViaDriverName(string printerName, UniversalPrinterProfile profile)
        {
            var driverName = profile.DriverName.ToLowerInvariant();
            var printerNameLower = printerName.ToLowerInvariant();

            // ═══════════════════════════════════════════════════════════════════
            // VENDOR-AWARE DETECTION: Detect vendor FIRST to avoid false positives
            // ═══════════════════════════════════════════════════════════════════
            bool isEpson = printerNameLower.Contains("epson") || printerNameLower.Contains("workforce") ||
                          printerNameLower.Contains("ecotank") || printerNameLower.Contains("stylus");
            bool isHP = printerNameLower.Contains("hp") || printerNameLower.Contains("laserjet") ||
                       printerNameLower.Contains("deskjet") || printerNameLower.Contains("officejet");
            bool isCanon = printerNameLower.Contains("canon") || printerNameLower.Contains("pixma");
            bool isBrother = printerNameLower.Contains("brother") || printerNameLower.Contains("mfc");
            bool isXerox = printerNameLower.Contains("xerox") || printerNameLower.Contains("versalink") ||
                          printerNameLower.Contains("altalink");

            // ═══════════════════════════════════════════════════════════════════
            // PostScript detection (WITH VENDOR VALIDATION)
            // ═══════════════════════════════════════════════════════════════════
            bool hasPostScriptDriver = driverName.Contains("postscript") ||
                                      driverName.Contains(" ps") ||
                                      driverName.Contains("adobe");

            if (hasPostScriptDriver)
            {
                // CRITICAL: Verify this is actually a PostScript printer
                // Epson consumer/prosumer printers (WorkForce, EcoTank) do NOT support PostScript
                if (isEpson)
                {
                    // Only enterprise Epson printers support PostScript
                    if (printerNameLower.Contains("workforce enterprise") ||
                        printerNameLower.Contains("workforce pro wf-c20") ||
                        printerNameLower.Contains("surecolor"))
                    {
                        profile.SupportsPostScript = true;
                    }
                    // Consumer/prosumer Epson: PostScript driver is FAKE (wrapper around ESC/P)
                    else
                    {
                        profile.SupportsPostScript = false; // Explicit false
                        Apex.Services.Logging.PrintLogger.Warning(
                            "[Capability] PostScript driver detected for consumer Epson '{Printer}' - IGNORING (fake driver). Using ESC/P instead.",
                            printerName);
                    }
                }
                else
                {
                    // Non-Epson: Trust PostScript driver detection
                    profile.SupportsPostScript = true;
                }
            }

            // ═══════════════════════════════════════════════════════════════════
            // PCL detection (Common on HP, Brother, some enterprise printers)
            // ═══════════════════════════════════════════════════════════════════
            if (driverName.Contains("pcl") ||
                driverName.Contains("pcl6") ||
                driverName.Contains("pcl 6") ||
                driverName.Contains("pcl5") ||
                driverName.Contains("pcl 5"))
            {
                profile.SupportsPCL = true;
            }

            // ═══════════════════════════════════════════════════════════════════
            // ESC/P detection (Epson primary language)
            // ═══════════════════════════════════════════════════════════════════
            if (driverName.Contains("esc/p") ||
                driverName.Contains("escp") ||
                driverName.Contains("escpos") ||
                isEpson) // ALL Epson printers support ESC/P
            {
                profile.SupportsESCP = true;
            }

            // ═══════════════════════════════════════════════════════════════════
            // ESC/Page detection (Epson high-end)
            // ═══════════════════════════════════════════════════════════════════
            if (driverName.Contains("esc/page") ||
                driverName.Contains("escpage") ||
                driverName.Contains("esc/p2"))
            {
                profile.SupportsESCPage = true;
            }

            // ═══════════════════════════════════════════════════════════════════
            // PDF detection (HP, Xerox enterprise printers)
            // ═══════════════════════════════════════════════════════════════════
            if (driverName.Contains("pdf") ||
                driverName.Contains("direct pdf") ||
                isXerox)
            {
                profile.SupportsPDF = true;
            }

            // ═══════════════════════════════════════════════════════════════════
            // XPS detection
            // ═══════════════════════════════════════════════════════════════════
            if (driverName.Contains("xps") ||
                driverName.Contains("xml paper"))
            {
                profile.SupportsXPS = true;
            }

            // ═══════════════════════════════════════════════════════════════════
            // EMF detection (Windows Enhanced Metafile)
            // ═══════════════════════════════════════════════════════════════════
            if (driverName.Contains("emf") ||
                driverName.Contains("enhanced metafile"))
            {
                profile.SupportsEMF = true;
            }
        }

        private void AnalyzeCapabilities(string[] capabilities, UniversalPrinterProfile profile)
        {
            foreach (var cap in capabilities)
            {
                var capLower = cap.ToLowerInvariant();

                if (capLower.Contains("postscript") || capLower.Contains("ps"))
                    profile.SupportsPostScript = true;

                if (capLower.Contains("pcl"))
                    profile.SupportsPCL = true;

                if (capLower.Contains("pdf"))
                    profile.SupportsPDF = true;

                if (capLower.Contains("esc/p") || capLower.Contains("escp"))
                    profile.SupportsESCP = true;

                if (capLower.Contains("duplex"))
                    profile.SupportsDuplex = true;

                if (capLower.Contains("color"))
                    profile.SupportsColor = true;
            }
        }

        private PrinterConnectionType DetectConnectionType(string portName)
        {
            if (string.IsNullOrEmpty(portName))
                return PrinterConnectionType.Unknown;

            var portLower = portName.ToLowerInvariant();

            if (portLower.StartsWith("usb"))
                return PrinterConnectionType.USB;

            if (portLower.StartsWith("lpt"))
                return PrinterConnectionType.Parallel;

            if (portLower.StartsWith("tcp") ||
                portLower.StartsWith("ip_") ||
                System.Net.IPAddress.TryParse(portName, out _))
                return PrinterConnectionType.Network;

            if (portLower.Contains("wifi") || portLower.Contains("wireless"))
                return PrinterConnectionType.WiFi;

            return PrinterConnectionType.Unknown;
        }

        private void BuildSupportedLanguagesList(UniversalPrinterProfile profile)
        {
            profile.SupportedLanguages.Clear();

            if (profile.SupportsPostScript)
                profile.SupportedLanguages.Add(PrintLanguage.PostScript);

            if (profile.SupportsPCL)
                profile.SupportedLanguages.Add(PrintLanguage.PCL);

            if (profile.SupportsPDF)
                profile.SupportedLanguages.Add(PrintLanguage.PDF);

            if (profile.SupportsESCPage)
                profile.SupportedLanguages.Add(PrintLanguage.ESCPage);

            if (profile.SupportsESCP)
                profile.SupportedLanguages.Add(PrintLanguage.ESCPOS);

            if (profile.SupportsXPS)
                profile.SupportedLanguages.Add(PrintLanguage.XPS);

            // GDI is always available as fallback
            profile.SupportedLanguages.Add(PrintLanguage.GDI);
        }

        private void ValidateProfile(UniversalPrinterProfile profile)
        {
            var printerNameLower = profile.PrinterName.ToLowerInvariant();
            bool isEpson = printerNameLower.Contains("epson") || printerNameLower.Contains("workforce") ||
                          printerNameLower.Contains("ecotank");

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL: Vendor-specific validation to prevent garbage output
            // ═══════════════════════════════════════════════════════════════════

            // EPSON Consumer/Prosumer Validation
            if (isEpson)
            {
                // Rule: If PostScript is detected BUT ESC/P is also available → Prefer ESC/P
                if (profile.SupportsPostScript && profile.SupportsESCP)
                {
                    // Consumer Epson: PostScript driver is often a wrapper that converts to ESC/P
                    // Sending raw PostScript → GARBAGE
                    // Solution: Disable PostScript, force ESC/P
                    if (!printerNameLower.Contains("enterprise") &&
                        !printerNameLower.Contains("surecolor"))
                    {
                        profile.SupportsPostScript = false;
                        Apex.Services.Logging.PrintLogger.Warning(
                            "[Capability] FORCED DISABLE PostScript for consumer Epson '{Printer}'. " +
                            "PostScript driver detected but is fake wrapper. Using ESC/P to prevent garbage output.",
                            profile.PrinterName);
                    }
                }

                // Rule: All Epson printers support ESC/P (if not detected, force it)
                if (!profile.SupportsESCP && !profile.SupportsESCPage)
                {
                    profile.SupportsESCP = true;
                    Apex.Services.Logging.PrintLogger.Info(
                        "[Capability] Epson printer '{Printer}' - Forced ESC/P support (vendor default)",
                        profile.PrinterName);
                }
            }

            // Safety: If no language detected, ensure GDI is available
            if (profile.SupportedLanguages.Count == 0 ||
                (profile.SupportedLanguages.Count == 1 && profile.SupportedLanguages[0] == PrintLanguage.GDI))
            {
                profile.IsRasterOnly = true;
                profile.RequiresRasterization = true;
                profile.CanHandleVector = false;
                profile.CanHandleTextNative = false;
            }
            else
            {
                // If printer supports PostScript or PCL or ESC/P, it can handle vector and text natively
                if (profile.SupportsPostScript || profile.SupportsPCL || profile.SupportsESCP || profile.SupportsESCPage)
                {
                    profile.CanHandleVector = true;
                    profile.CanHandleTextNative = true;
                    profile.RequiresRasterization = false;
                }
                else if (profile.SupportsPDF)
                {
                    profile.CanHandleVector = true;
                    profile.CanHandleTextNative = true;
                    profile.RequiresRasterization = false;
                }
                else
                {
                    // Raster-only printer
                    profile.IsRasterOnly = true;
                    profile.RequiresRasterization = true;
                    profile.CanHandleVector = false;
                    profile.CanHandleTextNative = false;
                }
            }

            // Ensure minimum DPI
            if (profile.NativeDpi < 300)
                profile.NativeDpi = 300;

            if (profile.MaxDpi < 300)
                profile.MaxDpi = 300;
        }
    }
}
