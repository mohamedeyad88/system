using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Apex.Services.Imposition
{
    /// <summary>
    /// Pre-press page tools (the "page management" / "stick on" toolbox): reverse,
    /// interleave duplex scans, insert blanks, join-2-up, tile/poster split, add bleed,
    /// trim &amp; shift, sample document, stamp overlay, and mask ("masking tape").
    /// Byte-array in/out (testable), PdfSharpCore, 1-based page indices.
    /// </summary>
    public class PdfPageToolsService
    {
        private const double MmToPt = 72.0 / 25.4;

        // ── Reverse ───────────────────────────────────────────────────────────────
        /// <summary>Returns the PDF with pages in reverse order.</summary>
        public byte[] ReversePages(byte[] pdf)
        {
            using var src = OpenImport(pdf);
            using var output = new PdfDocument();
            for (int i = src.PageCount; i >= 1; i--)
                output.AddPage(src.Pages[i - 1]);
            return Save(output);
        }

        // ── Interleave fronts/backs (shuffle even/odd — duplex scan fix) ────────────
        /// <summary>
        /// Combines a document whose first half is the fronts and second half the backs
        /// (as produced by scanning one side then flipping the stack) into correct
        /// 1,2,3,… order. When <paramref name="backsReversed"/> is true the backs half is
        /// taken in reverse (the usual result of flipping a stack).
        /// </summary>
        public byte[] InterleaveFrontsBacks(byte[] pdf, bool backsReversed = true)
        {
            using var src = OpenImport(pdf);
            int n = src.PageCount;
            if (n < 2) throw new InvalidOperationException("يلزم صفحتان على الأقل للدمج.");

            int half = (n + 1) / 2;
            var fronts = Enumerable.Range(1, half).ToList();
            var backs = Enumerable.Range(half + 1, n - half).ToList();
            if (backsReversed) backs.Reverse();

            using var output = new PdfDocument();
            for (int i = 0; i < half; i++)
            {
                output.AddPage(src.Pages[fronts[i] - 1]);
                if (i < backs.Count) output.AddPage(src.Pages[backs[i] - 1]);
            }
            return Save(output);
        }

        // ── Insert blank pages ──────────────────────────────────────────────────────
        /// <summary>
        /// Inserts <paramref name="count"/> blank pages after page <paramref name="afterPage1Based"/>
        /// (0 = before the first page). Blank size defaults to the first page's size.
        /// </summary>
        public byte[] InsertBlankPages(byte[] pdf, int afterPage1Based, int count,
            double? widthMm = null, double? heightMm = null)
        {
            if (count < 1) throw new ArgumentException("عدد الصفحات يجب أن يكون 1 أو أكثر.", nameof(count));
            using var src = OpenImport(pdf);
            if (afterPage1Based < 0 || afterPage1Based > src.PageCount)
                throw new ArgumentOutOfRangeException(nameof(afterPage1Based));

            double w = (widthMm ?? src.Pages[0].Width.Point / MmToPt) * MmToPt;
            double h = (heightMm ?? src.Pages[0].Height.Point / MmToPt) * MmToPt;

            using var output = new PdfDocument();
            void AddBlanks() { for (int b = 0; b < count; b++) { var p = output.AddPage(); p.Width = w; p.Height = h; } }

            if (afterPage1Based == 0) AddBlanks();
            for (int i = 1; i <= src.PageCount; i++)
            {
                output.AddPage(src.Pages[i - 1]);
                if (i == afterPage1Based) AddBlanks();
            }
            return Save(output);
        }

        // ── Join 2 pages side-by-side ───────────────────────────────────────────────
        /// <summary>Places each consecutive pair of pages side-by-side on one landscape sheet.</summary>
        public byte[] JoinTwoUp(byte[] pdf, double gapMm = 0)
        {
            int count = GetPageCount(pdf);
            if (count == 0) throw new InvalidOperationException("لا توجد صفحات.");
            double gap = gapMm * MmToPt;

            return WithStagedForm(pdf, (tmp, output) =>
            {
                for (int i = 1; i <= count; i += 2)
                {
                    using var left = XPdfForm.FromFile(tmp); left.PageNumber = i;
                    double sw = left.PointWidth, sh = left.PointHeight;

                    var page = output.AddPage();
                    bool hasRight = i + 1 <= count;
                    page.Width = hasRight ? sw * 2 + gap : sw;
                    page.Height = sh;

                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawImage(left, 0, 0, sw, sh);
                    if (hasRight)
                    {
                        using var right = XPdfForm.FromFile(tmp); right.PageNumber = i + 1;
                        gfx.DrawImage(right, sw + gap, 0, right.PointWidth, right.PointHeight);
                    }
                }
            });
        }

        // ── Tile pages (poster split) ───────────────────────────────────────────────
        /// <summary>
        /// Splits each page into a <paramref name="cols"/>×<paramref name="rows"/> grid of
        /// tiles (poster tiling), with an optional <paramref name="overlapMm"/> for gluing.
        /// Produces cols×rows output pages per source page (row-major, top-left first).
        /// </summary>
        public byte[] TilePages(byte[] pdf, int cols, int rows, double overlapMm = 0)
        {
            if (cols < 1 || rows < 1) throw new ArgumentException("الأعمدة والصفوف يجب أن تكون 1 أو أكثر.");
            int count = GetPageCount(pdf);
            if (count == 0) throw new InvalidOperationException("لا توجد صفحات.");
            double overlap = overlapMm * MmToPt;

            return WithStagedForm(pdf, (tmp, output) =>
            {
                for (int i = 1; i <= count; i++)
                {
                    using var probe = XPdfForm.FromFile(tmp); probe.PageNumber = i;
                    double sw = probe.PointWidth, sh = probe.PointHeight;
                    double tileW = sw / cols, tileH = sh / rows;

                    for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        var page = output.AddPage();
                        page.Width = tileW + overlap;
                        page.Height = tileH + overlap;

                        using var gfx = XGraphics.FromPdfPage(page);
                        gfx.IntersectClip(new XRect(0, 0, page.Width.Point, page.Height.Point));
                        using var form = XPdfForm.FromFile(tmp); form.PageNumber = i;
                        // Shift the whole page so this tile's top-left lands at the origin.
                        gfx.DrawImage(form, -c * tileW, -r * tileH, sw, sh);
                    }
                }
            });
        }

        // ── Add bleed (extend canvas) ───────────────────────────────────────────────
        /// <summary>Extends every page by <paramref name="bleedMm"/> on all sides, content centred.</summary>
        public byte[] AddBleed(byte[] pdf, double bleedMm)
        {
            if (bleedMm <= 0) throw new ArgumentException("قيمة النزيف يجب أن تكون أكبر من صفر.");
            int count = GetPageCount(pdf);
            double bleed = bleedMm * MmToPt;

            return WithStagedForm(pdf, (tmp, output) =>
            {
                for (int i = 1; i <= count; i++)
                {
                    using var form = XPdfForm.FromFile(tmp); form.PageNumber = i;
                    double sw = form.PointWidth, sh = form.PointHeight;
                    var page = output.AddPage();
                    page.Width = sw + bleed * 2;
                    page.Height = sh + bleed * 2;
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawImage(form, bleed, bleed, sw, sh);
                }
            });
        }

        // ── Trim & shift ────────────────────────────────────────────────────────────
        /// <summary>
        /// Trims <paramref name="trimMm"/> off every side and shifts content by
        /// (<paramref name="shiftXmm"/>, <paramref name="shiftYmm"/>). Output page = source − 2×trim.
        /// </summary>
        public byte[] TrimAndShift(byte[] pdf, double trimMm, double shiftXmm, double shiftYmm)
        {
            int count = GetPageCount(pdf);
            double trim = trimMm * MmToPt, sx = shiftXmm * MmToPt, sy = shiftYmm * MmToPt;

            return WithStagedForm(pdf, (tmp, output) =>
            {
                for (int i = 1; i <= count; i++)
                {
                    using var form = XPdfForm.FromFile(tmp); form.PageNumber = i;
                    double sw = form.PointWidth, sh = form.PointHeight;
                    double pw = Math.Max(1, sw - trim * 2), ph = Math.Max(1, sh - trim * 2);
                    var page = output.AddPage();
                    page.Width = pw; page.Height = ph;
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.IntersectClip(new XRect(0, 0, pw, ph));
                    // Move content up-left by the trim, plus the requested shift.
                    gfx.DrawImage(form, -trim + sx, -trim + sy, sw, sh);
                }
            });
        }

        // ── Sample document ─────────────────────────────────────────────────────────
        /// <summary>Generates a test PDF of <paramref name="pages"/> numbered pages at the given size.</summary>
        public byte[] GenerateSampleDocument(int pages, double widthMm = 210, double heightMm = 297)
        {
            if (pages < 1) throw new ArgumentException("عدد الصفحات يجب أن يكون 1 أو أكثر.", nameof(pages));
            PdfFonts.EnsureResolver();
            double w = widthMm * MmToPt, h = heightMm * MmToPt;

            using var output = new PdfDocument();
            for (int i = 1; i <= pages; i++)
            {
                var page = output.AddPage(); page.Width = w; page.Height = h;
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawRectangle(new XPen(XColors.Gray, 1), 6, 6, w - 12, h - 12);
                var big = new XFont("Arial", Math.Min(w, h) / 5, XFontStyle.Bold);
                var small = new XFont("Arial", 12, XFontStyle.Regular);
                gfx.DrawString($"{i}", big, XBrushes.Black, new XRect(0, 0, w, h), XStringFormats.Center);
                gfx.DrawString($"{widthMm:0}×{heightMm:0} mm  •  page {i}/{pages}", small, XBrushes.Gray,
                    new XRect(0, h - 34, w, 24), XStringFormats.Center);
            }
            return Save(output);
        }

        // ── Stamp overlay ("stick on → PDF pages") ──────────────────────────────────
        /// <summary>Overlays the first page of <paramref name="stampPdf"/> onto every base page.</summary>
        public byte[] StampPdf(byte[] basePdf, byte[] stampPdf,
            double xMm = 0, double yMm = 0, double? widthMm = null, double? heightMm = null)
        {
            int count = GetPageCount(basePdf);
            if (count == 0) throw new InvalidOperationException("الملف الأساسي لا يحتوي على صفحات.");

            string stampTmp = Stage(stampPdf);
            try
            {
                return WithStagedForm(basePdf, (tmp, output) =>
                {
                    for (int i = 1; i <= count; i++)
                    {
                        using var baseForm = XPdfForm.FromFile(tmp); baseForm.PageNumber = i;
                        double sw = baseForm.PointWidth, sh = baseForm.PointHeight;
                        var page = output.AddPage(); page.Width = sw; page.Height = sh;
                        using var gfx = XGraphics.FromPdfPage(page);
                        gfx.DrawImage(baseForm, 0, 0, sw, sh);

                        using var stamp = XPdfForm.FromFile(stampTmp); stamp.PageNumber = 1;
                        double dw = (widthMm.HasValue ? widthMm.Value * MmToPt : stamp.PointWidth);
                        double dh = (heightMm.HasValue ? heightMm.Value * MmToPt : stamp.PointHeight);
                        gfx.DrawImage(stamp, xMm * MmToPt, yMm * MmToPt, dw, dh);
                    }
                });
            }
            finally { TryDelete(stampTmp); }
        }

        // ── Mask area ("masking tape") ──────────────────────────────────────────────
        /// <summary>Draws a filled rectangle (default white) over a region of a page to hide content.</summary>
        public byte[] MaskArea(byte[] pdf, int page1Based, double xMm, double yMm, double widthMm, double heightMm,
            string colorHex = "#FFFFFF")
        {
            int count = GetPageCount(pdf);
            if (page1Based < 1 || page1Based > count)
                throw new ArgumentOutOfRangeException(nameof(page1Based));
            var color = ParseHex(colorHex);

            return WithStagedForm(pdf, (tmp, output) =>
            {
                for (int i = 1; i <= count; i++)
                {
                    using var form = XPdfForm.FromFile(tmp); form.PageNumber = i;
                    double sw = form.PointWidth, sh = form.PointHeight;
                    var page = output.AddPage(); page.Width = sw; page.Height = sh;
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawImage(form, 0, 0, sw, sh);
                    if (i == page1Based)
                        gfx.DrawRectangle(new XSolidBrush(color),
                            xMm * MmToPt, yMm * MmToPt, widthMm * MmToPt, heightMm * MmToPt);
                }
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        public int GetPageCount(byte[] pdf)
        {
            using var doc = OpenInfo(pdf);
            return doc.PageCount;
        }

        /// <summary>Rasterizes one page (<paramref name="page0"/>, 0-based) of a PDF to a PNG.</summary>
        public byte[] RenderPagePng(byte[] pdf, int page0 = 0, int dpi = 110)
            => PdfRasterizer.RenderPng(pdf, page0, dpi);

        private static byte[] WithStagedForm(byte[] pdf, Action<string, PdfDocument> build)
        {
            string tmp = Stage(pdf);
            try
            {
                using var output = new PdfDocument();
                build(tmp, output);
                return Save(output);
            }
            finally { TryDelete(tmp); }
        }

        private static string Stage(byte[] pdf)
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"apex_ptool_{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(tmp, pdf);
            return tmp;
        }

        private static void TryDelete(string path) { try { File.Delete(path); } catch { /* best effort */ } }

        private static XColor ParseHex(string hex)
        {
            hex = (hex ?? "").TrimStart('#');
            const System.Globalization.NumberStyles Hex = System.Globalization.NumberStyles.HexNumber;
            if (hex.Length == 6
                && int.TryParse(hex.Substring(0, 2), Hex, null, out int r)
                && int.TryParse(hex.Substring(2, 2), Hex, null, out int g)
                && int.TryParse(hex.Substring(4, 2), Hex, null, out int b))
                return XColor.FromArgb(r, g, b);
            return XColors.White;
        }

        private static PdfDocument OpenImport(byte[] bytes) =>
            PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

        private static PdfDocument OpenInfo(byte[] bytes) =>
            PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.InformationOnly);

        private static byte[] Save(PdfDocument doc)
        {
            using var ms = new MemoryStream();
            doc.Save(ms, closeStream: false);
            return ms.ToArray();
        }
    }
}
