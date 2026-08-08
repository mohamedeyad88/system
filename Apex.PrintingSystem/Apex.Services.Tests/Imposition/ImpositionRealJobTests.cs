using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;
using Apex.Services.PaperCutting;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Real imposition run: builds an 8-card source PDF (each card a distinct colour +
/// index bar), imposes it 8-up on an A3 press sheet with bleed/gutter/crop marks,
/// and writes the result to D:\Apex\publish\imposition-test for visual inspection.
/// </summary>
public class ImpositionRealJobTests
{
    private const string OutDir = @"D:\Apex\publish\imposition-test";

    [Fact]
    public void EightUp_BusinessCards_ImposedOnA3_WithCropMarks()
    {
        Directory.CreateDirectory(OutDir);
        string srcPath = Path.Combine(OutDir, "source-cards.pdf");
        WriteCardSource(srcPath, 8);

        var input = new ImpositionInput
        {
            Type = ImpositionType.NUp,
            NUp = NUpLayout.EightUp,
            PageWidth = 90, PageHeight = 50,          // business card
            SheetWidth = 297, SheetHeight = 420,      // A3
            SourcePageCount = 8,
            Bleed = 3, SheetMargin = 10, Gutter = 5,
            AllowRotation = true,
        };

        var planner = new ImpositionService(new PaperCuttingOptimizerService());
        ImpositionResult plan = planner.Plan(input);

        Assert.True(plan.IsValid, plan.ErrorMessage);
        Assert.Equal(1, plan.SheetsRequired);                 // 8 cards fit on one A3
        Assert.Equal(8, plan.Sheets[0].Front.Slots.Count);    // all 8 placed

        var marks = new PrintMarksOptions
        {
            CropMarks = true,
            RegistrationMarks = true,
            JobInfo = true,
            JobName = "Apex Business Cards",
            FileName = "source-cards.pdf",
        };

        byte[] pdf = new PdfImpositionEngine().Generate(plan, srcPath, marks: marks);

        string outPath = Path.Combine(OutDir, "imposed-a3.pdf");
        File.WriteAllBytes(outPath, pdf);

        using (var ms = new MemoryStream(pdf))
        using (var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly))
            Assert.Equal(1, doc.PageCount);                   // single imposed sheet

        Assert.True(new FileInfo(outPath).Length > 3000, "imposed PDF suspiciously small");

        // Rasterize the imposed sheet to PNG for visual inspection.
        using (var pdoc = PdfiumViewer.PdfDocument.Load(outPath))
        using (var img = pdoc.Render(0, 150f, 150f, false))
            img.Save(Path.Combine(OutDir, "imposed-a3.png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    [Fact]
    public void StepRepeat_AutoFillsSheet_WithMaxPieces_HighUtilization()
    {
        Directory.CreateDirectory(OutDir);
        string srcPath = Path.Combine(OutDir, "single-card.pdf");
        WriteCardSource(srcPath, 1); // one design, repeated to fill

        var input = new ImpositionInput
        {
            Type = ImpositionType.StepRepeat,
            PageWidth = 90, PageHeight = 50,
            SheetWidth = 420, SheetHeight = 297,   // A3 landscape
            SourcePageCount = 1,
            RequiredCopies = 200,
            Bleed = 2, SheetMargin = 8, Gutter = 3,
            AllowRotation = true,                   // optimizer maximizes fill
        };

        var planner = new ImpositionService(new PaperCuttingOptimizerService());
        ImpositionResult plan = planner.Plan(input);

        Assert.True(plan.IsValid, plan.ErrorMessage);
        int perSheet = plan.Sheets[0].Front.Slots.Count;
        Assert.True(perSheet >= 20, $"only {perSheet} cards/sheet");     // A3 holds many 90×50
        Assert.InRange(plan.UtilizationPercent, 75, 100);                // auto-fill packs tight

        byte[] pdf = new PdfImpositionEngine().Generate(
            plan, srcPath,
            marks: new PrintMarksOptions { CropMarks = true, JobInfo = true, JobName = "Apex Cards — Step & Repeat", FileName = "single-card.pdf" });

        string outPath = Path.Combine(OutDir, "step-repeat-a3.pdf");
        File.WriteAllBytes(outPath, pdf);
        using (var pdoc = PdfiumViewer.PdfDocument.Load(outPath))
        using (var img = pdoc.Render(0, 150f, 150f, false))
            img.Save(Path.Combine(OutDir, "step-repeat-a3.png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    [Fact]
    public void Alignment_MovesGrid_WithinSheet()
    {
        ImpositionInput Make(SheetAlignment a) => new()
        {
            Type = ImpositionType.NUp,
            NUp = NUpLayout.EightUp,
            PageWidth = 90, PageHeight = 50,
            SheetWidth = 297, SheetHeight = 420,   // A3 portrait — grid leaves empty space
            SourcePageCount = 8,
            Bleed = 3, SheetMargin = 10, Gutter = 5,
            AllowRotation = true,
            Alignment = a,
        };

        var planner = new ImpositionService(new PaperCuttingOptimizerService());
        var topLeft = planner.Plan(Make(SheetAlignment.TopLeft));
        var centre = planner.Plan(Make(SheetAlignment.Centre));
        var bottomRight = planner.Plan(Make(SheetAlignment.BottomRight));

        Assert.True(topLeft.IsValid && centre.IsValid && bottomRight.IsValid);

        double tlX = topLeft.Sheets[0].Front.Slots[0].X;
        double ctrX = centre.Sheets[0].Front.Slots[0].X;
        double brX = bottomRight.Sheets[0].Front.Slots[0].X;
        double tlY = topLeft.Sheets[0].Front.Slots[0].Y;
        double brY = bottomRight.Sheets[0].Front.Slots[0].Y;

        // Grid slides left→right and top→bottom as alignment changes.
        Assert.True(tlX < ctrX, $"top-left X {tlX} should be < centre X {ctrX}");
        Assert.True(ctrX < brX, $"centre X {ctrX} should be < bottom-right X {brX}");
        Assert.True(tlY < brY, $"top Y {tlY} should be < bottom Y {brY}");
        Assert.Equal(10, tlX, 1);   // top-left starts exactly at the margin
    }

    [Fact]
    public void ScaleToFit_FitsOversizePages_OntoSmallSheet()
    {
        ImpositionInput Make(bool scale) => new()
        {
            Type = ImpositionType.NUp,
            NUp = NUpLayout.TwoUp,
            PageWidth = 210, PageHeight = 297,     // A4 pages
            SheetWidth = 210, SheetHeight = 297,   // onto an A4 sheet, 2-up → won't fit full size
            SourcePageCount = 4,
            Bleed = 3, SheetMargin = 10, Gutter = 5,
            AllowRotation = true,
            ScaleToFit = scale,
        };

        var planner = new ImpositionService(new PaperCuttingOptimizerService());

        var without = planner.Plan(Make(false));
        Assert.False(without.IsValid);             // too big without scaling

        var with = planner.Plan(Make(true));
        Assert.True(with.IsValid, with.ErrorMessage);
        Assert.Equal(2, with.PagesPerSide);

        // Pages were scaled down to fit: slot smaller than the full placed size (210+6=216 mm).
        double slotW = with.Sheets[0].Front.Slots[0].Width;
        Assert.True(slotW < 216, $"slot width {slotW:0.0} should be < 216 (scaled to fit)");
    }

    [Fact]
    public void RenderFirstSheetPng_ProducesPreviewImage()
    {
        Directory.CreateDirectory(OutDir);
        string srcPath = Path.Combine(OutDir, "preview-source.pdf");
        WriteCardSource(srcPath, 8);

        var input = new ImpositionInput
        {
            Type = ImpositionType.NUp, NUp = NUpLayout.EightUp,
            PageWidth = 90, PageHeight = 50, SheetWidth = 297, SheetHeight = 420,
            SourcePageCount = 8, Bleed = 3, SheetMargin = 10, Gutter = 5, AllowRotation = true,
        };
        var plan = new ImpositionService(new PaperCuttingOptimizerService()).Plan(input);
        Assert.True(plan.IsValid, plan.ErrorMessage);

        byte[] png = new PdfImpositionEngine().RenderSheetPng(
            plan, srcPath, new PrintMarksOptions { CropMarks = true }, sheetIndex: 0, dpi: 110);

        Assert.True(png.Length > 2000, "preview PNG suspiciously small");
        File.WriteAllBytes(Path.Combine(OutDir, "preview-sheet1.png"), png);
    }

    [Fact]
    public void MirrorBacks_MirrorsBackSlots_ForDuplexBooklet()
    {
        ImpositionInput Make(bool mirror) => new()
        {
            Type = ImpositionType.SaddleStitch,
            PageWidth = 148, PageHeight = 210,   // A5 pages
            SheetWidth = 297, SheetHeight = 210, // A4 landscape sheet (2-up booklet)
            SourcePageCount = 8,
            Bleed = 0, SheetMargin = 0, Gutter = 0,
            MirrorBacksHorizontally = mirror,
        };

        var planner = new ImpositionService(new PaperCuttingOptimizerService());
        var normal = planner.Plan(Make(false));
        var mirrored = planner.Plan(Make(true));

        Assert.True(normal.IsValid && mirrored.IsValid);
        Assert.True(normal.IsDuplex, "saddle stitch should be duplex");

        var nBack = normal.Sheets.First(s => s.Back != null).Back!;
        var mBack = mirrored.Sheets.First(s => s.Back != null).Back!;

        bool anyChanged = false;
        for (int i = 0; i < nBack.Slots.Count; i++)
            if (Math.Abs(nBack.Slots[i].X - mBack.Slots[i].X) > 0.1) anyChanged = true;
        Assert.True(anyChanged, "back slots should be mirrored");

        // Mirror formula: X' = sheetW − X − width.
        var s = nBack.Slots[0];
        Assert.Equal(normal.SheetWidthMm - s.X - s.Width, mBack.Slots[0].X, 1);
    }

    [Fact]
    public void AlignSheetsIndependently_ReCentersPartialLastSheet()
    {
        ImpositionInput Make(bool indep) => new()
        {
            Type = ImpositionType.NUp, NUp = NUpLayout.EightUp,
            PageWidth = 90, PageHeight = 50, SheetWidth = 297, SheetHeight = 420,
            SourcePageCount = 10,   // 8 on sheet 1, 2 on sheet 2 (partial)
            Bleed = 3, SheetMargin = 10, Gutter = 5, AllowRotation = true,
            Alignment = SheetAlignment.Centre, AlignSheetsIndependently = indep,
        };

        var planner = new ImpositionService(new PaperCuttingOptimizerService());
        var normal = planner.Plan(Make(false));
        var indep = planner.Plan(Make(true));

        Assert.True(normal.IsValid && indep.IsValid);
        Assert.Equal(2, normal.SheetsRequired);
        Assert.Equal(2, normal.Sheets[1].Front.Slots.Count);   // partial last sheet

        // Full sheet 1 is untouched by the option.
        Assert.Equal(normal.Sheets[0].Front.Slots[0].Y, indep.Sheets[0].Front.Slots[0].Y, 1);
        // Partial sheet 2 is re-centred → its first slot moves.
        Assert.NotEqual(Math.Round(normal.Sheets[1].Front.Slots[0].Y, 1),
                        Math.Round(indep.Sheets[1].Front.Slots[0].Y, 1));
    }

    private static void WriteCardSource(string path, int count)
    {
        var colors = new[]
        {
            XColors.Crimson, XColors.SteelBlue, XColors.SeaGreen, XColors.DarkOrange,
            XColors.MediumPurple, XColors.Teal, XColors.Firebrick, XColors.DarkSlateGray,
        };

        using var doc = new PdfDocument();
        for (int i = 0; i < count; i++)
        {
            PdfPage page = doc.AddPage();
            page.Width = XUnit.FromMillimeter(90);
            page.Height = XUnit.FromMillimeter(50);

            using XGraphics g = XGraphics.FromPdfPage(page);
            double w = page.Width.Point, h = page.Height.Point;

            g.DrawRectangle(new XSolidBrush(colors[i % colors.Length]), 0, 0, w, h);

            // white index bar: (i+1) squares → identifies each card in the imposed sheet
            double sq = XUnit.FromMillimeter(6).Point;
            double gap = XUnit.FromMillimeter(2).Point;
            double x0 = XUnit.FromMillimeter(5).Point;
            double y0 = XUnit.FromMillimeter(34).Point;
            for (int k = 0; k <= i; k++)
                g.DrawRectangle(XBrushes.White, x0 + k * (sq + gap), y0, sq, sq);

            g.DrawRectangle(new XPen(XColors.White, 2), 3, 3, w - 6, h - 6);
        }
        doc.Save(path);
    }
}
