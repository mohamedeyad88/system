using Apex.Services.Imposition;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Apex.Services.Tests.Imposition;

/// <summary>Rotate / extract / delete over a 1-based page range (to = 0 means last page).</summary>
public class PageRangeTests
{
    private static byte[] Doc(int pages)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++) { var p = doc.AddPage(); p.Width = 595; p.Height = 842; }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private readonly PdfPageToolsService _svc = new();

    [Fact]
    public void Extract_KeepsOnlyTheRange()
    {
        var result = _svc.ExtractPages(Doc(5), fromPage: 2, toPage: 3);
        Assert.Equal(2, _svc.GetPageCount(result));
    }

    [Fact]
    public void Delete_RemovesTheRange()
    {
        var result = _svc.DeletePages(Doc(5), fromPage: 2, toPage: 3);
        Assert.Equal(3, _svc.GetPageCount(result));
    }

    [Fact]
    public void Delete_CannotRemoveEveryPage()
    {
        Assert.Throws<System.InvalidOperationException>(() => _svc.DeletePages(Doc(3), 1, 0));
    }

    [Fact]
    public void Rotate_TurnsTheGivenPageOnly()
    {
        var result = _svc.RotatePages(Doc(3), degrees: 90, fromPage: 1, toPage: 1);

        using var doc = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.InformationOnly);
        Assert.Equal(90, doc.Pages[0].Rotate);
        Assert.Equal(0, doc.Pages[1].Rotate);
    }

    [Fact]
    public void Rotate_RejectsNonRightAngles()
    {
        Assert.Throws<System.ArgumentException>(() => _svc.RotatePages(Doc(1), 45));
    }
}
