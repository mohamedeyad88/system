using System;
using System.Collections.Generic;
using System.IO;
using Apex.Core.Utilities;
using Apex.UI.Rendering;
using Apex.UI.ViewModels;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Apex.UI.Services
{
    /// <summary>
    /// Writes rendered templates to a TRUE VECTOR PDF.
    ///
    /// Replaces the previous path (render to a 150-dpi bitmap, wrap the bitmap in a
    /// PDF), which capped every variable-data job at raster resolution and made
    /// small Arabic type print ragged. Text is emitted as outlines and shapes as
    /// paths, so output is resolution-independent; embedded photographs stay raster
    /// because they genuinely are raster.
    ///
    /// Pages are produced by the same <see cref="TemplatePainter"/> that draws the
    /// on-screen preview, so the file matches what the operator approved.
    /// </summary>
    public sealed class VectorPdfExporter
    {
        /// <summary>Writes one record per page to <paramref name="filePath"/>.</summary>
        public void Export(IEnumerable<RenderedTemplate> pages, string filePath)
        {
            if (pages == null) throw new ArgumentNullException(nameof(pages));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("مسار الحفظ مطلوب.", nameof(filePath));

            using var doc = new PdfDocument();
            int count = 0;

            foreach (var page in pages)
            {
                if (page == null) continue;
                AddPage(doc, page);
                count++;
            }

            if (count == 0)
                throw new InvalidOperationException("لا توجد صفحات للتصدير.");

            doc.Info.Creator = "Apex Print OS";
            doc.Save(filePath);
        }

        /// <summary>Writes a single rendered record as a one-page PDF.</summary>
        public void ExportSingle(RenderedTemplate page, string filePath) =>
            Export(new[] { page }, filePath);

        private static void AddPage(PdfDocument doc, RenderedTemplate template)
        {
            var page = doc.AddPage();
            // PDF page geometry is in points (1/72"), taken straight from millimetres
            // so the sheet is physically the size the operator designed.
            page.Width = UnitConverter.MmToPoints(template.WidthMm);
            page.Height = UnitConverter.MmToPoints(template.HeightMm);

            using var gfx = XGraphics.FromPdfPage(page);
            using var target = new PdfRenderTarget(gfx);

            TemplatePainter.Paint(template, target);
        }
    }
}
