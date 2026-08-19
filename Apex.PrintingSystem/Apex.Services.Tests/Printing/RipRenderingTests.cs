using Apex.Services.Printing.RIP;
using Apex.Services.Printing.RIP.Models;
using Apex.Services.Printing.UniversalRIP;
using Apex.Services.Printing.VendorDetection;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// Covers the two defects that made a field run print one page per printer and
/// then report that nothing had printed at all.
/// </summary>
public class RipRenderingTests
{
    private static string WriteA4Pdf(int pages)
    {
        var path = Path.Combine(Path.GetTempPath(), $"apex_rip_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, Imposition.PdfOperationsServiceTests.MakePdf(pages));
        return path;
    }

    /// <summary>
    /// PdfiumViewer's Render(page, dpiX, dpiY, flags) overload sizes the bitmap from
    /// the page's size in POINTS and ignores the dpi for sizing, so every page came
    /// out 595x841 — 72 dpi — no matter what the engine asked for.
    /// </summary>
    [Theory]
    [InlineData(150)]
    [InlineData(300)]
    public async Task APageIsRasterisedAtTheResolutionThatWasAskedFor(int dpi)
    {
        var pdf = WriteA4Pdf(1);
        try
        {
            var strategy = new HybridRenderStrategy();
            var decision = new RenderDecision
            {
                Strategy = RenderStrategy.HighDpiRaster,
                RequiredDpi = dpi,
                RequiresRasterization = true
            };

            var result = await strategy.RenderPageAsync(pdf, 0, decision, new PageContentProfile());
            try
            {
                Assert.NotNull(result.RasterizedImage);

                // A4 is 595pt wide; at `dpi` that is 595/72*dpi pixels. Allow a pixel
                // of rounding either way.
                int expected = (int)Math.Round(595 / 72.0 * dpi);
                Assert.InRange(result.RasterizedImage!.Width, expected - 2, expected + 2);
            }
            finally { result.Dispose(); }
        }
        finally
        {
            File.Delete(pdf);
        }
    }

    /// <summary>
    /// A text-only page routes to NativeVector, which used to return a null bitmap.
    /// Every output generator needs a bitmap, so the page produced zero bytes, the
    /// quality gate rejected it, and the exception killed the remaining pages.
    /// </summary>
    [Fact]
    public async Task ATextOnlyPageStillProducesSomethingToPrint()
    {
        var pdf = WriteA4Pdf(1);
        try
        {
            var textOnly = new PageContentProfile
            {
                HasText = true,
                HasImages = false,
                HasVectorGraphics = false
            };

            var printer = new UniversalPrinterProfile
            {
                PrinterName = "Test PCL",
                PrimaryLanguage = PrintLanguage.PCL,
                SupportedLanguages = new List<PrintLanguage> { PrintLanguage.PCL, PrintLanguage.GDI },
                CanHandleTextNative = true,
                SupportsColor = false
            };

            var decision = new UniversalDecisionEngine()
                .MakeDecision(textOnly, printer, QualityLevel.Professional);

            // The decision itself is unchanged — this page really is text-only.
            Assert.Equal(RenderStrategy.NativeVector, decision.Strategy);

            var rendered = await new HybridRenderStrategy().RenderPageAsync(
                pdf, 0,
                new RenderDecision
                {
                    Strategy = decision.Strategy,
                    RequiredDpi = decision.RequiredDpi,
                    OutputFormat = decision.OutputLanguage,
                    QualityLevel = decision.QualityLevel,
                    RequiresRasterization = decision.RequiresRasterization,
                    PreserveText = decision.PreserveText,
                    PreserveVectors = decision.PreserveVectors
                },
                textOnly);

            var bytes = await new SafeOutputStrategy().GenerateSafeOutputAsync(
                new Apex.Services.Printing.UniversalRIP.RenderResult
                {
                    RasterImage = rendered.RasterizedImage,
                    SourcePdfPath = pdf,
                    IsNativeVector = rendered.RequiresNativeOutput
                },
                decision);

            Assert.NotEmpty(bytes);
            Assert.True(new QualityGate().ValidateBeforeSending(bytes, decision, printer),
                "the quality gate rejected the page, which aborts every page after it");

            rendered.Dispose();
        }
        finally
        {
            File.Delete(pdf);
        }
    }
}
