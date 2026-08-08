using Apex.Services.Imposition;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Unit tests for <see cref="PdfOperationsService"/> — merge/split/rotate/delete/reorder.
/// Source PDFs are built in-memory (blank pages) to avoid font-resolver dependencies.
/// </summary>
public class PdfOperationsServiceTests
{
    private readonly PdfOperationsService _svc = new();

    /// <summary>Creates an in-memory PDF with <paramref name="pages"/> blank A4 pages.</summary>
    internal static byte[] MakePdf(int pages)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++)
        {
            var p = doc.AddPage();
            p.Width = 595;   // A4 pt
            p.Height = 842;
        }
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static int PageCountOf(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }

    [Fact]
    public void GetPageCount_ReturnsCorrectCount()
    {
        Assert.Equal(5, _svc.GetPageCount(MakePdf(5)));
    }

    [Fact]
    public void Merge_ConcatenatesPageCounts()
    {
        var merged = _svc.Merge(new[] { MakePdf(2), MakePdf(3) });
        Assert.Equal(5, PageCountOf(merged));
    }

    [Fact]
    public void SplitToPages_ProducesOneDocPerPage()
    {
        var parts = _svc.SplitToPages(MakePdf(4));
        Assert.Equal(4, parts.Count);
        Assert.All(parts, p => Assert.Equal(1, PageCountOf(p)));
    }

    [Fact]
    public void ExtractPages_KeepsOnlyRequested()
    {
        var extracted = _svc.ExtractPages(MakePdf(4), new[] { 1, 3 });
        Assert.Equal(2, PageCountOf(extracted));
    }

    [Fact]
    public void DeletePages_RemovesRequested()
    {
        var result = _svc.DeletePages(MakePdf(4), new[] { 2 });
        Assert.Equal(3, PageCountOf(result));
    }

    [Fact]
    public void DeletePages_AllPages_Throws()
    {
        Assert.ThrowsAny<System.Exception>(
            () => _svc.DeletePages(MakePdf(2), new[] { 1, 2 }));
    }

    [Fact]
    public void ReorderPages_ValidPermutation_PreservesCount()
    {
        var result = _svc.ReorderPages(MakePdf(4), new[] { 4, 3, 2, 1 });
        Assert.Equal(4, PageCountOf(result));
    }

    [Fact]
    public void ReorderPages_InvalidPermutation_Throws()
    {
        Assert.Throws<System.ArgumentException>(
            () => _svc.ReorderPages(MakePdf(4), new[] { 1, 2, 3 }));   // missing page 4
    }

    [Fact]
    public void RotatePages_SetsRotationOnTargetPage()
    {
        var result = _svc.RotatePages(MakePdf(3), new[] { 1 }, 90);

        using var ms = new MemoryStream(result);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        Assert.Equal(90, doc.Pages[0].Rotate);
        Assert.Equal(0, doc.Pages[1].Rotate);
    }

    [Fact]
    public void RotatePages_NonQuarterAngle_Throws()
    {
        Assert.Throws<System.ArgumentException>(
            () => _svc.RotatePages(MakePdf(2), new[] { 1 }, 45));
    }

    [Fact]
    public void ResizePages_SetsTargetPageSize()
    {
        // Source A4 (595×842 pt) → resize to A3 (297×420 mm).
        var resized = _svc.ResizePages(MakePdf(3), 297, 420, maintainAspect: true);

        using var ms = new MemoryStream(resized);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        Assert.Equal(3, doc.PageCount);

        // A3 width = 297mm ≈ 841.9 pt; allow a small tolerance.
        double expectedW = 297 * 72.0 / 25.4;
        Assert.InRange(doc.Pages[0].Width.Point, expectedW - 1, expectedW + 1);
    }

    [Fact]
    public void ResizePages_InvalidTarget_Throws()
    {
        Assert.Throws<System.ArgumentException>(
            () => _svc.ResizePages(MakePdf(1), 0, 100));
    }
}
