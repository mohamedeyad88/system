using Apex.Services.Imposition;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Apex.Services.Tests.Imposition;

/// <summary>Printer's marks (adds a margin around the trim) and split-into-files.</summary>
public class PrinterMarksAndSplitTests
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

    /// <summary>A page whose visible area was cropped (smaller /CropBox over the /MediaBox),
    /// as a pre-cropped customer file arrives.</summary>
    private static byte[] CroppedDoc(double wmm = 210, double hmm = 297, double insetMm = 20)
    {
        using var doc = new PdfDocument();
        var p = doc.AddPage();
        p.Width = Mm(wmm); p.Height = Mm(hmm);
        p.CropBox = new PdfRectangle(
            new XPoint(Mm(insetMm), Mm(insetMm)),
            new XPoint(Mm(wmm - insetMm), Mm(hmm - insetMm)));
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private readonly PdfPageToolsService _svc = new();

    [Fact]
    public void PrinterMarks_RespectsCropBox_DoesNotRestoreTrimmedArea()
    {
        // 210×297 cropped 20mm on every side → visible 170×257. With marks (10mm margin)
        // the sheet must be 190×277 (crop + 2×10), NOT 230×317 — proving the trimmed-away
        // part is not resurrected when the page is re-embedded as a form.
        var marked = _svc.AddPrinterMarks(CroppedDoc(210, 297, 20), marginMm: 10);

        using var doc = PdfReader.Open(new MemoryStream(marked), PdfDocumentOpenMode.InformationOnly);
        Assert.Equal(190, System.Math.Round(doc.Pages[0].Width.Millimeter));
        Assert.Equal(277, System.Math.Round(doc.Pages[0].Height.Millimeter));
    }

    [Fact]
    public void PrinterMarks_EnlargeEachPageByTheMargin_KeepingCount()
    {
        var marked = _svc.AddPrinterMarks(Doc(2), marginMm: 10);

        using var doc = PdfReader.Open(new MemoryStream(marked), PdfDocumentOpenMode.InformationOnly);
        Assert.Equal(2, doc.PageCount);
        Assert.Equal(230, System.Math.Round(doc.Pages[0].Width.Millimeter));   // 210 + 2×10
        Assert.Equal(317, System.Math.Round(doc.Pages[0].Height.Millimeter));  // 297 + 2×10
    }

    [Theory]
    [InlineData(5, 2, 3)]   // 2 + 2 + 1
    [InlineData(4, 2, 2)]
    [InlineData(3, 1, 3)]
    [InlineData(1, 5, 1)]
    public void Split_ProducesTheRightNumberOfParts(int pages, int perFile, int expectedParts)
    {
        var parts = _svc.SplitEvery(Doc(pages), perFile);
        Assert.Equal(expectedParts, parts.Count);
    }

    [Fact]
    public void Split_RejectsZeroPerFile()
    {
        Assert.Throws<System.ArgumentException>(() => _svc.SplitEvery(Doc(3), 0));
    }
}
