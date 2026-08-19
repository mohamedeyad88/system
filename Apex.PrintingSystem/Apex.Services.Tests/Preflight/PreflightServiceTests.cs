using Apex.Services.Preflight;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Apex.Services.Tests.Preflight;

/// <summary>
/// Preflight is the "check the customer's file before you waste paper" feature. These
/// pin the reliable, metadata-driven checks: page count, size consistency, bleed, tiny
/// pages. (The image RGB/resolution scan is best-effort and covered by not throwing.)
/// </summary>
public class PreflightServiceTests
{
    private static double Mm(double mm) => mm * 72.0 / 25.4;

    private static byte[] Pdf(params (double w, double h)[] pages)
    {
        using var doc = new PdfDocument();
        foreach (var (w, h) in pages) { var p = doc.AddPage(); p.Width = w; p.Height = h; }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    [Fact]
    public void CountsPagesAndAcceptsAConsistentDocument()
    {
        var report = new PreflightService().Check(Pdf((Mm(210), Mm(297)), (Mm(210), Mm(297))));

        Assert.Equal(2, report.PageCount);
        Assert.DoesNotContain(report.Findings, f => f.Check == PreflightCheck.InconsistentSizes);
        Assert.True(report.Passed);   // no errors
    }

    [Fact]
    public void MixedPageSizesRaiseAWarning()
    {
        var report = new PreflightService().Check(Pdf((Mm(210), Mm(297)), (Mm(297), Mm(420))));

        Assert.Contains(report.Findings, f =>
            f.Check == PreflightCheck.InconsistentSizes && f.Severity == PreflightSeverity.Warning);
    }

    [Fact]
    public void APageWithNoBleedBoxIsFlagged()
    {
        var report = new PreflightService().Check(Pdf((Mm(210), Mm(297))));

        Assert.Contains(report.Findings, f => f.Check == PreflightCheck.NoBleed);
    }

    [Fact]
    public void ABleedBoxLargerThanTrimIsNotFlagged()
    {
        using var doc = new PdfDocument();
        var p = doc.AddPage();
        p.Width = Mm(216); p.Height = Mm(303);          // A4 trim + 3 mm bleed all round
        p.TrimBox = new PdfRectangle(new XPoint(Mm(3), Mm(3)), new XPoint(Mm(213), Mm(300)));
        p.BleedBox = new PdfRectangle(new XPoint(0, 0), new XPoint(Mm(216), Mm(303)));
        using var ms = new MemoryStream();
        doc.Save(ms, false);

        var report = new PreflightService().Check(ms.ToArray());

        Assert.DoesNotContain(report.Findings, f => f.Check == PreflightCheck.NoBleed);
    }

    [Fact]
    public void AZeroSizedPageIsAnError()
    {
        var report = new PreflightService().Check(Pdf((Mm(2), Mm(2))));

        Assert.Contains(report.Findings, f =>
            f.Check == PreflightCheck.TinyPage && f.Severity == PreflightSeverity.Error);
        Assert.False(report.Passed);
    }

    [Fact]
    public void GarbageBytesReportAsUnreadableRatherThanThrow()
    {
        var report = new PreflightService().Check(new byte[] { 1, 2, 3, 4, 5 });

        Assert.Contains(report.Findings, f => f.Check == PreflightCheck.Encrypted);
        Assert.False(report.Passed);
    }
}
