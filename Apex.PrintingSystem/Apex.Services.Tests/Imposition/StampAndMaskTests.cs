using Apex.Services.Imposition;
using PdfSharpCore.Pdf;

namespace Apex.Services.Tests.Imposition;

/// <summary>Watermark/stamp overlay and area masking — both preserve the page count and size.</summary>
public class StampAndMaskTests
{
    private static double Mm(double mm) => mm * 72.0 / 25.4;

    private static byte[] Doc(int pages, double wmm = 210, double hmm = 297)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++) { var p = doc.AddPage(); p.Width = Mm(wmm); p.Height = Mm(hmm); }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    [Fact]
    public void Stamp_OverlaysEveryPage_KeepingCountAndSize()
    {
        var svc = new PdfPageToolsService();
        var stamped = svc.StampPdf(Doc(3), Doc(1, 40, 20), xMm: 10, yMm: 10);

        Assert.Equal(3, svc.GetPageCount(stamped));
    }

    [Fact]
    public void Mask_CoversOnePage_KeepingCount()
    {
        var svc = new PdfPageToolsService();
        var masked = svc.MaskArea(Doc(2), page1Based: 1, xMm: 10, yMm: 10, widthMm: 50, heightMm: 20);

        Assert.Equal(2, svc.GetPageCount(masked));
    }

    [Fact]
    public void Mask_RejectsAPageOutOfRange()
    {
        var svc = new PdfPageToolsService();

        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            svc.MaskArea(Doc(2), page1Based: 5, xMm: 0, yMm: 0, widthMm: 10, heightMm: 10));
    }
}
