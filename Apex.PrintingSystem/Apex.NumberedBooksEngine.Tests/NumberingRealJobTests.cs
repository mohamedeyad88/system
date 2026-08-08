using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Apex.NumberedBooksEngine.Tests
{
    /// <summary>
    /// Real end-to-end exercise of the numbering engine: builds an A4 ticket
    /// sheet (4 tickets per sheet), then drives the full pipeline to produce an
    /// actual multi-page PDF, verifying that every printed serial is unique and
    /// sequential. Also covers Imposed (cutting) distribution, custom step, and
    /// Arabic-digit / copy rendering. Output lands in
    /// D:\Apex\publish\numbering-test for visual inspection.
    /// </summary>
    public class NumberingRealJobTests
    {
        private const string OutDir = @"D:\Apex\publish\numbering-test";
        private readonly ITestOutputHelper _out;

        public NumberingRealJobTests(ITestOutputHelper output)
        {
            _out = output;
            Directory.CreateDirectory(OutDir);
        }

        [Fact]
        public async Task LinearTicketBook_ProducesRealPdf_WithUniqueSequentialSerials()
        {
            const int slotsPerSheet = 4;
            const long start = 1001, step = 1, total = 200; // 1001..1200 → 50 sheets of 4

            byte[] templatePng = BuildTicketSheetPng(slotsPerSheet, out SKImage templateImage);
            File.WriteAllBytes(Path.Combine(OutDir, "_template.png"), templatePng);

            IReadOnlyList<SlotSpec> slots = BuildSlots(slotsPerSheet);
            string pdfPath = Path.Combine(OutDir, "ticket-book.pdf");

            BookJobOptions Options(Stream? ts) => BaseOptions(
                slots, NumberingMode.Linear, start, step, total, ts, pdfPath);

            // ── Verify the EXACT serials the engine will print ────────────────
            var strategy = NumberingStrategyFactory.Create(Options(null));
            List<long[]> pages = strategy.GeneratePageNumbers(Options(null)).ToList();
            List<long> serials = pages.SelectMany(p => p).Where(n => n > 0).ToList();

            Assert.Equal(50, pages.Count);                               // 200 / 4
            Assert.All(pages, p => Assert.Equal(slotsPerSheet, p.Length));
            Assert.Equal(200, serials.Count);
            Assert.Equal(200, serials.Distinct().Count());              // all unique
            Assert.Equal(1001, serials.Min());
            Assert.Equal(1200, serials.Max());
            Assert.Equal(Enumerable.Range(1001, 200).Select(i => (long)i), serials.OrderBy(n => n));
            Assert.Equal(new long[] { 1001, 1002, 1003, 1004 }, pages[0]); // consecutive on a sheet

            // ── Produce the REAL PDF through the full service ─────────────────
            using (var ts = new MemoryStream(templatePng))
            {
                JobResult result = await new NumberedBooksService()
                    .GenerateNumberedBooksAsync(Options(ts), new Progress<ProgressInfo>(), CancellationToken.None);

                Assert.True(result.Success, string.Join("; ", result.Errors));
                Assert.Equal(50, result.TotalPagesGenerated);
            }
            Assert.True(File.Exists(pdfPath), "PDF was not created");
            Assert.True(new FileInfo(pdfPath).Length > 10_000, "PDF suspiciously small");

            // ── Visual proof: render sheet 1 (serials 1001–1004) ──────────────
            var composer = new Composer();
            var (page1, _) = composer
                .GenerateMultiCopyPages(templateImage, Options(null), new[] { CopyType.Original })
                .First();
            SavePng(page1, Path.Combine(OutDir, "sheet_001.png"));
            page1.Dispose();
            templateImage.Dispose();

            _out.WriteLine($"PDF {new FileInfo(pdfPath).Length / 1024}KB · 50 sheets · serials 1001–1200 unique");
        }

        [Fact]
        public void ImposedCutting_DistributesColumnMajor_ForStacking()
        {
            IReadOnlyList<SlotSpec> slots = BuildSlots(4);
            BookJobOptions opt = BaseOptions(slots, NumberingMode.Imposed, 1001, 1, 200);

            List<long[]> pages = new NumberSequencer().GenerateSequence(opt).ToList();

            // 50 sheets. Cut-and-stack: sheet 0 slots take indices 0,50,100,150.
            Assert.Equal(50, pages.Count);
            Assert.Equal(new long[] { 1001, 1051, 1101, 1151 }, pages[0]);

            List<long> all = pages.SelectMany(p => p).Where(n => n > 0).ToList();
            Assert.Equal(200, all.Distinct().Count());
            Assert.Equal(Enumerable.Range(1001, 200).Select(i => (long)i), all.OrderBy(n => n));
        }

        [Fact]
        public void CustomStep_GeneratesSteppedSerials()
        {
            IReadOnlyList<SlotSpec> slots = BuildSlots(3);
            BookJobOptions opt = BaseOptions(slots, NumberingMode.Linear, 5, 5, 6); // 5,10,…,30

            List<long> all = new NumberSequencer().GenerateSequence(opt)
                .SelectMany(p => p).Where(n => n > 0).OrderBy(n => n).ToList();

            Assert.Equal(new long[] { 5, 10, 15, 20, 25, 30 }, all);
        }

        [Fact]
        public void ArabicDigits_And_CopyStyle_RenderVisually()
        {
            byte[] png = BuildTicketSheetPng(4, out SKImage tpl);
            IReadOnlyList<SlotSpec> slots = BuildSlots(4);
            BookJobOptions opt = BaseOptions(slots, NumberingMode.Linear, 1001, 1, 200);

            // Arabic-Indic digits (٠١٢…) variant
            var arabic = new Composer { UseArabicDigits = true };
            var (aPage, _) = arabic.GenerateMultiCopyPages(tpl, opt, new[] { CopyType.Original }).First();
            SavePng(aPage, Path.Combine(OutDir, "sheet_arabic_digits.png"));
            aPage.Dispose();

            // "صورة" copy variant (styled via slot CopyStyles)
            var western = new Composer();
            var (copyPage, _) = western.GenerateMultiCopyPages(tpl, opt, new[] { CopyType.Copy1 }).First();
            SavePng(copyPage, Path.Combine(OutDir, "sheet_copy.png"));
            copyPage.Dispose();
            tpl.Dispose();

            Assert.True(File.Exists(Path.Combine(OutDir, "sheet_arabic_digits.png")));
            Assert.True(File.Exists(Path.Combine(OutDir, "sheet_copy.png")));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static BookJobOptions BaseOptions(
            IReadOnlyList<SlotSpec> slots, NumberingMode mode,
            long start, long step, long total,
            Stream? templateStream = null, string outputPath = "out.pdf") => new(
                TemplateStream: templateStream,
                TemplatePath: null,
                TemplateFormat: TemplateFormat.Image,
                Layout: LayoutSpec.A4,
                Slots: slots,
                StartNumber: start,
                TotalNumbers: total,
                PagesPerBook: 50,
                CopiesPerPage: 1,
                Mode: mode,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 100,
                OutputMode: "SinglePdf",
                OutputPath: outputPath,
                Step: step);

        private static IReadOnlyList<SlotSpec> BuildSlots(int n)
        {
            var copyStyles = new[]
            {
                new CopyStyle("أصل", "#0B3D5C", 1f),
                new CopyStyle("صورة", "#CC0000", 0.85f),
            };

            var list = new List<SlotSpec>(n);
            for (int i = 0; i < n; i++)
            {
                float y = (float)i / n + 0.05f; // near the top of ticket i
                list.Add(new SlotSpec(
                    Id: $"slot{i + 1}",
                    X: 0.58f, Y: y, Width: 0.34f, Height: 0.10f,
                    FontFamily: "Arial", FontSize: 54,
                    FontColorHex: "#0B3D5C",
                    Align: TextAlign.Center, Rotation: 0,
                    CopyStyles: copyStyles));
            }
            return list;
        }

        /// <summary>Draws an A4 (200 dpi) sheet of <paramref name="n"/> tickets and returns PNG bytes.</summary>
        private static byte[] BuildTicketSheetPng(int n, out SKImage image)
        {
            var info = new SKImageInfo(1654, 2339); // A4 @ ~200 dpi
            using var surface = SKSurface.Create(info);
            SKCanvas c = surface.Canvas;
            c.Clear(SKColors.White);

            using var border = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true, Color = new SKColor(0x0B, 0x3D, 0x5C) };
            using var band = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0xE8, 0xF1, 0xF7) };
            using var title = new SKPaint { IsAntialias = true, TextSize = 40, Color = new SKColor(0x0B, 0x3D, 0x5C), Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) };
            using var label = new SKPaint { IsAntialias = true, TextSize = 44, Color = new SKColor(0x33, 0x33, 0x33), Typeface = SKTypeface.FromFamilyName("Arial") };

            float boxH = info.Height / (float)n;
            for (int i = 0; i < n; i++)
            {
                float top = i * boxH;
                var rect = new SKRect(40, top + 20, info.Width - 40, top + boxH - 20);
                c.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + 66), band);
                c.DrawRect(rect, border);
                c.DrawText("APEX EVENT TICKET", rect.Left + 24, rect.Top + 48, title);
                c.DrawText("No.", info.Width * 0.42f, top + boxH * 0.05f + 130, label);
            }

            image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 95);
            return data.ToArray();
        }

        private static void SavePng(SKImage img, string path)
        {
            using SKData data = img.Encode(SKEncodedImageFormat.Png, 92);
            using FileStream fs = File.Create(path);
            data.SaveTo(fs);
        }
    }
}
