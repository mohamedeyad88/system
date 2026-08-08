using Apex.Services.Printing.RIP.Models;
using Apex.Services.Printing.UniversalRIP;
using System;
using System.Diagnostics;
using System.Linq;

namespace Apex.Services.Printing.UniversalRIP
{
    /// <summary>
    /// Quality Gate - Validates decisions and output before sending to printer.
    /// 
    /// MANDATORY CHECKS:
    /// 1. Text is not rasterized (if printer supports native text)
    /// 2. DPI is adequate (≥300 for raster)
    /// 3. Output language is supported by printer
    /// 4. Raster quality meets minimum standards
    /// </summary>
    public class QualityGate
    {
        /// <summary>
        /// Validates decision before rendering (pre-render validation).
        /// </summary>
        public bool ValidateBeforeRender(
            PageContentProfile contentProfile,
            UniversalRenderDecision decision,
            UniversalPrinterProfile printerProfile)
        {
            // Check 1: Text preservation
            if (contentProfile.HasText &&
                !decision.PreserveText &&
                printerProfile.CanHandleTextNative)
            {
                Debug.WriteLine("[QualityGate] WARNING: Text will be rasterized but printer supports native text");
                return false; // Should preserve text
            }

            // Check 2: Vector preservation
            if (contentProfile.HasVectorGraphics &&
                !decision.PreserveVectors &&
                printerProfile.CanHandleVector)
            {
                Debug.WriteLine("[QualityGate] WARNING: Vectors will be rasterized but printer supports native vectors");
                return false; // Should preserve vectors
            }

            // Check 3: DPI adequacy
            if (decision.RequiresRasterization && decision.RequiredDpi < 300)
            {
                Debug.WriteLine("[QualityGate] ERROR: Rasterization DPI below 300");
                return false; // DPI too low
            }

            // Check 4: Language support
            if (!printerProfile.SupportsLanguage(decision.OutputLanguage))
            {
                Debug.WriteLine($"[QualityGate] ERROR: Output language {decision.OutputLanguage} not supported");
                return false; // Unsupported language
            }

            return true;
        }

        /// <summary>
        /// Validates render result after rendering (post-render validation).
        /// </summary>
        public bool ValidateAfterRender(
            Apex.Services.Printing.RIP.RenderResult renderResult,
            UniversalRenderDecision decision)
        {
            // Check 1: Render success
            if (!renderResult.Success)
            {
                Debug.WriteLine("[QualityGate] ERROR: Render failed");
                return false;
            }

            // Check 2: DPI validation (if rasterized)
            if (renderResult.RasterizedImage != null)
            {
                if (decision.RequiresRasterization && renderResult.RenderedDpi < 300)
                {
                    Debug.WriteLine($"[QualityGate] WARNING: Rendered DPI {renderResult.RenderedDpi} below 300");
                    // Not a hard failure, but logged
                }
            }

            // Check 3: Native output requirement
            if (decision.PreserveText && !renderResult.RequiresNativeOutput)
            {
                Debug.WriteLine("[QualityGate] WARNING: Text preservation requested but native output not generated");
                // Not a hard failure, but logged
            }

            return true;
        }

        /// <summary>
        /// Final validation before sending to printer (pre-send validation).
        /// This is the LAST check before data leaves the system.
        /// </summary>
        public bool ValidateBeforeSending(
            byte[] outputBytes,
            UniversalRenderDecision decision,
            UniversalPrinterProfile printerProfile)
        {
            // Check 1: Output is not empty
            if (outputBytes == null || outputBytes.Length == 0)
            {
                Debug.WriteLine("[QualityGate] ERROR: Output bytes are empty");
                return false;
            }

            // Check 2: Language is definitely supported
            if (!printerProfile.SupportsLanguage(decision.OutputLanguage))
            {
                Debug.WriteLine($"[QualityGate] CRITICAL: About to send unsupported language {decision.OutputLanguage} to printer");
                return false; // BLOCK - Never send unsupported language
            }

            // Check 3: Minimum output size (heuristic)
            if (outputBytes.Length < 100)
            {
                Debug.WriteLine("[QualityGate] WARNING: Output size suspiciously small");
                // Not a hard failure, but logged
            }

            return true;
        }
    }
}
