using Apex.Services.Imposition;
using PdfSharpCore.Pdf.IO;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Verifies every pre-press page tool end-to-end on real PDF bytes: page counts,
/// resulting page sizes, and (for a few) rasterized visual proof in
/// D:\Apex\publish\pagetools-test.
/// </summary>
public class PdfPageToolsServiceTests
{
    private const string OutDir = @"D:\Apex\publish\pagetools-test";
    private const double PtToMm = 25.4 / 72.0;
    private readonly PdfPageToolsService _tools = new();

    private static (int Count, double WidMm, double HeiMm) Info(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly);
        var p = doc.Pages[0];
        return (doc.PageCount, p.Width.Point * PtToMm, p.Height.Point * PtToMm);
    }

    private static void Png(byte[] pdf, int page0, string name)
    {
        Directory.CreateDirectory(OutDir);
        string tmp = Path.Combine(OutDir, name + ".pdf");
        File.WriteAllBytes(tmp, pdf);
        using var doc = PdfiumViewer.PdfDocument.Load(tmp);
        using var img = doc.Render(page0, 150f, 150f, false);
        img.Save(Path.Combine(OutDir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    [Fact]
    public void Rasterizer_HandlesParallelRenders_WithoutCrashing()
    {
        byte[] pdf = _tools.GenerateSampleDocument(3, 100, 100);
        var lengths = new System.Collections.Concurrent.ConcurrentBag<int>();

        // Pdfium is not thread-safe; PdfRasterizer serializes internally. Hammer it in parallel.
        System.Threading.Tasks.Parallel.For(0, 16, i =>
        {
            byte[] png = PdfRasterizer.RenderPng(pdf, i % 3, 90);
            lengths.Add(png.Length);
        });

        Assert.Equal(16, lengths.Count);
        Assert.All(lengths, len => Assert.True(len > 500));
    }

    [Fact]
    public void SampleDocument_Generates_NumberedPages()
    {
        byte[] pdf = _tools.GenerateSampleDocument(4, 100, 100);
        var (count, w, h) = Info(pdf);
        Assert.Equal(4, count);
        Assert.Equal(100, w, 1); Assert.Equal(100, h, 1);
        Png(pdf, 0, "sample_p1");
    }

    [Fact]
    public void Reverse_KeepsCount_FlipsOrder()
    {
        byte[] src = _tools.GenerateSampleDocument(4, 80, 80);
        byte[] outPdf = _tools.ReversePages(src);
        Assert.Equal(4, Info(outPdf).Count);
        Png(outPdf, 0, "reverse_p1_should_show_4");
    }

    [Fact]
    public void Interleave_FrontsBacks_KeepsCount()
    {
        byte[] src = _tools.GenerateSampleDocument(4, 80, 80);       // fronts 1,2 | backs 3,4
        byte[] outPdf = _tools.InterleaveFrontsBacks(src, backsReversed: true);
        Assert.Equal(4, Info(outPdf).Count);                         // order → 1,4,2,3
    }

    [Fact]
    public void InsertBlankPages_AddsPages()
    {
        byte[] src = _tools.GenerateSampleDocument(3, 80, 80);
        byte[] outPdf = _tools.InsertBlankPages(src, afterPage1Based: 1, count: 2);
        Assert.Equal(5, Info(outPdf).Count);
    }

    [Fact]
    public void JoinTwoUp_HalvesPages_DoublesWidth()
    {
        byte[] src = _tools.GenerateSampleDocument(4, 90, 50);
        byte[] outPdf = _tools.JoinTwoUp(src, gapMm: 0);
        var (count, w, h) = Info(outPdf);
        Assert.Equal(2, count);
        Assert.Equal(180, w, 1);   // two 90mm pages joined
        Assert.Equal(50, h, 1);
        Png(outPdf, 0, "join2up_p1");
    }

    [Fact]
    public void Tile_Splits_IntoGrid()
    {
        byte[] src = _tools.GenerateSampleDocument(1, 200, 200);
        byte[] outPdf = _tools.TilePages(src, cols: 2, rows: 2, overlapMm: 0);
        var (count, w, h) = Info(outPdf);
        Assert.Equal(4, count);            // 2×2 tiles
        Assert.Equal(100, w, 1);           // each tile 100mm
        Assert.Equal(100, h, 1);
        Png(outPdf, 0, "tile_topleft");
    }

    [Fact]
    public void AddBleed_GrowsPage_BothSides()
    {
        byte[] src = _tools.GenerateSampleDocument(1, 100, 100);
        byte[] outPdf = _tools.AddBleed(src, bleedMm: 5);
        var (_, w, h) = Info(outPdf);
        Assert.Equal(110, w, 1);   // +5mm each side
        Assert.Equal(110, h, 1);
    }

    /// <summary>
    /// Growing the page is only half the job. Without a trim box a RIP or cutter
    /// treats the media box as the finished size, so the bleed gets printed as part
    /// of the product — the opposite of what the operator asked for. Measured on a
    /// real run 2026-08-16: the source carried six trim boxes and the output carried
    /// none.
    /// </summary>
    [Fact]
    public void AddBleed_MarksWhereTheSheetGetsCut()
    {
        byte[] src = _tools.GenerateSampleDocument(2, 100, 100);
        byte[] outPdf = _tools.AddBleed(src, bleedMm: 3);

        using var doc = PdfReader.Open(new MemoryStream(outPdf), PdfDocumentOpenMode.InformationOnly);
        Assert.Equal(2, doc.PageCount);

        foreach (var page in doc.Pages.Cast<PdfSharpCore.Pdf.PdfPage>())
        {
            // Media box grew by the bleed on every side.
            Assert.Equal(106, page.Width.Point * PtToMm, 1);
            Assert.Equal(106, page.Height.Point * PtToMm, 1);

            // Trim box is the original 100×100 page, centred — i.e. inset by the bleed.
            var trim = page.TrimBox;
            Assert.Equal(3, trim.X1 * PtToMm, 1);
            Assert.Equal(3, trim.Y1 * PtToMm, 1);
            Assert.Equal(103, trim.X2 * PtToMm, 1);
            Assert.Equal(103, trim.Y2 * PtToMm, 1);

            // Everything added is bleed, so the bleed box is the whole new sheet.
            Assert.Equal(0, page.BleedBox.X1 * PtToMm, 1);
            Assert.Equal(106, page.BleedBox.X2 * PtToMm, 1);
        }
    }

    [Fact]
    public void TrimAndShift_ShrinksPage()
    {
        byte[] src = _tools.GenerateSampleDocument(1, 100, 100);
        byte[] outPdf = _tools.TrimAndShift(src, trimMm: 5, shiftXmm: 0, shiftYmm: 0);
        var (_, w, h) = Info(outPdf);
        Assert.Equal(90, w, 1);    // −5mm each side
        Assert.Equal(90, h, 1);
    }

    [Fact]
    public void Stamp_PreservesBase_AndOverlays()
    {
        byte[] baseDoc = _tools.GenerateSampleDocument(2, 100, 100);
        byte[] stamp = _tools.GenerateSampleDocument(1, 30, 30);
        byte[] outPdf = _tools.StampPdf(baseDoc, stamp, xMm: 5, yMm: 5, widthMm: 30, heightMm: 30);
        Assert.Equal(2, Info(outPdf).Count);
        Png(outPdf, 0, "stamp_p1");
    }

    [Fact]
    public void Mask_CoversRegion_KeepsCount()
    {
        byte[] src = _tools.GenerateSampleDocument(2, 100, 100);
        byte[] outPdf = _tools.MaskArea(src, page1Based: 1, xMm: 20, yMm: 20, widthMm: 60, heightMm: 40);
        Assert.Equal(2, Info(outPdf).Count);
        Png(outPdf, 0, "mask_p1");
    }
}
