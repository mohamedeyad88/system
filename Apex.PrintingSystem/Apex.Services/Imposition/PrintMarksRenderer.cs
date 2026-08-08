using Apex.Core.Models.Imposition;
using PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;

namespace Apex.Services.Imposition
{
    /// <summary>
    /// Draws printer's marks (crop, fold, registration, colour bars, job slug) onto an
    /// imposed press-sheet page via PdfSharpCore <see cref="XGraphics"/>. All input
    /// geometry is in millimetres and converted to PDF points internally.
    /// </summary>
    public class PrintMarksRenderer
    {
        private const double MmToPt = 72.0 / 25.4;

        // Prepress rule: marks must be K-only (100% black plate), NOT RGB black.
        // RGB black separates to a 4-colour composite at the RIP, so any plate
        // misregistration ghosts the crop marks. CMYK(0,0,0,1) stays on K.
        private static readonly XColor MarkBlack = XColor.FromCmyk(0, 0, 0, 1);

        /// <summary>
        /// Renders the requested marks for one sheet side.
        /// </summary>
        /// <param name="gfx">Graphics for the output sheet page.</param>
        /// <param name="sheetWmm">Sheet width (mm).</param>
        /// <param name="sheetHmm">Sheet height (mm).</param>
        /// <param name="slots">Placed page slots on this side (positions include bleed).</param>
        /// <param name="bleedMm">Bleed applied around each page (mm), both axes.</param>
        /// <param name="opt">Which marks to draw and their sizing.</param>
        public void Draw(
            XGraphics gfx, double sheetWmm, double sheetHmm,
            IReadOnlyList<PageSlot> slots, double bleedMm, PrintMarksOptions opt)
            => Draw(gfx, sheetWmm, sheetHmm, slots, bleedMm, bleedMm, opt);

        /// <summary>
        /// Renders marks when bleed differs per axis. Crop marks sit on the trim line,
        /// so using one figure for both axes puts them inside the artwork on whichever
        /// axis bleeds more — and the operator cuts to a mark that is in the wrong place.
        /// </summary>
        public void Draw(
            XGraphics gfx, double sheetWmm, double sheetHmm,
            IReadOnlyList<PageSlot> slots, double bleedHmm, double bleedVmm, PrintMarksOptions opt)
        {
            if (gfx == null || opt == null) return;

            double thick = Math.Max(0.05, opt.MarkThickness) * MmToPt;
            var pen = new XPen(MarkBlack, thick);

            if (opt.CropMarks)
                foreach (var slot in slots)
                    DrawCropMarks(gfx, pen, slot, bleedHmm, bleedVmm, opt);

            if (opt.FoldMarks)
                DrawFoldMarks(gfx, pen, sheetWmm, sheetHmm, opt);

            if (opt.RegistrationMarks)
                DrawRegistrationMarks(gfx, sheetWmm, sheetHmm, thick);

            if (opt.ColorBars)
                DrawColorBars(gfx, sheetWmm, sheetHmm);

            if (opt.JobInfo)
                DrawJobInfo(gfx, sheetHmm, opt);
        }

        // ── Crop marks (at each page's trim corners) ───────────────────────────────

        private static void DrawCropMarks(
            XGraphics gfx, XPen pen, PageSlot slot,
            double bleedHmm, double bleedVmm, PrintMarksOptions opt)
        {
            if (slot.IsBlank) return;

            // Trim box = placed slot inset by that axis' bleed.
            double bh = bleedHmm * MmToPt, bv = bleedVmm * MmToPt;
            double tx = slot.X * MmToPt + bh;
            double ty = slot.Y * MmToPt + bv;
            double tw = slot.Width * MmToPt - 2 * bh;
            double th = slot.Height * MmToPt - 2 * bv;
            if (tw <= 0 || th <= 0) return;

            double len = opt.MarkLength * MmToPt;
            double off = opt.MarkOffset * MmToPt;

            double left = tx, right = tx + tw, top = ty, bottom = ty + th;

            // Each corner: one horizontal + one vertical tick pointing outward.
            // Top-left
            gfx.DrawLine(pen, left - off - len, top, left - off, top);
            gfx.DrawLine(pen, left, top - off - len, left, top - off);
            // Top-right
            gfx.DrawLine(pen, right + off, top, right + off + len, top);
            gfx.DrawLine(pen, right, top - off - len, right, top - off);
            // Bottom-left
            gfx.DrawLine(pen, left - off - len, bottom, left - off, bottom);
            gfx.DrawLine(pen, left, bottom + off, left, bottom + off + len);
            // Bottom-right
            gfx.DrawLine(pen, right + off, bottom, right + off + len, bottom);
            gfx.DrawLine(pen, right, bottom + off, right, bottom + off + len);
        }

        // ── Fold marks (dashed, at sheet center) ───────────────────────────────────

        private static void DrawFoldMarks(
            XGraphics gfx, XPen basePen, double sheetWmm, double sheetHmm, PrintMarksOptions opt)
        {
            double cx = sheetWmm / 2.0 * MmToPt;
            double len = opt.MarkLength * MmToPt;
            double sheetH = sheetHmm * MmToPt;

            var dash = new XPen(MarkBlack, basePen.Width) { DashStyle = XDashStyle.Dash };
            // Short fold ticks at top and bottom center.
            gfx.DrawLine(dash, cx, 0, cx, len);
            gfx.DrawLine(dash, cx, sheetH - len, cx, sheetH);
        }

        // ── Registration marks (target at edge centers) ───────────────────────────

        private static void DrawRegistrationMarks(
            XGraphics gfx, double sheetWmm, double sheetHmm, double thick)
        {
            double w = sheetWmm * MmToPt, h = sheetHmm * MmToPt;
            double r = 3.0 * MmToPt;          // target radius
            double margin = 5.0 * MmToPt;     // distance from edge

            DrawTarget(gfx, w / 2, margin, r, thick);          // top
            DrawTarget(gfx, w / 2, h - margin, r, thick);      // bottom
            DrawTarget(gfx, margin, h / 2, r, thick);          // left
            DrawTarget(gfx, w - margin, h / 2, r, thick);      // right
        }

        private static void DrawTarget(XGraphics gfx, double cx, double cy, double r, double thick)
        {
            var pen = new XPen(MarkBlack, thick);
            gfx.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
            gfx.DrawLine(pen, cx - r * 1.5, cy, cx + r * 1.5, cy);  // horizontal crosshair
            gfx.DrawLine(pen, cx, cy - r * 1.5, cx, cy + r * 1.5);  // vertical crosshair
        }

        // ── Colour bars (CMYK + RGB swatches along the bottom) ────────────────────

        private static void DrawColorBars(XGraphics gfx, double sheetWmm, double sheetHmm)
        {
            // True process-ink patches (100% C, M, Y, K, plus overprint pairs and a
            // 50% K tint) — RGB approximations are useless for press calibration.
            var colors = new[]
            {
                XColor.FromCmyk(1, 0, 0, 0),   // C
                XColor.FromCmyk(0, 1, 0, 0),   // M
                XColor.FromCmyk(0, 0, 1, 0),   // Y
                XColor.FromCmyk(0, 0, 0, 1),   // K
                XColor.FromCmyk(0, 1, 1, 0),   // M+Y (red)
                XColor.FromCmyk(1, 0, 1, 0),   // C+Y (green)
                XColor.FromCmyk(1, 1, 0, 0),   // C+M (blue)
                XColor.FromCmyk(0, 0, 0, 0.5), // 50% K
            };
            double sw = 6.0 * MmToPt, sh = 4.0 * MmToPt;
            double y = sheetHmm * MmToPt - sh - 2 * MmToPt;
            double x = sheetWmm / 2.0 * MmToPt - colors.Length * sw / 2.0;

            foreach (var c in colors)
            {
                gfx.DrawRectangle(new XSolidBrush(c), x, y, sw, sh);
                x += sw;
            }
        }

        // ── Job-info slug (defensive: skipped if no font resolver) ────────────────

        private static void DrawJobInfo(XGraphics gfx, double sheetHmm, PrintMarksOptions opt)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(opt.JobName)) parts.Add(opt.JobName);
            if (!string.IsNullOrWhiteSpace(opt.FileName)) parts.Add(opt.FileName);
            if (opt.IncludeDate) parts.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            if (parts.Count == 0) return;

            string text = string.Join("  |  ", parts);
            try
            {
                var font = new XFont("Arial", 7, XFontStyle.Regular);
                double y = sheetHmm * MmToPt - 3 * MmToPt;
                gfx.DrawString(text, font, new XSolidBrush(MarkBlack),
                    new XPoint(4 * MmToPt, y));
            }
            catch
            {
                // No font resolver available in this environment — skip text slug,
                // line/colour marks are unaffected.
            }
        }
    }
}
