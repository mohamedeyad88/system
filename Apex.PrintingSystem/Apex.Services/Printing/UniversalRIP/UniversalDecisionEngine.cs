using Apex.Services.Printing.RIP.Models;
using Apex.Services.Printing.UniversalRIP;
using Apex.Services.Printing.VendorDetection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Services.Printing.UniversalRIP
{
    /// <summary>
    /// Universal Decision Engine - Makes optimal rendering decisions based on:
    /// 1. Content Analysis (what's on the page)
    /// 2. Printer Capabilities (what the printer supports)
    /// 3. Safety Rules (never send unsupported language)
    /// 
    /// GOVERNING PRINCIPLE: Printers are supported by capabilities, not by brand names.
    /// </summary>
    public class UniversalDecisionEngine
    {
        /// <summary>
        /// Makes the optimal rendering decision for a page.
        /// This is the CORE logic of the Universal RIP Engine.
        /// </summary>
        public UniversalRenderDecision MakeDecision(
            PageContentProfile contentProfile,
            UniversalPrinterProfile printerProfile,
            QualityLevel qualityLevel = QualityLevel.Professional)
        {
            var decision = new UniversalRenderDecision
            {
                PrinterProfile = printerProfile,
                ContentProfile = contentProfile,
                QualityLevel = qualityLevel
            };

            // Step 1: Determine output language (MUST be supported by printer)
            decision.OutputLanguage = DetermineSafeOutputLanguage(printerProfile, contentProfile);

            // Step 2: Apply RIP rules based on content type
            ApplyUniversalRipRules(contentProfile, printerProfile, decision);

            // Step 3: Validate decision (Quality Gate)
            ValidateDecision(decision);

            return decision;
        }

        #region Language Determination (Safety-Critical)

        /// <summary>
        /// Determines the SAFE output language that the printer definitely supports.
        /// NEVER returns an unsupported language.
        /// VENDOR-AWARE: Considers printer manufacturer to avoid garbage output.
        /// </summary>
        private PrintLanguage DetermineSafeOutputLanguage(
            UniversalPrinterProfile printerProfile,
            PageContentProfile contentProfile)
        {
            var printerNameLower = printerProfile.PrinterName.ToLowerInvariant();
            bool isEpson = printerNameLower.Contains("epson") || printerNameLower.Contains("workforce") ||
                          printerNameLower.Contains("ecotank");
            bool isEnterprise = printerNameLower.Contains("enterprise") || printerNameLower.Contains("surecolor");
            bool isWFC5210 = printerNameLower.Contains("wf-c5210") || printerNameLower.Contains("wfc5210");

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: EPSON WF-C5210 doesn't support our ESC/P commands
            // Force GDI (Windows native printing) for WF-C5210
            // ═══════════════════════════════════════════════════════════════════
            if (isWFC5210)
            {
                Apex.Services.Logging.PrintLogger.Warning(
                    "[DecisionEngine] EPSON WF-C5210 detected - Forcing GDI (Windows printing)");
                return PrintLanguage.GDI;
            }

            // ═══════════════════════════════════════════════════════════════════
            // EPSON PRINTERS: Prioritize ESC/P over PostScript (unless enterprise)
            // ═══════════════════════════════════════════════════════════════════
            if (isEpson && !isEnterprise)
            {
                // For consumer/prosumer Epson: ESC/P is NATIVE, PostScript is fake
                // Priority: ESC/Page > ESC/P > PCL > PostScript > GDI

                if (printerProfile.SupportsESCPage)
                    return PrintLanguage.ESCPage;

                if (printerProfile.SupportsESCP)
                    return PrintLanguage.ESCPOS;

                if (printerProfile.SupportsPCL)
                    return PrintLanguage.PCL;

                if (printerProfile.SupportsPostScript)
                    return PrintLanguage.PostScript; // Last resort

                return PrintLanguage.GDI;
            }

            // ═══════════════════════════════════════════════════════════════════
            // NON-EPSON OR ENTERPRISE PRINTERS: Traditional priority
            // ═══════════════════════════════════════════════════════════════════
            // Priority order (best quality first):
            // 1. PostScript (best for text/vector)
            // 2. PCL (good for text/vector)
            // 3. PDF (universal, preserves quality)
            // 4. ESC/Page (Epson high-end)
            // 5. ESC/P (Epson basic)
            // 6. GDI (fallback, raster-only)

            // Rule 1: If printer supports PostScript → Use it (best quality)
            if (printerProfile.SupportsPostScript)
            {
                return PrintLanguage.PostScript;
            }

            // Rule 2: If printer supports PCL → Use it (good quality)
            if (printerProfile.SupportsPCL)
            {
                return PrintLanguage.PCL;
            }

            // Rule 3: If printer supports PDF → Use it (preserves quality)
            if (printerProfile.SupportsPDF)
            {
                return PrintLanguage.PDF;
            }

            // Rule 4: If printer supports ESC/Page → Use it (Epson high-end)
            if (printerProfile.SupportsESCPage)
            {
                return PrintLanguage.ESCPage;
            }

            // Rule 5: If printer supports ESC/P → Use it (Epson basic)
            if (printerProfile.SupportsESCP)
            {
                return PrintLanguage.ESCPOS;
            }

            // Rule 6: Fallback to GDI (always available, but raster-only)
            return PrintLanguage.GDI;
        }

        #endregion

        #region Universal RIP Rules

        /// <summary>
        /// Applies universal RIP rules based on content and printer capabilities.
        /// </summary>
        private void ApplyUniversalRipRules(
            PageContentProfile contentProfile,
            UniversalPrinterProfile printerProfile,
            UniversalRenderDecision decision)
        {
            // ═══════════════════════════════════════════════════════════════════
            // RULE 1: Text-Only Content → Native Vector (if printer supports it)
            // ═══════════════════════════════════════════════════════════════════
            if (contentProfile.HasText &&
                !contentProfile.HasImages &&
                !contentProfile.HasVectorGraphics)
            {
                if (printerProfile.CanHandleTextNative)
                {
                    decision.Strategy = RenderStrategy.NativeVector;
                    decision.PreserveText = true;
                    decision.PreserveVectors = false;
                    decision.RequiresRasterization = false;
                    decision.RequiredDpi = 0;
                    decision.DecisionReason = "Text-only: Using native vector output via " + decision.OutputLanguage;
                    return;
                }
                else
                {
                    // Raster-only printer: must rasterize even text
                    decision.Strategy = RenderStrategy.HighDpiRaster;
                    decision.PreserveText = false;
                    decision.RequiresRasterization = true;
                    decision.RequiredDpi = Math.Max(printerProfile.NativeDpi, 300);
                    decision.DecisionReason = $"Text-only but raster-only printer: Rasterizing at {decision.RequiredDpi} DPI";
                    return;
                }
            }

            // ═══════════════════════════════════════════════════════════════════
            // RULE 2: Vector Graphics Only → Preserve Paths (if printer supports it)
            // ═══════════════════════════════════════════════════════════════════
            if (contentProfile.HasVectorGraphics && !contentProfile.HasImages)
            {
                if (printerProfile.CanHandleVector)
                {
                    decision.Strategy = RenderStrategy.NativeVector;
                    decision.PreserveText = contentProfile.HasText;
                    decision.PreserveVectors = true;
                    decision.RequiresRasterization = false;
                    decision.RequiredDpi = 0;
                    decision.DecisionReason = "Vector graphics: Preserving paths as native via " + decision.OutputLanguage;
                    return;
                }
                else
                {
                    // Raster-only: must rasterize vectors
                    decision.Strategy = RenderStrategy.HighDpiRaster;
                    decision.PreserveVectors = false;
                    decision.RequiresRasterization = true;
                    decision.RequiredDpi = Math.Max(printerProfile.NativeDpi, 600);
                    decision.DecisionReason = $"Vector graphics but raster-only printer: Rasterizing at {decision.RequiredDpi} DPI";
                    return;
                }
            }

            // ═══════════════════════════════════════════════════════════════════
            // RULE 3: Images Only → High DPI Raster (always required for images)
            // ═══════════════════════════════════════════════════════════════════
            if (contentProfile.HasImages &&
                !contentProfile.HasText &&
                !contentProfile.HasVectorGraphics)
            {
                decision.Strategy = RenderStrategy.HighDpiRaster;
                decision.PreserveText = false;
                decision.PreserveVectors = false;
                decision.RequiresRasterization = true;
                decision.RequiredDpi = CalculateOptimalDpi(contentProfile, printerProfile, decision.QualityLevel);
                decision.DecisionReason = $"Images only: Rasterizing at {decision.RequiredDpi} DPI";
                return;
            }

            // ═══════════════════════════════════════════════════════════════════
            // RULE 4: Mixed Content → Hybrid (Best Quality)
            // ═══════════════════════════════════════════════════════════════════
            if (contentProfile.IsMixedContent ||
                (contentProfile.HasText && contentProfile.HasImages) ||
                (contentProfile.HasVectorGraphics && contentProfile.HasImages))
            {
                if (printerProfile.CanHandleTextNative || printerProfile.CanHandleVector)
                {
                    // Hybrid: Text/Vector native, Images rasterized
                    decision.Strategy = RenderStrategy.Hybrid;
                    decision.PreserveText = printerProfile.CanHandleTextNative;
                    decision.PreserveVectors = printerProfile.CanHandleVector && contentProfile.HasVectorGraphics;
                    decision.RequiresRasterization = true; // For images only
                    decision.RequiredDpi = CalculateOptimalDpi(contentProfile, printerProfile, decision.QualityLevel);
                    decision.DecisionReason = $"Mixed content: Hybrid rendering (text/vector native, images at {decision.RequiredDpi} DPI)";
                    return;
                }
                else
                {
                    // Raster-only printer: must rasterize everything
                    decision.Strategy = RenderStrategy.HighDpiRaster;
                    decision.PreserveText = false;
                    decision.PreserveVectors = false;
                    decision.RequiresRasterization = true;
                    decision.RequiredDpi = CalculateOptimalDpi(contentProfile, printerProfile, decision.QualityLevel);
                    decision.DecisionReason = $"Mixed content but raster-only printer: Full rasterization at {decision.RequiredDpi} DPI";
                    return;
                }
            }

            // ═══════════════════════════════════════════════════════════════════
            // RULE 5: Fallback (should rarely be reached)
            // ═══════════════════════════════════════════════════════════════════
            decision.Strategy = RenderStrategy.FallbackRaster;
            decision.PreserveText = false;
            decision.PreserveVectors = false;
            decision.RequiresRasterization = true;
            decision.RequiredDpi = Math.Max(printerProfile.NativeDpi, (int)decision.QualityLevel);
            decision.DecisionReason = "Fallback: Unexpected content type, using safe rasterization";
        }

        private int CalculateOptimalDpi(
            PageContentProfile contentProfile,
            UniversalPrinterProfile printerProfile,
            QualityLevel qualityLevel)
        {
            // Base DPI from quality level
            int baseDpi = (int)qualityLevel;

            // Adjust based on printer's native DPI
            int printerDpi = Math.Max(printerProfile.NativeDpi, 300);

            // Use the higher of: quality level, printer native DPI, or source image resolution
            int optimalDpi = Math.Max(baseDpi, printerDpi);

            // If source images have high resolution, try to preserve it (up to printer max)
            if (contentProfile.HasImages && contentProfile.MinImageResolution > 0)
            {
                if (contentProfile.MinImageResolution >= optimalDpi)
                {
                    // Source has adequate resolution - use it (capped at printer max)
                    optimalDpi = (int)Math.Min(contentProfile.MinImageResolution, printerProfile.MaxDpi);
                }
                else if (contentProfile.MinImageResolution < 150)
                {
                    // Very low resolution - use higher DPI to compensate
                    optimalDpi = Math.Max(optimalDpi, 600);
                }
            }

            // Cap at printer's maximum DPI
            optimalDpi = Math.Min(optimalDpi, printerProfile.MaxDpi);

            // Ensure minimum 300 DPI
            return Math.Max(optimalDpi, 300);
        }

        #endregion

        #region Quality Gate Validation

        /// <summary>
        /// Validates the decision before any page is sent.
        /// This is the Quality Gate that prevents unsupported language delivery.
        /// </summary>
        private void ValidateDecision(UniversalRenderDecision decision)
        {
            var errors = new List<string>();

            // ═══════════════════════════════════════════════════════════════════
            // SAFETY CHECK 1: Output language must be supported by printer
            // ═══════════════════════════════════════════════════════════════════
            if (!decision.PrinterProfile.SupportsLanguage(decision.OutputLanguage))
            {
                errors.Add($"CRITICAL: Output language {decision.OutputLanguage} is NOT supported by printer {decision.PrinterProfile.PrinterName}");

                // Auto-correct: Fall back to GDI (always available)
                decision.OutputLanguage = PrintLanguage.GDI;
                decision.Strategy = RenderStrategy.FallbackRaster;
                decision.RequiresRasterization = true;
                decision.RequiredDpi = Math.Max(decision.PrinterProfile.NativeDpi, 300);
                decision.DecisionReason = "SAFETY: Unsupported language detected, auto-corrected to GDI";
            }

            // ═══════════════════════════════════════════════════════════════════
            // SAFETY CHECK 2: Text preservation requires native language support
            // ═══════════════════════════════════════════════════════════════════
            if (decision.PreserveText && !decision.PrinterProfile.CanHandleTextNative)
            {
                errors.Add("WARNING: Text preservation requested but printer cannot handle native text");
                decision.PreserveText = false;
                decision.RequiresRasterization = true;
                decision.RequiredDpi = Math.Max(decision.RequiredDpi, 300);
            }

            // ═══════════════════════════════════════════════════════════════════
            // SAFETY CHECK 3: Vector preservation requires native language support
            // ═══════════════════════════════════════════════════════════════════
            if (decision.PreserveVectors && !decision.PrinterProfile.CanHandleVector)
            {
                errors.Add("WARNING: Vector preservation requested but printer cannot handle native vectors");
                decision.PreserveVectors = false;
                decision.RequiresRasterization = true;
                decision.RequiredDpi = Math.Max(decision.RequiredDpi, 600);
            }

            // ═══════════════════════════════════════════════════════════════════
            // SAFETY CHECK 4: DPI must be within printer capabilities
            // ═══════════════════════════════════════════════════════════════════
            if (decision.RequiresRasterization && decision.RequiredDpi > decision.PrinterProfile.MaxDpi)
            {
                errors.Add($"WARNING: Required DPI {decision.RequiredDpi} exceeds printer max {decision.PrinterProfile.MaxDpi}");
                decision.RequiredDpi = decision.PrinterProfile.MaxDpi;
            }

            // ═══════════════════════════════════════════════════════════════════
            // SAFETY CHECK 5: Minimum DPI for quality
            // ═══════════════════════════════════════════════════════════════════
            if (decision.RequiresRasterization && decision.RequiredDpi < 300)
            {
                errors.Add("WARNING: Rasterization DPI below 300, quality may be degraded");
                decision.RequiredDpi = 300;
            }

            // Log validation errors (if any)
            if (errors.Count > 0)
            {
                decision.ValidationWarnings = errors;
                System.Diagnostics.Debug.WriteLine($"[UniversalDecision] Validation warnings: {string.Join("; ", errors)}");
            }

            decision.IsValid = true; // Decision is valid (may have been auto-corrected)
        }

        #endregion
    }

    /// <summary>
    /// Universal Render Decision - Complete decision with safety validation.
    /// </summary>
    public class UniversalRenderDecision
    {
        public UniversalPrinterProfile PrinterProfile { get; set; } = null!;
        public PageContentProfile ContentProfile { get; set; } = null!;

        public RenderStrategy Strategy { get; set; }
        public PrintLanguage OutputLanguage { get; set; }
        public int RequiredDpi { get; set; }
        public bool PreserveText { get; set; }
        public bool PreserveVectors { get; set; }
        public bool RequiresRasterization { get; set; }
        public QualityLevel QualityLevel { get; set; }
        public string DecisionReason { get; set; } = "";

        public bool IsValid { get; set; }
        public List<string> ValidationWarnings { get; set; } = new();
    }
}
