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

                // Render at the page's true pixel size for the requested DPI.
                //
                // doc.Render(idx, dpi, dpi, false) looks like a DPI call but binds to the
                // Render(page, int WIDTH, int HEIGHT, bool) overload — dpi is an int — so it
                // produced a dpi×dpi image (110×110 px) that the UI then stretched to fill the
                // preview: every section looked pixelated no matter the page size. Compute the
                // real pixel dimensions from the page size (points → inches × dpi) instead, the
                // same way the print rasterizer was fixed.
                var sizePt = doc.PageSizes[idx];                 // points (1/72")
                int width  = Math.Max(1, (int)Math.Round(sizePt.Width  / 72.0 * dpi));
                int height = Math.Max(1, (int)Math.Round(sizePt.Height / 72.0 * dpi));
                using var img = doc.Render(idx, width, height, false);

                // Force a uniform 96×96 DPI on the bitmap before saving. Pdfium stamps the
                // rendered page with a NON-square DPI (e.g. 827×1169 for A4) whose x:y ratio
                // equals the page's aspect. WPF then reads that DPI to size the image, so a
                // correct 595×841-pixel A4 came out SQUARE on screen ("مطة"): the pixels were
                // right but the device-independent size was 1:1. Normalising the DPI makes WPF
                // display the image at its true pixel aspect. (Preview only — the print path
                // uses a different rasteriser and is unaffected.)
                if (img is System.Drawing.Bitmap bmp)
                    bmp.SetResolution(96f, 96f);

                using var png = new MemoryStream();
                img.Save(png, ImageFormat.Png);
                return png.ToArray();
            }
        }

        /// <summary>
        /// Renders up to <paramref name="maxPages"/> pages, loading the PDF <b>once</b> (not once
        /// per page — reloading a multi-MB file 25 times made a 25-page preview crawl or appear to
        /// hang). Each rendered page's PNG is handed to <paramref name="onPage"/> as it is ready so
        /// the UI can show pages progressively. A page that fails to render is skipped, never fatal;
        /// rendering stops early when <paramref name="cancelled"/> returns true.
        /// </summary>
        public static void RenderPagesStreaming(byte[] pdf, int maxPages, int dpi,
            Action<byte[]> onPage, Func<bool>? cancelled = null)
        {
            if (pdf == null || pdf.Length == 0 || onPage == null) return;
            if (dpi <= 0) dpi = 110;

            lock (RenderLock)
            {
                using var stream = new MemoryStream(pdf);
                using var doc = PdfDocument.Load(stream);
                int n = Math.Min(doc.PageCount, Math.Max(0, maxPages));
                for (int i = 0; i < n; i++)
                {
                    if (cancelled != null && cancelled()) return;
                    try
                    {
                        var sizePt = doc.PageSizes[i];
                        int w = Math.Max(1, (int)Math.Round(sizePt.Width / 72.0 * dpi));
                        int h = Math.Max(1, (int)Math.Round(sizePt.Height / 72.0 * dpi));
                        using var img = doc.Render(i, w, h, false);
                        if (img is System.Drawing.Bitmap bmp) bmp.SetResolution(96f, 96f);
                        using var ms = new MemoryStream();
                        img.Save(ms, ImageFormat.Png);
                        onPage(ms.ToArray());
                    }
                    catch { /* skip this page, keep going */ }
                }
            }
        }
    }
}
