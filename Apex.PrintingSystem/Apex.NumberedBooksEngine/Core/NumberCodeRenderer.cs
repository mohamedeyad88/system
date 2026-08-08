using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>What a numbering slot prints.</summary>
    public enum SlotKind
    {
        /// <summary>The number as text (the default).</summary>
        Text,

        /// <summary>A 1-D barcode carrying the same number.</summary>
        Barcode,

        /// <summary>A QR code carrying the same number.</summary>
        QrCode
    }

    /// <summary>
    /// Renders the sequence number as a scannable symbol.
    ///
    /// The number and its barcode MUST come from the same value: a book whose
    /// printed digits and scanned code disagree is worse than one with no barcode,
    /// because downstream systems trust the scan. So the caller passes the already
    /// formatted string and this only encodes it.
    /// </summary>
    public static class NumberCodeRenderer
    {
        public static BarcodeFormat ResolveFormat(SlotKind kind, string? barcodeType)
        {
            if (kind == SlotKind.QrCode) return BarcodeFormat.QR_CODE;

            return (barcodeType ?? "").Trim().ToUpperInvariant() switch
            {
                "EAN13" or "EAN-13" or "EAN" => BarcodeFormat.EAN_13,
                "CODE39" or "CODE-39" => BarcodeFormat.CODE_39,
                "QR" or "QRCODE" or "QR_CODE" => BarcodeFormat.QR_CODE,
                _ => BarcodeFormat.CODE_128,
            };
        }

        /// <summary>
        /// Encodes <paramref name="content"/> into an <see cref="SKImage"/> sized in
        /// pixels. Returns null when the content cannot be encoded (for example
        /// EAN-13 given a prefix), so the caller can fall back to printing the number
        /// as text rather than leaving an empty box on the sheet.
        /// </summary>
        public static SKImage? TryRender(string content, BarcodeFormat format, int widthPx, int heightPx)
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

                var info = new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var bmp = new SKBitmap(info);

                System.Runtime.InteropServices.Marshal.Copy(
                    pixels.Pixels, 0, bmp.GetPixels(), pixels.Pixels.Length);

                return SKImage.FromBitmap(bmp.Copy());
            }
            catch
            {
                // Invalid content for the chosen symbology → caller falls back to text.
                return null;
            }
        }
    }
}
