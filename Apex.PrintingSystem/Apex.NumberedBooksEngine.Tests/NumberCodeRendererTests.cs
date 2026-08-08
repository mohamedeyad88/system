using Apex.NumberedBooksEngine.Core;
using Xunit;
using ZXing;

namespace Apex.NumberedBooksEngine.Tests;

/// <summary>
/// Encoding the sequence number as a scannable symbol.
///
/// The printed digits and the code must carry the SAME value — downstream systems
/// trust the scan, so a mismatch is worse than having no barcode at all. Failure to
/// encode must return null so the caller falls back to printing the number as text
/// rather than leaving an empty box on the sheet.
/// </summary>
public class NumberCodeRendererTests
{
    [Theory]
    [InlineData(SlotKind.QrCode, null, BarcodeFormat.QR_CODE)]
    [InlineData(SlotKind.Barcode, "CODE128", BarcodeFormat.CODE_128)]
    [InlineData(SlotKind.Barcode, "code39", BarcodeFormat.CODE_39)]
    [InlineData(SlotKind.Barcode, "EAN13", BarcodeFormat.EAN_13)]
    [InlineData(SlotKind.Barcode, null, BarcodeFormat.CODE_128)]      // sensible default
    [InlineData(SlotKind.Barcode, "nonsense", BarcodeFormat.CODE_128)] // unknown → default
    public void FormatResolution(SlotKind kind, string? type, BarcodeFormat expected)
    {
        Assert.Equal(expected, NumberCodeRenderer.ResolveFormat(kind, type));
    }

    [Fact]
    public void QrKind_IgnoresTheBarcodeType()
    {
        // A QR slot must stay QR even if a 1-D symbology was left selected.
        Assert.Equal(BarcodeFormat.QR_CODE,
            NumberCodeRenderer.ResolveFormat(SlotKind.QrCode, "EAN13"));
    }

    [Theory]
    [InlineData("000123")]
    [InlineData("INV-000123")]
    public void Code128_EncodesToAnImage(string content)
    {
        using var img = NumberCodeRenderer.TryRender(content, BarcodeFormat.CODE_128, 300, 90);

        Assert.NotNull(img);
        Assert.True(img!.Width > 0 && img.Height > 0);
    }

    [Fact]
    public void Qr_EncodesToAnImage()
    {
        using var img = NumberCodeRenderer.TryRender("000123", BarcodeFormat.QR_CODE, 200, 200);
        Assert.NotNull(img);
    }

    [Fact]
    public void InvalidContentForSymbology_ReturnsNullSoTheCallerPrintsText()
    {
        // EAN-13 is numeric-only: a prefixed number cannot be encoded, and emitting
        // a wrong-but-scannable symbol would be far worse than falling back.
        Assert.Null(NumberCodeRenderer.TryRender("INV-000123", BarcodeFormat.EAN_13, 300, 90));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankContent_ReturnsNull(string? content)
    {
        Assert.Null(NumberCodeRenderer.TryRender(content!, BarcodeFormat.CODE_128, 200, 60));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(200, 0)]
    [InlineData(-10, 60)]
    public void NonPositiveSize_ReturnsNull(int w, int h)
    {
        Assert.Null(NumberCodeRenderer.TryRender("000123", BarcodeFormat.CODE_128, w, h));
    }
}
