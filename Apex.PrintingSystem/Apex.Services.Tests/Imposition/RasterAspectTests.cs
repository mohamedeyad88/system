using Apex.Services.Imposition;
using PdfSharpCore.Pdf;

namespace Apex.Services.Tests.Imposition;

public class RasterAspectTests
{
    private static (int w, int h) PngSize(byte[] png) =>
        ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19],
         (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);

    [Fact]
    public void RenderPng_PreservesPortraitAspect()
    {
        using var doc = new PdfDocument();
        var p = doc.AddPage();
        p.Width = 100;   // points
        p.Height = 200;  // ratio 0.5 (tall portrait)
        using var ms = new MemoryStream();
        doc.Save(ms, false);

        var png = PdfRasterizer.RenderPng(ms.ToArray(), 0, 150);
        var (w, h) = PngSize(png);

        // Expect the image to keep the page's 0.5 width:height ratio.
        Assert.True(System.Math.Abs((double)w / h - 0.5) < 0.05, $"expected ~0.5 ratio, got {w}x{h}");
    }

    [Fact]
    public void RenderPng_HasUniformDpi_SoWpfShowsTruePixelAspect()
    {
        // Pdfium stamps a non-square DPI whose ratio equals the page aspect, which made WPF
        // display a correct-pixel A4 as a SQUARE. The rasteriser must normalise it to a
        // uniform DPI so the on-screen (device-independent) aspect matches the pixels.
        using var doc = new PdfDocument();
        var p = doc.AddPage();
        p.Width = 100; p.Height = 200;
        using var ms = new MemoryStream();
        doc.Save(ms, false);

        var png = PdfRasterizer.RenderPng(ms.ToArray(), 0, 150);

        int ph = IndexOf(png, "pHYs");
        Assert.True(ph >= 0, "PNG has no pHYs chunk to carry DPI");
        int o = ph + 4;
        long ppx = ((long)png[o] << 24) | ((long)png[o + 1] << 16) | ((long)png[o + 2] << 8) | png[o + 3];
        long ppy = ((long)png[o + 4] << 24) | ((long)png[o + 5] << 16) | ((long)png[o + 6] << 8) | png[o + 7];
        Assert.Equal(ppx, ppy);   // square DPI → WPF keeps the true pixel aspect
    }

    private static int IndexOf(byte[] b, string sig)
    {
        var s = System.Text.Encoding.ASCII.GetBytes(sig);
        for (int i = 0; i < b.Length - s.Length; i++)
        { bool m = true; for (int j = 0; j < s.Length; j++) if (b[i + j] != s[j]) { m = false; break; } if (m) return i; }
        return -1;
    }
}
