using Apex.Services.Imposition;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Step-and-repeat is the cards / stickers "many-up": one small design tiled across a
/// press sheet with gutters and crop marks. These pin the sheet output and the loud
/// failure when the grid cannot fit.
/// </summary>
public class StepAndRepeatTests
{
    private static double Mm(double mm) => mm * 72.0 / 25.4;

    private static byte[] Card(double wmm, double hmm)
    {
        using var doc = new PdfDocument();
        var p = doc.AddPage();
        p.Width = Mm(wmm); p.Height = Mm(hmm);
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    [Fact]
    public void ProducesOneSheetOfTheRequestedSize()
    {
        var svc = new PdfPageToolsService();

        // 8 business cards (2×4) on an A3 sheet.
        var sheet = svc.StepAndRepeat(Card(90, 50), 297, 420, cols: 2, rows: 4, gutterMm: 3, cropMarks: true);

        using var doc = PdfReader.Open(new MemoryStream(sheet), PdfDocumentOpenMode.InformationOnly);
        Assert.Equal(1, doc.PageCount);
        Assert.Equal(297, System.Math.Round(doc.Pages[0].Width.Millimeter));
        Assert.Equal(420, System.Math.Round(doc.Pages[0].Height.Millimeter));
    }

    [Fact]
    public void AGridThatCannotFitFailsLoudly()
    {
        var svc = new PdfPageToolsService();

        // 3×3 of a 200 mm card cannot fit an A4 sheet.
        Assert.Throws<System.InvalidOperationException>(() =>
            svc.StepAndRepeat(Card(200, 200), 210, 297, cols: 3, rows: 3, gutterMm: 0, cropMarks: false));
    }
}
