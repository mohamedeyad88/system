using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace Apex.Services.Printing.RIP
{
    /// <summary>
    /// 🖨️ POSTSCRIPT OUTPUT GENERATOR
    ///
    /// Generates production-grade PostScript Level 2/3 output from rendered bitmaps.
    ///
    /// Encoding: ASCII85 (most compact, universally supported by PS interpreters)
    /// Standard:  DSC 3.0 compliant
    /// Supports:  Single-page and multi-page jobs (each page = showpage)
    ///
    /// ASCII85 encoding encodes 4 bytes → 5 ASCII chars (25% overhead vs 100% for Hex).
    /// Supported by all PostScript Level 2+ interpreters.
    /// </summary>
    public static class PostScriptOutputGenerator
    {
        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Generate complete PostScript document from a single rendered page.
        /// </summary>
        public static byte[] GeneratePage(Bitmap page, int pageNumber = 1, int totalPages = 1)
        {
            var sb = new StringBuilder();

            AppendDocumentHeader(sb, totalPages);
            AppendPage(sb, page, pageNumber);
            AppendDocumentTrailer(sb);

            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        /// <summary>
        /// Generate a single PS page section (for embedding in multi-page jobs).
        /// </summary>
        public static string GeneratePageSection(Bitmap page, int pageNumber)
        {
            var sb = new StringBuilder();
            AppendPage(sb, page, pageNumber);
            return sb.ToString();
        }

        // ── DSC Document Structure ─────────────────────────────────────────

        private static void AppendDocumentHeader(StringBuilder sb, int totalPages)
        {
            sb.AppendLine("%!PS-Adobe-3.0");
            sb.AppendLine("%%Creator: Apex Printing System");
            sb.AppendLine($"%%CreationDate: {DateTime.Now:ddd MMM dd HH:mm:ss yyyy}");
            sb.AppendLine($"%%Pages: {totalPages}");
            sb.AppendLine("%%DocumentData: Clean7Bit");
            sb.AppendLine("%%LanguageLevel: 2");
            sb.AppendLine("%%EndComments");
            sb.AppendLine("%%BeginProlog");
            sb.AppendLine("%%EndProlog");
            sb.AppendLine("%%BeginSetup");
            sb.AppendLine("%%EndSetup");
        }

        private static void AppendDocumentTrailer(StringBuilder sb)
        {
            sb.AppendLine("%%Trailer");
            sb.AppendLine("%%EOF");
        }

        private static void AppendPage(StringBuilder sb, Bitmap bmp, int pageNumber)
        {
            // Page width and height in points (72 pts = 1 inch)
            // Assuming 300 DPI rendering → 1 pixel = 72/300 = 0.24 pts
            // Or use actual bitmap size scaled to letter/A4
            const float renderDpi = 300f;
            float ptWidth  = bmp.Width  * 72f / renderDpi;
            float ptHeight = bmp.Height * 72f / renderDpi;

            sb.AppendLine($"%%Page: {pageNumber} {pageNumber}");
            sb.AppendLine($"%%PageBoundingBox: 0 0 {(int)ptWidth} {(int)ptHeight}");
            sb.AppendLine("%%BeginPageSetup");
            sb.AppendLine($"<< /PageSize [{(int)ptWidth} {(int)ptHeight}] >> setpagedevice");
            sb.AppendLine("%%EndPageSetup");
            sb.AppendLine();

            // Save graphics state
            sb.AppendLine("gsave");

            // Set up coordinate system: origin bottom-left, image fills page
            sb.AppendLine($"0 0 translate");
            sb.AppendLine($"{ptWidth:F2} {ptHeight:F2} scale");

            // Encode image data in ASCII85
            var imageData = BitmapToAscii85Rgb(bmp);

            // PostScript image operator
            // Format: width height bits matrix datasource image
            sb.AppendLine($"{bmp.Width} {bmp.Height} 8");
            sb.AppendLine($"[{bmp.Width} 0 0 -{bmp.Height} 0 {bmp.Height}]");
            sb.AppendLine("currentfile");
            sb.AppendLine("/ASCII85Decode filter");
            sb.AppendLine("false 3");        // false = multi-channel, 3 = RGB components
            sb.AppendLine("colorimage");

            // ASCII85 image data
            sb.Append(imageData);
            sb.AppendLine();

            // Restore graphics state
            sb.AppendLine("grestore");
            sb.AppendLine("showpage");
            sb.AppendLine("%%PageTrailer");
            sb.AppendLine();
        }

        // ── ASCII85 Encoding ──────────────────────────────────────────────

        /// <summary>
        /// Encode a bitmap as ASCII85-encoded RGB data for PostScript colorimage operator.
        /// ASCII85 encodes 4 bytes as 5 printable ASCII chars (base-85).
        /// </summary>
        private static string BitmapToAscii85Rgb(Bitmap bmp)
        {
            // Extract raw RGB bytes
            var rgbData = ExtractRgbBytes(bmp);

            // Encode to ASCII85
            var encoded = EncodeAscii85(rgbData);

            // Wrap at 76 chars per line (DSC compliant)
            return WrapLines(encoded, 76);
        }

        private static byte[] ExtractRgbBytes(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                int stride    = data.Stride;
                int width     = bmp.Width;
                int height    = bmp.Height;
                var rgbBytes  = new byte[width * height * 3];

                unsafe
                {
                    byte* ptr = (byte*)data.Scan0;
                    int   dst = 0;

                    for (int y = 0; y < height; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            // BitmapData is BGR — convert to RGB for PostScript
                            rgbBytes[dst++] = row[x * 3 + 2]; // R
                            rgbBytes[dst++] = row[x * 3 + 1]; // G
                            rgbBytes[dst++] = row[x * 3 + 0]; // B
                        }
                    }
                }

                return rgbBytes;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        /// <summary>
        /// Encode bytes using ASCII85 (base-85) encoding per RFC 1924 / PostScript spec.
        /// Groups of 4 bytes → 5 ASCII chars in range '!' (33) to 'u' (117).
        /// All-zero group → 'z' (special case).
        /// End marker: '~>'
        /// </summary>
        private static string EncodeAscii85(byte[] data)
        {
            var sb     = new StringBuilder((int)(data.Length * 1.25) + 10);
            int length = data.Length;
            int i      = 0;

            while (i < length)
            {
                // Read up to 4 bytes
                int remaining = Math.Min(4, length - i);
                uint group    = 0;

                for (int j = 0; j < 4; j++)
                {
                    group <<= 8;
                    if (j < remaining)
                        group |= data[i + j];
                }

                if (remaining == 4 && group == 0)
                {
                    // Special case: all-zero group encodes as 'z'
                    sb.Append('z');
                }
                else
                {
                    // Encode 4 bytes as 5 chars
                    char[] chars = new char[5];
                    for (int j = 4; j >= 0; j--)
                    {
                        chars[j] = (char)('!' + (group % 85));
                        group    /= 85;
                    }

                    // Only emit as many chars as needed for partial final group
                    int charsToEmit = remaining + 1;
                    for (int j = 0; j < charsToEmit; j++)
                        sb.Append(chars[j]);
                }

                i += remaining;
            }

            // End-of-data marker
            sb.Append("~>");

            return sb.ToString();
        }

        private static string WrapLines(string text, int lineLength)
        {
            var sb = new StringBuilder(text.Length + text.Length / lineLength + 10);
            int i  = 0;

            while (i < text.Length)
            {
                int count = Math.Min(lineLength, text.Length - i);
                sb.AppendLine(text.Substring(i, count));
                i += count;
            }

            return sb.ToString();
        }
    }
}
