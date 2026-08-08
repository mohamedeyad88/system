using Apex.Services.Printing.RIP.Models;
using Apex.Services.Printing.VendorDetection;
using System;
using System.Linq;

namespace Apex.Services.Printing.RIP
{
    /// <summary>
    /// RIP Decision Engine - Determines optimal rendering strategy based on content analysis.
    /// 
    /// RIP-LIKE BEHAVIOR:
    /// - Text → Always native/vector
    /// - Vector graphics → Preserve paths
    /// - Images → Raster only when necessary, at high DPI
    /// - Mixed content → Hybrid output
    /// 
    /// Rasterization is NEVER the default - it's a last resort.
    /// </summary>
    public class RipDecisionEngine
    {
        /// <summary>
        /// Make rendering decision based on content profile and printer capabilities.
        /// </summary>
        public RenderDecision MakeDecision(
            PageContentProfile contentProfile,
            PrinterMetadata printerMetadata,
            QualityLevel qualityLevel = QualityLevel.Professional)
        {
            var decision = new RenderDecision
            {
                PrinterVendor = printerMetadata.Vendor,
                QualityLevel = qualityLevel
            };

            // Determine output format based on printer capabilities
            decision.OutputFormat = DetermineOutputFormat(printerMetadata);

            // Apply RIP logic rules
            ApplyRipRules(contentProfile, decision);

            return decision;
        }

        /// <summary>
        /// Make decision for entire document (uses sample or first page analysis).
        /// </summary>
        public RenderDecision MakeDocumentDecision(
            PageContentProfile representativeProfile,
            PrinterMetadata printerMetadata,
            QualityLevel qualityLevel = QualityLevel.Professional)
        {
            return MakeDecision(representativeProfile, printerMetadata, qualityLevel);
        }

        #region Private Decision Logic

        private void ApplyRipRules(PageContentProfile profile, RenderDecision decision)
        {
            // ═══════════════════════════════════════════════════════════════════
            // RIP RULE 1: Text Only → Native Vector
            // ═══════════════════════════════════════════════════════════════════
            if (profile.HasText && !profile.HasImages && !profile.HasVectorGraphics)
            {
                decision.Strategy = RenderStrategy.NativeVector;
                decision.PreserveText = true;
                decision.PreserveVectors = false;
                decision.RequiresRasterization = false;
                decision.RequiredDpi = 0; // Not needed
                decision.DecisionReason = "Text-only content - using native vector rendering";
                return;
            }

            // ═══════════════════════════════════════════════════════════════════
            // RIP RULE 2: Vector Graphics Only → Preserve Paths
            // ═══════════════════════════════════════════════════════════════════
            if (profile.HasVectorGraphics && !profile.HasImages)
            {
                decision.Strategy = RenderStrategy.NativeVector;
                decision.PreserveText = profile.HasText;
                decision.PreserveVectors = true;
                decision.RequiresRasterization = false;
                decision.RequiredDpi = 0;
                decision.DecisionReason = "Vector graphics only - preserving paths as native";
                return;
            }

            // ═══════════════════════════════════════════════════════════════════
            // RIP RULE 3: Images Only → High DPI Raster
            // ═══════════════════════════════════════════════════════════════════
            if (profile.HasImages && !profile.HasText && !profile.HasVectorGraphics)
            {
                decision.Strategy = RenderStrategy.HighDpiRaster;
                decision.PreserveText = false;
                decision.PreserveVectors = false;
                decision.RequiresRasterization = true;
                decision.RequiredDpi = CalculateRequiredDpi(profile, decision.QualityLevel);
                decision.DecisionReason = $"Images only - rasterizing at {decision.RequiredDpi} DPI";
                return;
            }

            // ═══════════════════════════════════════════════════════════════════
            // RIP RULE 4: Mixed Content → Hybrid (Preferred)
            // ═══════════════════════════════════════════════════════════════════
            if (profile.IsMixedContent || (profile.HasText && profile.HasImages))
            {
                decision.Strategy = RenderStrategy.Hybrid;
                decision.PreserveText = true; // Always preserve text
                decision.PreserveVectors = profile.HasVectorGraphics;
                decision.RequiresRasterization = true; // For images only
                decision.RequiredDpi = CalculateRequiredDpi(profile, decision.QualityLevel);
                decision.DecisionReason = $"Mixed content - hybrid rendering (text/vector native, images at {decision.RequiredDpi} DPI)";
                return;
            }

            // ═══════════════════════════════════════════════════════════════════
            // RIP RULE 5: Fallback (should rarely be reached)
            // ═══════════════════════════════════════════════════════════════════
            decision.Strategy = RenderStrategy.FallbackRaster;
            decision.PreserveText = false;
            decision.PreserveVectors = false;
            decision.RequiresRasterization = true;
            decision.RequiredDpi = (int)decision.QualityLevel;
            decision.DecisionReason = "Fallback rasterization (unexpected content type)";
        }

        private int CalculateRequiredDpi(PageContentProfile profile, QualityLevel qualityLevel)
        {
            // Base DPI from quality level
            int baseDpi = (int)qualityLevel;

            // Adjust based on image resolution in source
            if (profile.HasImages && profile.MinImageResolution > 0)
            {
                // Use source resolution if it's adequate, otherwise use quality level
                if (profile.MinImageResolution >= baseDpi)
                {
                    // Source has adequate resolution - use it
                    return (int)Math.Min(profile.MinImageResolution, 1200); // Cap at 1200 DPI
                }
                else if (profile.MinImageResolution < 150)
                {
                    // Very low resolution - use higher DPI to compensate
                    return Math.Max(baseDpi, 600);
                }
            }

            return baseDpi;
        }

        private PrintLanguage DetermineOutputFormat(PrinterMetadata metadata)
        {
            // Determine best output format based on printer capabilities
            var capabilities = metadata.Capabilities ?? new PrinterCapabilities();

            // Check printer language support from metadata
            if (metadata.PrintLanguage != PrintLanguage.Unknown)
            {
                return metadata.PrintLanguage;
            }

            // Fallback to capabilities
            if (capabilities.SupportsPostScript)
                return PrintLanguage.PostScript;

            if (capabilities.SupportsPcl)
                return PrintLanguage.PCL;

            if (metadata.Vendor == PrinterVendor.Epson)
                return PrintLanguage.ESCPage; // ESC/Page for Epson

            if (capabilities.SupportsPdf)
                return PrintLanguage.PDF;

            // Fallback to PostScript (most universal)
            return PrintLanguage.PostScript;
        }

        #endregion
    }
}
