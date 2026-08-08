using System.Drawing;
using Apex.Services.Templates;

namespace Apex.Services.Tests.Templates;

/// <summary>
/// Verifies <see cref="CodeRenderer.TryRenderPng"/> — the string-symbology API the
/// UI layer uses to render QR/barcode slots. The point of these tests is not that
/// "some bytes came back", but that the produced symbol is genuinely SCANNABLE:
/// every case encodes and then decodes the PNG back to the original content.
/// </summary>
public class CodeRendererPngTests
{
    private static string? DecodePng(byte[] png)
    {
        using var ms = new MemoryStream(png);
        using var bmp = new Bitmap(ms);
        return CodeRenderer.TryDecode(bmp);
    }

    [Theory]
    [InlineData("QR", "https://apexprint.me/verify/12345")]
    [InlineData("QRCODE", "ABC-XYZ-0099")]
    [InlineData("QR_CODE", "1234567890")]
    public void Qr_RoundTrips(string symbology, string content)
    {
        byte[]? png = CodeRenderer.TryRenderPng(content, symbology, 300, 300);

        Assert.NotNull(png);
        Assert.Equal(content, DecodePng(png!));
    }

    [Fact]
    public void Code128_RoundTrips()
    {
        const string content = "APX-00125";
        byte[]? png = CodeRenderer.TryRenderPng(content, "CODE128", 400, 120);

        Assert.NotNull(png);
        Assert.Equal(content, DecodePng(png!));
    }

    [Fact]
    public void Ean13_RoundTrips()
    {
        // 12 digits + checksum; ZXing appends/validates the 13th digit.
        const string content = "5901234123457";
        byte[]? png = CodeRenderer.TryRenderPng(content, "EAN13", 400, 160);

        Assert.NotNull(png);
        Assert.Equal(content, DecodePng(png!));
    }

    [Fact]
    public void UnknownSymbology_FallsBackToCode128AndStillScans()
    {
        const string content = "FALLBACK-1";
        byte[]? png = CodeRenderer.TryRenderPng(content, "something-unsupported", 400, 120);

        Assert.NotNull(png);
        Assert.Equal(content, DecodePng(png!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankContent_ReturnsNull_SoCallerCanFallBackToText(string? content)
    {
        Assert.Null(CodeRenderer.TryRenderPng(content!, "QR", 200, 200));
    }

    [Fact]
    public void InvalidContentForSymbology_ReturnsNull_SoCallerCanFallBackToText()
    {
        // EAN-13 is numeric-only: letters must fail rather than emit a bogus symbol.
        Assert.Null(CodeRenderer.TryRenderPng("NOT-NUMERIC", "EAN13", 400, 160));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void NonPositiveSize_ReturnsNull(int w, int h)
    {
        Assert.Null(CodeRenderer.TryRenderPng("DATA", "QR", w, h));
    }
}
