using PdfiumViewer;
using System;
using System.Drawing.Imaging;
using System.IO;

namespace Apex.Services.Imposition
{
    /// <summary>
    /// Thread-safe PDF → PNG rasterization. Pdfium (the native engine) is NOT thread-safe,
    /// so every render is serialized behind a single process-wide lock. Without this, a live
    /// preview overlapping an export — or rapid preview-page navigation — can crash the
    /// native library. All on-screen preview paths go through here.
    /// </summary>
    public static class PdfRasterizer
    {
        private static readonly object RenderLock = new();

        /// <summary>Renders page <paramref name="page0"/> (0-based, clamped) of a PDF to PNG bytes.</summary>
        public static byte[] RenderPng(byte[] pdf, int page0 = 0, int dpi = 110)
        {
            if (pdf == null || pdf.Length == 0)
                throw new ArgumentException("لا يوجد ملف للعرض.", nameof(pdf));
            if (dpi <= 0) dpi = 110;

            lock (RenderLock)
            {
                using var stream = new MemoryStream(pdf);
                using var doc = PdfDocument.Load(stream);
                if (doc.PageCount == 0)
                    throw new InvalidOperationException("الملف لا يحتوي على صفحات.");

                int idx = Math.Clamp(page0, 0, doc.PageCount - 1);
                using var img = doc.Render(idx, dpi, dpi, false);
                using var png = new MemoryStream();
                img.Save(png, ImageFormat.Png);
                return png.ToArray();
            }
        }
    }
}
