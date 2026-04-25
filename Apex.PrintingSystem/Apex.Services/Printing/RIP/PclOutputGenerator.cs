using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Apex.Services.Printing.RIP
{
    /// <summary>
    /// 🖨️ PCL 5 OUTPUT GENERATOR
    ///
    /// Generates PCL5 (Printer Command Language 5) raster graphics output.
    ///
    /// PCL5 is supported by:
    ///   - All HP LaserJet printers (1984+)
    ///   - Many Canon and Brother laser printers
    ///   - Most enterprise Xerox/Ricoh devices
    ///
    /// Raster approach: JPEG-encoded color, or raw 1-bit monochrome for B&W.
    /// PCL5 Image commands: ESC*r (raster graphics) + ESC*b (raster data transfer)
    /// </summary>
    public static class PclOutputGenerator
    {
        // PCL escape character
        private const byte ESC = 0x1B;

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Generate complete PCL5 document from a rendered bitmap.
        /// </summary>
        public static byte[] GeneratePage(Bitmap page, bool isColorPrinter = true)
        {
            using var ms = new MemoryStream();

            // ── PCL Reset + Job Header ────────────────────────────────────
            ms.Write(BuildResetSequence());
            ms.Write(BuildJobHeader(page.Width, page.Height, isColorPrinter));

            // ── Raster Image ──────────────────────────────────────────────
            if (isColorPrinter)
                ms.Write(BuildColorRaster(page));
            else
                ms.Write(BuildMonochromeRaster(page));

            // ── PCL Reset + Form Feed ─────────────────────────────────────
            ms.Write(BuildJobTrailer());

            return ms.ToArray();
        }

        // ── PCL Building Blocks ───────────────────────────────────────────

        private static byte[] BuildResetSequence()
        {
            // ESC E = Printer Reset
            return new byte[] { ESC, (byte)'E' };
        }

        private static byte[] BuildJobHeader(int widthPx, int heightPx, bool color)
        {
            using var ms = new MemoryStream();

            // ESC &l0O = Portrait orientation
            WritePcl(ms, "&l0O");

            // ESC &l2A = Letter size (2=Letter, 26=A4, 27=A3)
            WritePcl(ms, "&l26A");   // A4

            // ESC *t300R = Set raster resolution to 300 DPI
            WritePcl(ms, "*t300R");

            // ESC *r0F = Raster orientation (0=landscape clipping, 3=portrait clipping)
            WritePcl(ms, "*r3F");

            if (color)
            {
                // ESC *v6W = Color mode: simple CMY + black
                // 6 bytes: color model(1=device RGB), pixel encoding(3=direct), bits per index(8)
                // bits per component: R(8) G(8) B(8)
                ms.Write(new byte[] { ESC, (byte)'*', (byte)'v', (byte)'6', (byte)'W',
                    0x01,   // Color model: Device RGB
                    0x03,   // Pixel encoding: direct pixel
                    0x00,   // Bits per index (N/A for direct)
                    0x08,   // Bits per R component
                    0x08,   // Bits per G component
                    0x08    // Bits per B component
                });
            }

            // ESC *r<width>S = Set source raster width
            WritePcl(ms, $"*r{widthPx}S");

            // ESC *r<height>T = Set source raster height
            WritePcl(ms, $"*r{heightPx}T");

            // ESC *r1A = Start raster graphics (relative to current print position)
            WritePcl(ms, "*r1A");

            return ms.ToArray();
        }

        private static byte[] BuildColorRaster(Bitmap bmp)
        {
            using var ms = new MemoryStream();

            // Encode as JPEG for color (PCL5 supports raster images via JPEG-encoded rows)
            // Actually PCL5 standard is row-by-row. We use uncompressed RGB rows.

            // ESC *b0M = Compression mode 0 (uncompressed)
            WritePcl(ms, "*b0M");

            var rect  = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                int stride = bmpData.Stride;
                int width  = bmp.Width;
                int height = bmp.Height;

                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0;

                    for (int y = 0; y < height; y++)
                    {
                        // Each raster row: ESC *b<count>W followed by row data
                        byte* row    = ptr + y * stride;
                        int   rowLen = width * 3; // 3 bytes per pixel (RGB)

                        // Build row bytes (BGR → RGB conversion)
                        var rowBytes = new byte[rowLen];
                        for (int x = 0; x < width; x++)
                        {
                            rowBytes[x * 3 + 0] = row[x * 3 + 2]; // R
                            rowBytes[x * 3 + 1] = row[x * 3 + 1]; // G
                            rowBytes[x * 3 + 2] = row[x * 3 + 0]; // B
                        }

                        // ESC *b<count>W = Transfer raster data (W = last row of band)
                        WritePcl(ms, $"*b{rowLen}W");
                        ms.Write(rowBytes, 0, rowLen);
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            return ms.ToArray();
        }

        private static byte[] BuildMonochromeRaster(Bitmap bmp)
        {
            using var ms = new MemoryStream();

            // ESC *b0M = No compression
            WritePcl(ms, "*b0M");

            // For monochrome: 1 bit per pixel, packed 8 pixels per byte
            // Row width in bytes (ceil to byte boundary)
            int rowBytes = (bmp.Width + 7) / 8;

            var rect    = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var bmpMono = bmp.Clone(rect, PixelFormat.Format1bppIndexed);

            try
            {
                var bmpData = bmpMono.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format1bppIndexed);

                try
                {
                    int stride = bmpData.Stride;
                    int height = bmp.Height;

                    unsafe
                    {
                        byte* ptr = (byte*)bmpData.Scan0;

                        for (int y = 0; y < height; y++)
                        {
                            byte* row      = ptr + y * stride;
                            var   rowCopy  = new byte[rowBytes];
                            // Note: in PCL, 0-bit = black ink, 1-bit = white (opposite of BMP 1bpp)
                            for (int b = 0; b < rowBytes; b++)
                                rowCopy[b] = (byte)~row[b]; // Invert

                            WritePcl(ms, $"*b{rowBytes}W");
                            ms.Write(rowCopy, 0, rowBytes);
                        }
                    }
                }
                finally
                {
                    bmpMono.UnlockBits(bmpData);
                }
            }
            finally
            {
                bmpMono.Dispose();
            }

            return ms.ToArray();
        }

        private static byte[] BuildJobTrailer()
        {
            using var ms = new MemoryStream();

            // ESC *rB = End raster graphics
            WritePcl(ms, "*rB");

            // Form feed
            ms.WriteByte(0x0C);

            // ESC E = Reset
            ms.Write(new byte[] { ESC, (byte)'E' });

            return ms.ToArray();
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static void WritePcl(Stream stream, string command)
        {
            // PCL commands start with ESC
            stream.WriteByte(ESC);
            var bytes = System.Text.Encoding.ASCII.GetBytes(command);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
