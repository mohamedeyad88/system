using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Apex.Services.Templates;
using Apex.UI.Rendering;
using Apex.UI.ViewModels;

namespace Apex.UI.Tests;

/// <summary>
/// Verifies that ONE painting algorithm drives both preview and export.
///
/// These use a recording <see cref="IRenderTarget"/> so the drawing commands can be
/// asserted without a display. The regressions being guarded against are the ones
/// that made preview and output disagree: images fitted differently, fields spilling
/// outside their slot, and error styling diverging between the two paths.
/// </summary>
public class TemplatePainterTests
{
    // ── Recording target ──────────────────────────────────────────────────────
    private sealed record Cmd(string Op, Rect Rect, string? Text = null, ImageFit? Fit = null);

    private sealed class RecordingTarget : IRenderTarget
    {
        public List<Cmd> Commands { get; } = new();
        public int Depth { get; private set; }
        public int MaxDepth { get; private set; }

        public void FillRect(Rect rect, Brush brush) => Commands.Add(new Cmd("fill", rect));
        public void StrokeRect(Rect rect, Brush stroke, double thickness) => Commands.Add(new Cmd("stroke", rect));
        public void DrawImage(BitmapSource image, Rect rect, ImageFit fit) => Commands.Add(new Cmd("image", rect, Fit: fit));
        public void DrawText(string text, Rect rect, TextStyle style) => Commands.Add(new Cmd("text", rect, text));
        public void PushClip(Rect rect)
        {
            Commands.Add(new Cmd("clip", rect));
            Depth++;
            MaxDepth = Math.Max(MaxDepth, Depth);
        }
        public void Pop()
        {
            Commands.Add(new Cmd("pop", default));
            Depth--;
        }
    }

    private static RenderedTemplate Template(params RenderedFieldItem[] fields)
    {
        var t = new RenderedTemplate { WidthMm = 210, HeightMm = 297 };
        foreach (var f in fields) t.Fields.Add(f);
        return t;
    }

    private static BitmapSource Pixel() =>
        BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PaintsPageBackgroundAtFullPageSizeInDips()
    {
        var t = Template();
        var target = new RecordingTarget();

        TemplatePainter.Paint(t, target);

        var fill = Assert.Single(target.Commands.Where(c => c.Op == "fill"));
        Assert.Equal(t.WidthDip, fill.Rect.Width, 6);
        Assert.Equal(t.HeightDip, fill.Rect.Height, 6);
    }

    [Fact]
    public void EveryFieldIsClippedToItsOwnSlot()
    {
        var t = Template(
            new RenderedFieldItem { FieldType = SlotDataType.Text, RenderedText = "a", X = 10, Y = 10, Width = 50, Height = 20 },
            new RenderedFieldItem { FieldType = SlotDataType.Text, RenderedText = "b", X = 10, Y = 40, Width = 50, Height = 20 });
        var target = new RecordingTarget();

        TemplatePainter.Paint(t, target);

        Assert.Equal(2, target.Commands.Count(c => c.Op == "clip"));
        Assert.Equal(2, target.Commands.Count(c => c.Op == "pop"));
        Assert.Equal(0, target.Depth);       // balanced
        Assert.Equal(1, target.MaxDepth);    // never nested
    }

    [Fact]
    public void ImageSlot_HonoursItsFitMode_NotAHardcodedStretch()
    {
        var t = Template(new RenderedFieldItem
        {
            FieldType = SlotDataType.Image,
            ImageSource = Pixel(),
            ImageFitMode = "Cover",
            X = 0, Y = 0, Width = 40, Height = 40,
        });
        var target = new RecordingTarget();

        TemplatePainter.Paint(t, target);

        var img = Assert.Single(target.Commands.Where(c => c.Op == "image"));
        Assert.Equal(ImageFit.Cover, img.Fit);
    }

    [Theory]
    [InlineData("Contain", ImageFit.Contain)]
    [InlineData("Cover", ImageFit.Cover)]
    [InlineData("Stretch", ImageFit.Stretch)]
    [InlineData("Fill", ImageFit.Stretch)]
    [InlineData("", ImageFit.Contain)]
    [InlineData(null, ImageFit.Contain)]
    public void FitModeParsing(string? mode, ImageFit expected)
    {
        var t = Template(new RenderedFieldItem
        {
            FieldType = SlotDataType.Image,
            ImageSource = Pixel(),
            ImageFitMode = mode!,
            Width = 10, Height = 10,
        });
        var target = new RecordingTarget();

        TemplatePainter.Paint(t, target);

        Assert.Equal(expected, target.Commands.Single(c => c.Op == "image").Fit);
    }

    [Fact]
    public void EncodedQrPaintsAsImage_NotAsText()
    {
        var t = Template(new RenderedFieldItem
        {
            FieldType = SlotDataType.QrCode,
            ImageSource = Pixel(),          // successfully encoded
            RenderedText = "PAYLOAD",
            Width = 30, Height = 30,
        });
        var target = new RecordingTarget();

        TemplatePainter.Paint(t, target);

        Assert.Contains(target.Commands, c => c.Op == "image");
        Assert.DoesNotContain(target.Commands, c => c.Op == "text");
    }

    [Fact]
    public void UnencodableQr_FallsBackToAReadablePlate()
    {
        var t = Template(new RenderedFieldItem
        {
            FieldType = SlotDataType.QrCode,
            ImageSource = null,             // encoding failed
            RenderedText = "BAD-VALUE",
            Width = 30, Height = 30,
        });
        var target = new RecordingTarget();

        TemplatePainter.Paint(t, target);

        Assert.Contains(target.Commands, c => c.Op == "text" && c.Text == "BAD-VALUE");
    }

    [Fact]
    public void ErrorField_DrawsPlateBorderAndMessage()
    {
        var t = Template(new RenderedFieldItem
        {
            FieldType = SlotDataType.Text,
            Error = FieldRenderError.NotMapped,
            Width = 40, Height = 10,
        });
        var target = new RecordingTarget();

        TemplatePainter.Paint(t, target);

        Assert.Contains(target.Commands, c => c.Op == "stroke");
        Assert.Contains(target.Commands, c => c.Op == "text" && c.Text == "[غير مربوط]");
        // An errored field must not also paint its (stale) image.
        Assert.DoesNotContain(target.Commands, c => c.Op == "image");
    }

    [Fact]
    public void FieldsArePaintedInOrder()
    {
        var t = Template(
            new RenderedFieldItem { FieldType = SlotDataType.Text, RenderedText = "first", Width = 10, Height = 10 },
            new RenderedFieldItem { FieldType = SlotDataType.Text, RenderedText = "second", Width = 10, Height = 10 });
        var target = new RecordingTarget();

        TemplatePainter.Paint(t, target);

        var texts = target.Commands.Where(c => c.Op == "text").Select(c => c.Text).ToList();
        Assert.Equal(new[] { "first", "second" }, texts);
    }

    [Fact]
    public void NullTemplateOrTarget_DoesNotThrow()
    {
        TemplatePainter.Paint(null!, new RecordingTarget());
        TemplatePainter.Paint(Template(), null!);
    }
}
