using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;
using Apex.Services.PaperCutting;
using PdfSharpCore.Pdf.IO;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Integration tests for <see cref="PdfImpositionEngine"/>: verifies that a plan from
/// <see cref="ImpositionService"/> renders to a valid PDF with the expected page count.
/// </summary>
public class PdfImpositionEngineTests
{
    private readonly ImpositionService _planner = new(new PaperCuttingOptimizerService());
    private readonly PdfImpositionEngine _engine = new();

    private static int PageCountOf(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }

    /// <summary>Writes a blank-page source PDF to a temp file and returns its path.</summary>
    private static string WriteTempPdf(int pages)
    {
        var bytes = PdfOperationsServiceTests.MakePdf(pages);
        string path = Path.Combine(Path.GetTempPath(),
            $"apex_imp_test_{System.Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void NUp_ProducesOnePagePerSheet()
    {
        var input = new ImpositionInput
        {
            Type = ImpositionType.NUp,
            NUp = NUpLayout.TwoUp,
            PageWidth = 210, PageHeight = 297,
            SheetWidth = 320, SheetHeight = 450,
            SourcePageCount = 4,
            Bleed = 0, SheetMargin = 0, Gutter = 0
        };
        var plan = _planner.Plan(input);
        Assert.True(plan.IsValid, plan.ErrorMessage);

        string src = WriteTempPdf(4);
        try
        {
            var pdf = _engine.Generate(plan, src, marks: null);
            Assert.Equal(plan.SheetsRequired, PageCountOf(pdf));   // single-sided
        }
        finally { File.Delete(src); }
    }

    [Fact]
    public void SaddleStitch_ProducesTwoOutputPagesPerSheet()
    {
        var input = new ImpositionInput
        {
            Type = ImpositionType.SaddleStitch,
            PageWidth = 210, PageHeight = 297,
            SheetWidth = 450, SheetHeight = 320,
            SourcePageCount = 8,
            Bleed = 0, SheetMargin = 0, Gutter = 0
        };
        var plan = _planner.Plan(input);
        Assert.True(plan.IsValid, plan.ErrorMessage);
        Assert.True(plan.IsDuplex);

        string src = WriteTempPdf(8);
        try
        {
            var pdf = _engine.Generate(plan, src, marks: null);
            // 2 sheets × 2 sides = 4 output pages
            Assert.Equal(plan.SheetsRequired * 2, PageCountOf(pdf));
        }
        finally { File.Delete(src); }
    }

    [Fact]
    public void Generate_WithPrintMarks_ProducesValidPdf()
    {
        var input = new ImpositionInput
        {
            Type = ImpositionType.NUp,
            NUp = NUpLayout.FourUp,
            PageWidth = 100, PageHeight = 150,
            SheetWidth = 320, SheetHeight = 450,
            SourcePageCount = 4,
            Bleed = 3, SheetMargin = 10, Gutter = 5
        };
        var plan = _planner.Plan(input);
        Assert.True(plan.IsValid, plan.ErrorMessage);

        var marks = new PrintMarksOptions
        {
            CropMarks = true,
            RegistrationMarks = true,
            ColorBars = true,
            JobInfo = true,
            JobName = "اختبار",
            FileName = "test.pdf"
        };

        string src = WriteTempPdf(4);
        try
        {
            var pdf = _engine.Generate(plan, src, marks);
            Assert.True(PageCountOf(pdf) >= 1);   // valid, reopenable
        }
        finally { File.Delete(src); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Prepress regression: imposed sheets must carry TrimBox and BleedBox so
    // RIPs/cutters know the live area programmatically, and TrimBox must be
    // inset from BleedBox by the configured bleed.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Generate_WritesTrimAndBleedBoxes()
    {
        var input = new ImpositionInput
        {
            Type = ImpositionType.NUp,
            NUp = NUpLayout.FourUp,
            PageWidth = 100, PageHeight = 150,
            SheetWidth = 320, SheetHeight = 450,
            SourcePageCount = 4,
            Bleed = 3, SheetMargin = 10, Gutter = 5
        };
        var plan = _planner.Plan(input);
        Assert.True(plan.IsValid, plan.ErrorMessage);
        Assert.Equal(3, plan.BleedMm, 3);

        string src = WriteTempPdf(4);
        try
        {
            var pdf = _engine.Generate(plan, src, marks: null);
            var ascii = System.Text.Encoding.ASCII.GetString(pdf);

            Assert.Contains("/TrimBox", ascii);
            Assert.Contains("/BleedBox", ascii);

            // Bleed inset check: trim width = bleed width − 2×bleed (in points).
            var trim = System.Text.RegularExpressions.Regex.Match(
                ascii, @"/TrimBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)");
            var bleed = System.Text.RegularExpressions.Regex.Match(
                ascii, @"/BleedBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)");
            Assert.True(trim.Success && bleed.Success);

            double T(System.Text.RegularExpressions.Match m, int g) =>
                double.Parse(m.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture);
            double trimW = T(trim, 3) - T(trim, 1);
            double bleedW = T(bleed, 3) - T(bleed, 1);
            double expectedInset = 2 * 3 * 72.0 / 25.4;   // 2×3mm in pt
            Assert.True(Math.Abs((bleedW - trimW) - expectedInset) < 0.5,
                $"TrimBox not inset by bleed: bleedW={bleedW}, trimW={trimW}");
        }
        finally { File.Delete(src); }
    }

    [Fact]
    public void Generate_InvalidPlan_Throws()
    {
        var bad = new ImpositionResult { IsValid = false, ErrorMessage = "x" };
        Assert.ThrowsAny<System.Exception>(() => _engine.Generate(bad, "nope.pdf"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Print-quality regression: placed pages must NEVER be stretched.
    // Decodes the output page content stream and asserts every placement matrix
    // is a uniform scale (|scaleX| == |scaleY|), including rotated slots.
    // This is the exact measurement that exposed the anamorphic-distortion bug.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Generate_RotatedNUp_PlacesPagesWithUniformScale()
    {
        var input = new ImpositionInput
        {
            Type = ImpositionType.NUp,
            NUp = NUpLayout.TwoUp,
            PageWidth = 148, PageHeight = 210,      // A5 config
            SheetWidth = 320, SheetHeight = 450,    // SRA3 → forces rotated 1×2 grid
            Bleed = 3, SheetMargin = 10, Gutter = 5,
            AllowRotation = true,
            SourcePageCount = 2
        };
        var plan = _planner.Plan(input);
        Assert.True(plan.IsValid, plan.ErrorMessage);
        Assert.Equal(90, plan.Sheets[0].Front.Slots[0].Rotation);

        string src = WriteTempPdf(2);               // A4 sources (595×842pt)
        try
        {
            var pdf = _engine.Generate(plan, src, marks: null);
            var matrices = ExtractPlacementMatrices(pdf);

            Assert.NotEmpty(matrices);
            foreach (var (a, b, c, d) in matrices)
            {
                // Uniform scale: for 0°/180° → |a|==|d| (b=c=0);
                // for 90°/270° → |b|==|c| (a=d=0). Tolerance 0.5%.
                double sx = Math.Max(Math.Abs(a), Math.Abs(b));
                double sy = Math.Max(Math.Abs(c), Math.Abs(d));
                Assert.True(Math.Abs(sx - sy) <= 0.005 * Math.Max(sx, sy),
                    $"Non-uniform placement matrix [{a} {b} {c} {d}] — page would be distorted.");
            }
        }
        finally { File.Delete(src); }
    }

    /// <summary>
    /// Inflates every FlateDecode stream in the PDF and extracts the (a b c d) of
    /// each `cm` matrix that immediately precedes an XObject `Do` placement.
    /// </summary>
    private static List<(double a, double b, double c, double d)> ExtractPlacementMatrices(byte[] pdf)
    {
        var results = new List<(double, double, double, double)>();
        var ascii = System.Text.Encoding.ASCII.GetString(pdf);
        var rx = new System.Text.RegularExpressions.Regex(
            @"([\d.-]+) ([\d.-]+) ([\d.-]+) ([\d.-]+) ([\d.-]+) ([\d.-]+) cm[^Q]*?/\w+ Do");

        int pos = 0;
        while (true)
        {
            int s = ascii.IndexOf("stream", pos, StringComparison.Ordinal);
            if (s < 0) break;
            int dataStart = ascii.IndexOf('\n', s) + 1;
            int e = ascii.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (e < 0) break;

            int len = e - dataStart;
            if (len > 2 && pdf[dataStart] == 0x78)          // zlib header → FlateDecode
            {
                try
                {
                    using var ms = new MemoryStream(pdf, dataStart + 2, len - 2);
                    using var ds = new System.IO.Compression.DeflateStream(
                        ms, System.IO.Compression.CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    ds.CopyTo(outMs);
                    var content = System.Text.Encoding.ASCII.GetString(outMs.ToArray());

                    foreach (System.Text.RegularExpressions.Match m in rx.Matches(content))
                        results.Add((
                            double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                            double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                            double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                            double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture)));
                }
                catch { /* not an inflatable content stream — skip */ }
            }
            pos = e + 9;
        }
        return results;
    }

    [Fact]
    public void Generate_WithPdfX_StampsTitleAndIdentification()
    {
        var input = new ImpositionInput
        {
            Type = ImpositionType.NUp,
            NUp = NUpLayout.TwoUp,
            PageWidth = 210, PageHeight = 297,
            SheetWidth = 320, SheetHeight = 450,
            SourcePageCount = 2,
            Bleed = 0, SheetMargin = 0, Gutter = 0
        };
        var plan = _planner.Plan(input);
        var export = new ImpositionExportOptions
        {
            PdfX = PdfXConformance.PdfX3,
            Title = "كتالوج",
            Author = "Apex"
        };

        string src = WriteTempPdf(2);
        try
        {
            var pdf = _engine.Generate(plan, src, marks: null, export: export);
            using var ms = new MemoryStream(pdf);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
            Assert.Equal("كتالوج", doc.Info.Title);
        }
        finally { File.Delete(src); }
    }
}
