using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace Apex.Services.Templates
{
    /// <summary>
    /// Encodes QR codes and 1-D barcodes (Code128 / EAN-13) into a
    /// <see cref="Bitmap"/> so template slots can print real, scannable codes
    /// instead of the raw text value. Returns <c>null</c> when the content
    /// cannot be encoded (e.g. EAN-13 given non-numeric data) so callers can
    /// fall back to rendering the value as text.
    /// </summary>
    public static class CodeRenderer
    {
        public static BarcodeFormat ResolveBarcodeFormat(string? name) => (name ?? "").Trim().ToUpperInvariant() switch
        {
            "EAN13" or "EAN-13" or "EAN" => BarcodeFormat.EAN_13,
            "CODE39" or "CODE-39" => BarcodeFormat.CODE_39,
            "QR" or "QRCODE" or "QR_CODE" => BarcodeFormat.QR_CODE,
            _ => BarcodeFormat.CODE_128,
        };

        /// <summary>Encode <paramref name="content"/> into a bitmap of the requested pixel size.</summary>
        public static Bitmap? TryRender(string content, BarcodeFormat format, int widthPx, int heightPx)
        {
            if (string.IsNullOrWhiteSpace(content) || widthPx <= 0 || heightPx <= 0) return null;

            try
            {
                var writer = new BarcodeWriterPixelData
                {
                    Format = format,
                    Options = new EncodingOptions
                    {
                        Width = widthPx,
                        Height = heightPx,
                        Margin = format == BarcodeFormat.QR_CODE ? 1 : 4,
                        PureBarcode = true,
                    },
                };

                PixelData pixels = writer.Write(content);

                var bmp = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppRgb);
                BitmapData bmpData = bmp.LockBits(
                    new Rectangle(0, 0, pixels.Width, pixels.Height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    Marshal.Copy(pixels.Pixels, 0, bmpData.Scan0, pixels.Pixels.Length);
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }
                return bmp;
            }
            catch
            {
                // Invalid content for the chosen symbology → let caller fall back to text.
                return null;
            }
        }

        /// <summary>
        /// Encodes <paramref name="content"/> and returns the symbol as PNG bytes.
        /// The symbology is named by string ("QR", "CODE128", "EAN13", …) so callers
        /// in the UI layer can request a real, scannable code without taking a
        /// dependency on ZXing types. Returns <c>null</c> when the content cannot be
        /// encoded, so the caller can fall back to drawing the raw value as text.
        /// </summary>
        public static byte[]? TryRenderPng(string content, string? symbology, int widthPx, int heightPx)
        {
            using Bitmap? bmp = TryRender(content, ResolveBarcodeFormat(symbology), widthPx, heightPx);
            if (bmp == null) return null;

            try
            {
                using var ms = new System.IO.MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Decode the first code found in <paramref name="bitmap"/> (used by tests/verification).</summary>
        public static string? TryDecode(Bitmap bitmap)
        {
            if (bitmap == null) return null;
            try
            {
                int w = bitmap.Width, h = bitmap.Height;
                using var clone = bitmap.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppRgb);
                BitmapData data = clone.LockBits(new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                byte[] buffer = new byte[data.Stride * h];
                try { Marshal.Copy(data.Scan0, buffer, 0, buffer.Length); }
                finally { clone.UnlockBits(data); }

                var source = new RGBLuminanceSource(buffer, w, h, RGBLuminanceSource.BitmapFormat.BGRA32);
                var reader = new BarcodeReaderGeneric
                {
                    AutoRotate = true,
                    Options = new DecodingOptions { TryHarder = true },
                };
                return reader.Decode(source)?.Text;
            }
            catch
            {
                return null;
            }
        }
    }
}
