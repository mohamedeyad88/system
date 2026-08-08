using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Apex.Services.Imposition
{
    /// <summary>
    /// PDF page operations for pre-press: merge, split, rotate, delete, reorder, extract.
    /// Works on in-memory byte arrays (testable) using PdfSharpCore. All page indices are
    /// 1-based to match how users refer to pages.
    /// </summary>
    public class PdfOperationsService
    {
        // ── Read ────────────────────────────────────────────────────────────────

        public int GetPageCount(byte[] pdf)
        {
            using var doc = OpenInfo(pdf);
            return doc.PageCount;
        }

        /// <summary>
        /// Returns the first page's size in millimetres. Used to auto-fill the
        /// imposition page-size fields so a mismatch between the configured size
        /// and the real source never silently degrades the layout.
        /// </summary>
        public (double WidthMm, double HeightMm) GetFirstPageSizeMm(byte[] pdf)
        {
            using var doc = OpenInfo(pdf);
            if (doc.PageCount == 0) return (0, 0);
            var p = doc.Pages[0];
            const double PtToMm = 25.4 / 72.0;
            return (p.Width.Point * PtToMm, p.Height.Point * PtToMm);
        }

        // ── Merge ───────────────────────────────────────────────────────────────

        /// <summary>Concatenates several PDFs into one, preserving page order.</summary>
        public byte[] Merge(IReadOnlyList<byte[]> pdfs)
        {
            if (pdfs == null || pdfs.Count == 0)
                throw new ArgumentException("لا توجد ملفات للدمج.", nameof(pdfs));

            using var output = new PdfDocument();
            foreach (var bytes in pdfs)
            {
                using var src = OpenImport(bytes);
                for (int i = 0; i < src.PageCount; i++)
                    output.AddPage(src.Pages[i]);
            }
            return Save(output);
        }

        // ── Split ───────────────────────────────────────────────────────────────

        /// <summary>Splits a PDF into one single-page PDF per source page.</summary>
        public IReadOnlyList<byte[]> SplitToPages(byte[] pdf)
        {
            using var src = OpenImport(pdf);
            var results = new List<byte[]>(src.PageCount);
            for (int i = 0; i < src.PageCount; i++)
            {
                using var one = new PdfDocument();
                one.AddPage(src.Pages[i]);
                results.Add(Save(one));
            }
            return results;
        }

        // ── Extract / Delete ──────────────────────────────────────────────────────

        /// <summary>Returns a PDF containing only the given pages, in the given order (1-based).</summary>
        public byte[] ExtractPages(byte[] pdf, IReadOnlyList<int> pages1Based)
        {
            using var src = OpenImport(pdf);
            ValidatePages(pages1Based, src.PageCount);

            using var output = new PdfDocument();
            foreach (int p in pages1Based)
                output.AddPage(src.Pages[p - 1]);
            return Save(output);
        }

        /// <summary>Returns a PDF with the given pages removed (1-based).</summary>
        public byte[] DeletePages(byte[] pdf, IReadOnlyList<int> pages1Based)
        {
            using var src = OpenImport(pdf);
            ValidatePages(pages1Based, src.PageCount);

            var remove = new HashSet<int>(pages1Based);
            using var output = new PdfDocument();
            for (int i = 1; i <= src.PageCount; i++)
                if (!remove.Contains(i))
                    output.AddPage(src.Pages[i - 1]);

            if (output.PageCount == 0)
                throw new InvalidOperationException("لا يمكن حذف كل الصفحات.");
            return Save(output);
        }

        // ── Reorder ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Reorders pages according to <paramref name="order1Based"/>, which must be a
        /// permutation of 1..PageCount.
        /// </summary>
        public byte[] ReorderPages(byte[] pdf, IReadOnlyList<int> order1Based)
        {
            using var src = OpenImport(pdf);
            if (order1Based == null || order1Based.Count != src.PageCount
                || !order1Based.OrderBy(x => x).SequenceEqual(Enumerable.Range(1, src.PageCount)))
                throw new ArgumentException("ترتيب الصفحات يجب أن يكون توزيعة كاملة لكل الصفحات.", nameof(order1Based));

            using var output = new PdfDocument();
            foreach (int p in order1Based)
                output.AddPage(src.Pages[p - 1]);
            return Save(output);
        }

        // ── Rotate ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rotates the given pages (1-based) by <paramref name="degrees"/> (90/180/270),
        /// leaving other pages untouched. Returns a new PDF with all pages preserved.
        /// </summary>
        public byte[] RotatePages(byte[] pdf, IReadOnlyList<int> pages1Based, int degrees)
        {
            int norm = ((degrees % 360) + 360) % 360;
            if (norm % 90 != 0)
                throw new ArgumentException("زاوية التدوير يجب أن تكون مضاعفاً لـ 90.", nameof(degrees));

            using var src = OpenImport(pdf);
            ValidatePages(pages1Based, src.PageCount);
            var rotate = new HashSet<int>(pages1Based);

            using var output = new PdfDocument();
            for (int i = 1; i <= src.PageCount; i++)
            {
                var added = output.AddPage(src.Pages[i - 1]);
                if (rotate.Contains(i))
                    added.Rotate = (added.Rotate + norm) % 360;
            }
            return Save(output);
        }

        // ── Resize / scale ──────────────────────────────────────────────────────

        private const double MmToPt = 72.0 / 25.4;

        /// <summary>
        /// Resizes every page to a target page size (mm). Page content is scaled
        /// vectorially onto the new page. When <paramref name="maintainAspect"/> is true
        /// the content is fit inside the new size and centred (no distortion); otherwise
        /// it is stretched to fill exactly.
        /// </summary>
        public byte[] ResizePages(byte[] pdf, double targetWidthMm, double targetHeightMm, bool maintainAspect = true)
        {
            if (targetWidthMm <= 0 || targetHeightMm <= 0)
                throw new ArgumentException("مقاس الهدف يجب أن يكون أكبر من صفر.");

            int count = GetPageCount(pdf);
            if (count == 0) throw new InvalidOperationException("الملف لا يحتوي على صفحات.");

            double tw = targetWidthMm * MmToPt;
            double th = targetHeightMm * MmToPt;

            // XPdfForm reads source pages by file path, so stage the bytes in a temp file.
            string tmp = Path.Combine(Path.GetTempPath(), $"apex_resize_{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(tmp, pdf);
            try
            {
                using var output = new PdfDocument();
                for (int i = 1; i <= count; i++)
                {
                    var page = output.AddPage();
                    page.Width = tw;
                    page.Height = th;

                    using var gfx = XGraphics.FromPdfPage(page);
                    using var form = XPdfForm.FromFile(tmp);
                    form.PageNumber = i;

                    double sw = form.PointWidth, sh = form.PointHeight;
                    if (sw <= 0 || sh <= 0) { gfx.DrawImage(form, 0, 0, tw, th); continue; }

                    if (maintainAspect)
                    {
                        double scale = Math.Min(tw / sw, th / sh);
                        double dw = sw * scale, dh = sh * scale;
                        gfx.DrawImage(form, (tw - dw) / 2.0, (th - dh) / 2.0, dw, dh);
                    }
                    else
                    {
                        gfx.DrawImage(form, 0, 0, tw, th);
                    }
                }
                return Save(output);
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* best-effort cleanup */ }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static PdfDocument OpenImport(byte[] bytes)
        {
            var ms = new MemoryStream(bytes);
            return PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        }

        private static PdfDocument OpenInfo(byte[] bytes)
        {
            var ms = new MemoryStream(bytes);
            return PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        }

        private static byte[] Save(PdfDocument doc)
        {
            using var ms = new MemoryStream();
            doc.Save(ms, closeStream: false);
            return ms.ToArray();
        }

        private static void ValidatePages(IReadOnlyList<int> pages, int count)
        {
            if (pages == null || pages.Count == 0)
                throw new ArgumentException("لم يتم تحديد صفحات.");
            foreach (int p in pages)
                if (p < 1 || p > count)
                    throw new ArgumentOutOfRangeException(nameof(pages), $"رقم صفحة غير صالح: {p} (المدى 1..{count}).");
        }
    }
}
