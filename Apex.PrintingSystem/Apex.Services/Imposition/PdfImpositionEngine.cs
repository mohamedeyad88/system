using Apex.Core.Models.Imposition;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System;
using System.IO;

namespace Apex.Services.Imposition
{
    /// <summary>
    /// Renders an <see cref="ImpositionResult"/> plan into a print-ready PDF: each sheet
    /// side becomes one output page, and every <see cref="PageSlot"/> draws its source page
    /// (vector, via <see cref="XPdfForm"/>) at the planned position/size/rotation. Optional
    /// printer's marks are drawn on top by <see cref="PrintMarksRenderer"/>.
    /// </summary>
    public class PdfImpositionEngine
    {
        private const double MmToPt = 72.0 / 25.4;
        private readonly PrintMarksRenderer _marks = new();

        /// <summary>
        /// Generates the imposed PDF.
        /// </summary>
        /// <param name="plan">Layout produced by <c>ImpositionService.Plan</c>.</param>
        /// <param name="sourcePdfPath">Path to the source document (XPdfForm reads by path).</param>
        /// <param name="marks">Printer's-marks options, or null to draw none.</param>
        /// <returns>The imposed PDF as a byte array.</returns>
        public byte[] Generate(
            ImpositionResult plan, string sourcePdfPath,
            PrintMarksOptions? marks = null, ImpositionExportOptions? export = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsValid) throw new InvalidOperationException($"خطة مونتاج غير صالحة: {plan.ErrorMessage}");
            if (string.IsNullOrWhiteSpace(sourcePdfPath) || !File.Exists(sourcePdfPath))
                throw new FileNotFoundException("ملف المصدر غير موجود.", sourcePdfPath);

            PdfFonts.EnsureResolver(); // so job-info / mark labels actually render

            using var output = new PdfDocument();
            double bleedMm = plan.BleedMm;

            // One cache for the whole run: the source file is parsed once per page,
            // not once per placed slot.
            using var source = new SourcePageCache(sourcePdfPath);

            foreach (var sheet in plan.Sheets)
            {
                RenderSide(output, sheet.Front, plan, source, bleedMm, marks);
                if (plan.IsDuplex && sheet.Back != null)
                    RenderSide(output, sheet.Back, plan, source, bleedMm, marks);
            }

            ApplyMetadata(output, export);

            using var ms = new MemoryStream();
            output.Save(ms, closeStream: false);
            return ms.ToArray();
        }

        /// <summary>
        /// Renders one sheet (<paramref name="sheetIndex"/>) of a plan to a PNG for on-screen
        /// preview — showing exactly what will export (grid, alignment, scaling, marks, real
        /// page content), rasterized.
        /// </summary>
        public byte[] RenderSheetPng(ImpositionResult plan, string sourcePdfPath,
            PrintMarksOptions? marks = null, int sheetIndex = 0, int dpi = 96)
        {
            if (plan == null || plan.Sheets.Count == 0)
                throw new InvalidOperationException("لا توجد أوراق للمعاينة.");

            int idx = Math.Clamp(sheetIndex, 0, plan.Sheets.Count - 1);
            var preview = new ImpositionResult
            {
                IsValid = true,
                SheetWidthMm = plan.SheetWidthMm,
                SheetHeightMm = plan.SheetHeightMm,
                Sheets = new System.Collections.Generic.List<SheetLayout> { plan.Sheets[idx] },
            };

            byte[] pdf = Generate(preview, sourcePdfPath, marks);
            return PdfRasterizer.RenderPng(pdf, 0, dpi);
        }

        /// <summary>
        /// Stamps document info and, when requested, PDF/X identification metadata.
        /// This is identification-level (GTS_PDFXVersion) — full PDF/X validation requires
        /// an embedded ICC OutputIntent which PdfSharpCore does not produce; the flag marks
        /// intent and sets the trim/title so downstream RIPs recognise the file.
        /// </summary>
        private static void ApplyMetadata(PdfDocument doc, ImpositionExportOptions? export)
        {
            doc.Info.Creator = "Apex Print OS — Imposition";
            if (export == null) return;

            if (!string.IsNullOrWhiteSpace(export.Title)) doc.Info.Title = export.Title;
            if (!string.IsNullOrWhiteSpace(export.Author)) doc.Info.Author = export.Author;

            if (export.PdfX == PdfXConformance.None) return;

            // DELIBERATELY NOT stamping /GTS_PDFXVersion.
            //
            // Real PDF/X conformance requires an embedded ICC OutputIntent, plus
            // guarantees about fonts, transparency and colour spaces, none of which
            // this writer produces. Stamping the key anyway made the file CLAIM a
            // conformance it does not have: the customer's prepress rejects it, and
            // naive tools trust it and print with the wrong colour assumptions.
            //
            // Until a real OutputIntent is embedded, we record the operator's intent
            // as a plain keyword only — an honest label, not a conformance claim.
            string intent = export.PdfX == PdfXConformance.PdfX1a ? "PDF/X-1a" : "PDF/X-3";
            try
            {
                doc.Info.Keywords = $"Apex intended output: {intent} (identification not embedded)";
            }
            catch
            {
                // Metadata is best-effort; never fail an export over it.
            }
        }

        // ── One output page per sheet side ────────────────────────────────────────

        /// <summary>
        /// Caches one <see cref="XPdfForm"/> per source page for the lifetime of a
        /// single Generate/Render run.
        ///
        /// Without it the source PDF was re-opened and re-parsed for EVERY placed
        /// slot: a 16-up duplex job of 100 sheets meant 3,200 full parses of the same
        /// file, which turned minutes of work into hours on real jobs.
        /// </summary>
        private sealed class SourcePageCache : IDisposable
        {
            private readonly Dictionary<int, XPdfForm> _byPage = new();
            private readonly string _path;

            public SourcePageCache(string path) => _path = path;

            public XPdfForm Get(int pageNumber1Based)
            {
                if (_byPage.TryGetValue(pageNumber1Based, out var cached)) return cached;

                var form = XPdfForm.FromFile(_path);
                form.PageNumber = pageNumber1Based;
                _byPage[pageNumber1Based] = form;
                return form;
            }

            /// <summary>Page count of the source document (opens once, then cached).</summary>
            public int PageCount => Get(1).PageCount;

            public void Dispose()
            {
                foreach (var f in _byPage.Values)
                {
                    try { f.Dispose(); } catch { /* best-effort cleanup */ }
                }
                _byPage.Clear();
            }
        }

        private void RenderSide(
            PdfDocument output, SheetSide side, ImpositionResult plan,
            SourcePageCache source, double bleedMm, PrintMarksOptions? marks)
        {
            var page = output.AddPage();
            page.Width = plan.SheetWidthMm * MmToPt;
            page.Height = plan.SheetHeightMm * MmToPt;

            using var gfx = XGraphics.FromPdfPage(page);

            foreach (var slot in side.Slots)
            {
                if (slot.IsBlank) continue;
                DrawSlot(gfx, slot, source);
            }

            if (marks != null)
                _marks.Draw(gfx, plan.SheetWidthMm, plan.SheetHeightMm, side.Slots,
                    plan.BleedHorizontalMm > 0 ? plan.BleedHorizontalMm : bleedMm,
                    plan.BleedVerticalMm > 0 ? plan.BleedVerticalMm : bleedMm,
                    marks);

            SetPrepressBoxes(page, plan, side, bleedMm);
        }

        /// <summary>
        /// Writes prepress geometry boxes so RIPs/cutters know the sheet's live area
        /// programmatically (not just via visual crop marks):
        ///   BleedBox = union of all placed slots (content incl. bleed),
        ///   TrimBox  = that union inset by the bleed (the final trim result).
        /// PDF box origin is bottom-left, while our slot Y grows downward from the
        /// top — so Y is flipped against the sheet height.
        /// </summary>
        private static void SetPrepressBoxes(
            PdfSharpCore.Pdf.PdfPage page, ImpositionResult plan, SheetSide side, double bleedMm)
        {
            // Bleed can differ per axis. Insetting both axes by one figure puts the
            // trim line inside the artwork on whichever axis has the larger bleed —
            // a cutter following it removes live art on every product of the sheet.
            double bx = plan.BleedHorizontalMm > 0 ? plan.BleedHorizontalMm : bleedMm;
            double by = plan.BleedVerticalMm > 0 ? plan.BleedVerticalMm : bleedMm;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;
            foreach (var s in side.Slots)
            {
                if (s.IsBlank) continue;
                any = true;
                minX = Math.Min(minX, s.X);
                minY = Math.Min(minY, s.Y);
                maxX = Math.Max(maxX, s.X + s.Width);
                maxY = Math.Max(maxY, s.Y + s.Height);
            }
            if (!any) return;

            double sheetH = plan.SheetHeightMm;

            // Bleed box: full placed area (top-down mm → bottom-up pt).
            page.BleedBox = new PdfSharpCore.Pdf.PdfRectangle(
                new XPoint(minX * MmToPt, (sheetH - maxY) * MmToPt),
                new XPoint(maxX * MmToPt, (sheetH - minY) * MmToPt));

            // Trim box: inset by the bleed of each axis, clamped to stay a valid rectangle.
            double bh = Math.Max(0, bx), bv = Math.Max(0, by);
            double tMinX = minX + bh, tMaxX = maxX - bh;
            double tMinY = minY + bv, tMaxY = maxY - bv;
            if (tMaxX > tMinX && tMaxY > tMinY)
            {
                page.TrimBox = new PdfSharpCore.Pdf.PdfRectangle(
                    new XPoint(tMinX * MmToPt, (sheetH - tMaxY) * MmToPt),
                    new XPoint(tMaxX * MmToPt, (sheetH - tMinY) * MmToPt));
            }
        }

        // ── Draw one source page into a slot (vector) ─────────────────────────────

        private static void DrawSlot(XGraphics gfx, PageSlot slot, SourcePageCache source)
        {
            // Clamp requested page to the available range in the source document.
            int page = Math.Max(1, Math.Min(slot.SourcePageNumber, source.PageCount));
            var form = source.Get(page);   // cached — not re-opened per slot

            double x = slot.X * MmToPt;
            double y = slot.Y * MmToPt;
            double w = slot.Width * MmToPt;
            double h = slot.Height * MmToPt;

            // Print-quality rule: NEVER stretch the source page. Scale uniformly
            // (same factor on both axes) to fit the slot and centre the result.
            // Stretching caused anamorphic distortion when the configured page size
            // didn't match the true source page size (found in prepress QA review).
            double srcW = form.PointWidth;
            double srcH = form.PointHeight;
            if (srcW <= 0 || srcH <= 0) { gfx.DrawImage(form, x, y, w, h); return; }

            bool quarter = slot.Rotation % 180 != 0;

            // Footprint the source occupies inside the slot (swapped when rotated 90/270).
            double fitW = quarter ? h : w;   // slot extent along the page's own width axis
            double fitH = quarter ? w : h;   // slot extent along the page's own height axis
            double scale = Math.Min(fitW / srcW, fitH / srcH);
            double dw = srcW * scale;
            double dh = srcH * scale;

            if (slot.Rotation == 0)
            {
                // Centre the uniformly-scaled page inside the slot.
                gfx.DrawImage(form, x + (w - dw) / 2.0, y + (h - dh) / 2.0, dw, dh);
                return;
            }

            // Rotate about the slot centre for 90/180/270, drawing at uniform scale.
            var state = gfx.Save();
            gfx.TranslateTransform(x + w / 2.0, y + h / 2.0);
            gfx.RotateTransform(slot.Rotation);
            gfx.DrawImage(form, -dw / 2.0, -dh / 2.0, dw, dh);
            gfx.Restore(state);
        }

    }
}
