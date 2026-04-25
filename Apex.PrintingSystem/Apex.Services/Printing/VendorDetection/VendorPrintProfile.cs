using System;

namespace Apex.Services.Printing.VendorDetection
{
    /// <summary>
    /// Vendor-specific print profile with optimized settings.
    /// These profiles are HIDDEN from users and applied automatically.
    /// </summary>
    public record VendorPrintProfile
    {
        /// <summary>Target vendor for this profile.</summary>
        public PrinterVendor Vendor { get; init; }
        
        /// <summary>Human-readable profile name (internal use only).</summary>
        public string ProfileName { get; init; } = string.Empty;
        
        #region Chunk & Buffer Settings
        
        /// <summary>
        /// Data chunk size for streaming (in KB).
        /// HP: Medium (64KB) - faster streaming
        /// Epson: Small (16KB) - safer, more reliable
        /// Generic: Balanced (32KB)
        /// </summary>
        public int ChunkSizeKB { get; init; } = 32;
        
        /// <summary>
        /// Buffer size for print data (in KB).
        /// Larger buffers help with network instability.
        /// </summary>
        public int BufferSizeKB { get; init; } = 128;
        
        /// <summary>
        /// Maximum pages to batch together.
        /// Epson: Smaller batches (1-5) for page-level confirmation
        /// HP: Larger batches (10-20) for speed
        /// </summary>
        public int MaxPagesPerBatch { get; init; } = 10;
        
        #endregion
        
        #region Timing & Retry Settings
        
        /// <summary>
        /// Delay between chunks (in milliseconds).
        /// Epson: Slower (50-100ms) for reliability
        /// HP: Faster (10-20ms) for speed
        /// </summary>
        public int ChunkDelayMs { get; init; } = 20;
        
        /// <summary>
        /// Delay between pages (in milliseconds).
        /// </summary>
        public int PageDelayMs { get; init; } = 100;
        
        /// <summary>
        /// Maximum retry attempts on failure.
        /// </summary>
        public int MaxRetryAttempts { get; init; } = 3;
        
        /// <summary>
        /// Delay before retry (in milliseconds).
        /// </summary>
        public int RetryDelayMs { get; init; } = 1000;
        
        /// <summary>
        /// Timeout for printer response (in seconds).
        /// Network printers may need longer timeouts.
        /// </summary>
        public int PrinterTimeoutSeconds { get; init; } = 30;
        
        #endregion
        
        #region Print Method Preferences
        
        /// <summary>
        /// Prefer RAW printing when possible (faster, less processing).
        /// HP strongly prefers RAW.
        /// </summary>
        public bool PreferRawPrinting { get; init; } = false;
        
        /// <summary>
        /// Use page-level confirmation (slower but safer).
        /// Epson strongly prefers this.
        /// </summary>
        public bool UsePageConfirmation { get; init; } = false;
        
        /// <summary>
        /// Stream data progressively vs. send all at once.
        /// Network printers benefit from streaming.
        /// </summary>
        public bool UseProgressiveStreaming { get; init; } = true;
        
        /// <summary>
        /// Use printer's internal job queue when available.
        /// </summary>
        public bool UsePrinterJobQueue { get; init; } = true;
        
        #endregion
        
        #region Network-Specific Settings
        
        /// <summary>
        /// Additional timeout for network printers (added to base timeout).
        /// </summary>
        public int NetworkExtraTimeoutSeconds { get; init; } = 15;
        
        /// <summary>
        /// Number of connection attempts for network printers.
        /// </summary>
        public int NetworkConnectionRetries { get; init; } = 3;
        
        /// <summary>
        /// Send keep-alive signals for long jobs.
        /// </summary>
        public bool UseNetworkKeepAlive { get; init; } = true;
        
        /// <summary>
        /// Keep-alive interval (in seconds).
        /// </summary>
        public int KeepAliveIntervalSeconds { get; init; } = 30;
        
        #endregion
        
        #region Error Handling
        
        /// <summary>
        /// Automatically retry on spooler errors.
        /// </summary>
        public bool AutoRetrySpoolerErrors { get; init; } = true;
        
        /// <summary>
        /// Clear spooler queue on persistent errors.
        /// </summary>
        public bool ClearQueueOnPersistentError { get; init; } = false;
        
        /// <summary>
        /// Fall back to GDI if direct printing fails.
        /// </summary>
        public bool FallbackToGdiOnError { get; init; } = true;
        
        #endregion
        
        /// <summary>
        /// Get effective timeout for this printer type and connection.
        /// </summary>
        public int GetEffectiveTimeout(bool isNetworkPrinter)
        {
            var timeout = PrinterTimeoutSeconds;
            if (isNetworkPrinter)
                timeout += NetworkExtraTimeoutSeconds;
            return timeout;
        }
        
        /// <summary>
        /// Get effective chunk size in bytes.
        /// </summary>
        public int GetChunkSizeBytes() => ChunkSizeKB * 1024;
        
        /// <summary>
        /// Get effective buffer size in bytes.
        /// </summary>
        public int GetBufferSizeBytes() => BufferSizeKB * 1024;
    }
    
    /// <summary>
    /// Factory for creating vendor-specific profiles.
    /// All profiles are predefined and cannot be modified by users.
    /// </summary>
    public static class VendorProfileFactory
    {
        /// <summary>
        /// Get the appropriate profile for a vendor.
        /// </summary>
        public static VendorPrintProfile GetProfile(PrinterVendor vendor)
        {
            return vendor switch
            {
                PrinterVendor.HP => HpProfile,
                PrinterVendor.Epson => EpsonProfile,
                PrinterVendor.Canon => CanonProfile,
                PrinterVendor.Brother => BrotherProfile,
                PrinterVendor.Xerox => EnterpriseProfile,
                PrinterVendor.Ricoh => EnterpriseProfile,
                _ => GenericProfile
            };
        }
        
        /// <summary>
        /// Get profile based on printer metadata (most accurate).
        /// </summary>
        public static VendorPrintProfile GetProfile(PrinterMetadata metadata)
        {
            var baseProfile = GetProfile(metadata.Vendor);
            
            // Adjust for network printers
            if (metadata.IsNetworkPrinter)
            {
                // Network printers need more careful handling
                return baseProfile with
                {
                    MaxRetryAttempts = baseProfile.MaxRetryAttempts + 1,
                    PrinterTimeoutSeconds = baseProfile.PrinterTimeoutSeconds + 10,
                    UseNetworkKeepAlive = true
                };
            }
            
            return baseProfile;
        }
        
        #region Predefined Profiles
        
        /// <summary>
        /// HP Profile - Optimized for speed with RAW printing.
        /// HP printers handle data quickly and prefer direct data streams.
        /// </summary>
        private static readonly VendorPrintProfile HpProfile = new()
        {
            Vendor = PrinterVendor.HP,
            ProfileName = "HP Speed Optimized",
            
            // Medium chunks, fast streaming
            ChunkSizeKB = 64,
            BufferSizeKB = 256,
            MaxPagesPerBatch = 20,
            
            // Fast timing - PRIORITY: Speed (minimal delays)
            ChunkDelayMs = 0,  // No delay for speed
            PageDelayMs = 0,   // No delay for speed
            
            // Fewer retries (HP is reliable)
            MaxRetryAttempts = 2,
            RetryDelayMs = 500,
            PrinterTimeoutSeconds = 30,
            
            // Prefer RAW printing
            PreferRawPrinting = true,
            UsePageConfirmation = false,
            UseProgressiveStreaming = true,
            UsePrinterJobQueue = true,
            
            // Network settings
            NetworkExtraTimeoutSeconds = 10,
            NetworkConnectionRetries = 2,
            UseNetworkKeepAlive = true,
            KeepAliveIntervalSeconds = 45,
            
            // Error handling
            AutoRetrySpoolerErrors = true,
            ClearQueueOnPersistentError = false,
            FallbackToGdiOnError = true
        };
        
        /// <summary>
        /// Epson Profile - Optimized for reliability with careful pacing.
        /// Epson printers work best with smaller, confirmed data transfers.
        /// </summary>
        private static readonly VendorPrintProfile EpsonProfile = new()
        {
            Vendor = PrinterVendor.Epson,
            ProfileName = "Epson Reliability Optimized",
            
            // Smaller chunks, higher buffering
            ChunkSizeKB = 16,
            BufferSizeKB = 512,
            MaxPagesPerBatch = 5,
            
            // Optimized timing - PRIORITY: Speed (minimal delays)
            ChunkDelayMs = 5,   // Minimal delay
            PageDelayMs = 10,   // Minimal delay
            
            // More retries (for reliability)
            MaxRetryAttempts = 4,
            RetryDelayMs = 2000,
            PrinterTimeoutSeconds = 45,
            
            // Page-level confirmation
            PreferRawPrinting = false,
            UsePageConfirmation = true,
            UseProgressiveStreaming = true,
            UsePrinterJobQueue = true,
            
            // Network settings (more conservative)
            NetworkExtraTimeoutSeconds = 20,
            NetworkConnectionRetries = 4,
            UseNetworkKeepAlive = true,
            KeepAliveIntervalSeconds = 20,
            
            // Error handling
            AutoRetrySpoolerErrors = true,
            ClearQueueOnPersistentError = true,
            FallbackToGdiOnError = true
        };
        
        /// <summary>
        /// Canon Profile - Balanced between speed and reliability.
        /// </summary>
        private static readonly VendorPrintProfile CanonProfile = new()
        {
            Vendor = PrinterVendor.Canon,
            ProfileName = "Canon Balanced",
            
            ChunkSizeKB = 32,
            BufferSizeKB = 256,
            MaxPagesPerBatch = 10,
            
            ChunkDelayMs = 0,   // No delay for speed
            PageDelayMs = 5,    // Minimal delay
            
            MaxRetryAttempts = 3,
            RetryDelayMs = 1000,
            PrinterTimeoutSeconds = 35,
            
            PreferRawPrinting = false,
            UsePageConfirmation = false,
            UseProgressiveStreaming = true,
            UsePrinterJobQueue = true,
            
            NetworkExtraTimeoutSeconds = 15,
            NetworkConnectionRetries = 3,
            UseNetworkKeepAlive = true,
            KeepAliveIntervalSeconds = 30,
            
            AutoRetrySpoolerErrors = true,
            ClearQueueOnPersistentError = false,
            FallbackToGdiOnError = true
        };
        
        /// <summary>
        /// Brother Profile - Similar to HP (fast, reliable).
        /// </summary>
        private static readonly VendorPrintProfile BrotherProfile = new()
        {
            Vendor = PrinterVendor.Brother,
            ProfileName = "Brother Speed Optimized",
            
            ChunkSizeKB = 48,
            BufferSizeKB = 192,
            MaxPagesPerBatch = 15,
            
            ChunkDelayMs = 0,   // No delay for speed
            PageDelayMs = 5,    // Minimal delay
            
            MaxRetryAttempts = 2,
            RetryDelayMs = 750,
            PrinterTimeoutSeconds = 30,
            
            PreferRawPrinting = true,
            UsePageConfirmation = false,
            UseProgressiveStreaming = true,
            UsePrinterJobQueue = true,
            
            NetworkExtraTimeoutSeconds = 10,
            NetworkConnectionRetries = 2,
            UseNetworkKeepAlive = true,
            KeepAliveIntervalSeconds = 40,
            
            AutoRetrySpoolerErrors = true,
            ClearQueueOnPersistentError = false,
            FallbackToGdiOnError = true
        };
        
        /// <summary>
        /// Enterprise Profile - For Xerox, Ricoh, and similar enterprise printers.
        /// </summary>
        private static readonly VendorPrintProfile EnterpriseProfile = new()
        {
            Vendor = PrinterVendor.Xerox,
            ProfileName = "Enterprise Optimized",
            
            // Large chunks for enterprise printers
            ChunkSizeKB = 128,
            BufferSizeKB = 512,
            MaxPagesPerBatch = 50,
            
            ChunkDelayMs = 0,   // No delay for speed
            PageDelayMs = 0,    // No delay for speed
            
            MaxRetryAttempts = 2,
            RetryDelayMs = 500,
            PrinterTimeoutSeconds = 60,
            
            PreferRawPrinting = true,
            UsePageConfirmation = false,
            UseProgressiveStreaming = true,
            UsePrinterJobQueue = true,
            
            NetworkExtraTimeoutSeconds = 20,
            NetworkConnectionRetries = 3,
            UseNetworkKeepAlive = true,
            KeepAliveIntervalSeconds = 60,
            
            AutoRetrySpoolerErrors = true,
            ClearQueueOnPersistentError = false,
            FallbackToGdiOnError = true
        };
        
        /// <summary>
        /// Generic Profile - Safe defaults for unknown printers.
        /// </summary>
        private static readonly VendorPrintProfile GenericProfile = new()
        {
            Vendor = PrinterVendor.Generic,
            ProfileName = "Generic Balanced",
            
            ChunkSizeKB = 32,
            BufferSizeKB = 128,
            MaxPagesPerBatch = 10,
            
            ChunkDelayMs = 0,   // No delay for speed
            PageDelayMs = 5,    // Minimal delay
            
            MaxRetryAttempts = 3,
            RetryDelayMs = 1000,
            PrinterTimeoutSeconds = 30,
            
            PreferRawPrinting = false,
            UsePageConfirmation = false,
            UseProgressiveStreaming = true,
            UsePrinterJobQueue = true,
            
            NetworkExtraTimeoutSeconds = 15,
            NetworkConnectionRetries = 3,
            UseNetworkKeepAlive = false,
            KeepAliveIntervalSeconds = 30,
            
            AutoRetrySpoolerErrors = true,
            ClearQueueOnPersistentError = false,
            FallbackToGdiOnError = true
        };
        
        #endregion
    }
}
