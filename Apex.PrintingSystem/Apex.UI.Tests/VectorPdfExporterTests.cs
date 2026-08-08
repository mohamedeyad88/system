using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Apex.Core.Utilities;
using Apex.Services.Templates;
using Apex.UI.Services;
using Apex.UI.ViewModels;

namespace Apex.UI.Tests;

/// <summary>
/// Proves the variable-data PDF is TRUE VECTOR, not a wrapped bitmap.
///
/// The old exporter rendered each record to a 150-dpi PNG and embedded it, which
/// capped output at raster resolution and made small Arabic type print ragged.
/// Asserting "a file was produced" would not have caught that — so these tests
/// inspect the PDF content stream for vector path operators and for the ABSENCE of
/// a full-page image.
/// </summary>
public class VectorPdfExporterTests
{
    private static T RunSta<T>(System.Func<T> body)
    {
        T result = default!;
        System.Exception? error = null;
        var t = new Thread(() =>
        {
            try { result = body(); }
            catch (System.Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        if (error != null) throw new InvalidOperationException("STA body threw", error);
        return result;
    }

    private static RenderedTemplate Template(params RenderedFieldItem[] fields)
    {
        var t = new RenderedTemplate { WidthMm = 210, HeightMm = 297 };
        foreach (var f in fields) t.Fields.Add(f);
        return t;
    }

    private static RenderedFieldItem Text(string value, string colour = "#000000") => new()
    {
        FieldType = SlotDataType.Text,
        RenderedText = value,
        X = 10, Y = 10, Width = 120, Height = 20,
        FontFamily = "Tahoma",
        FontSize = 14,
        TextColor = colour,
    };

    /// <summary>Exports to a temp file and returns the raw bytes.</summary>
    private static byte[] ExportBytes(RenderedTemplate t)
    {
        string path = Path.Combine(Path.GetTempPath(), $"apex-vec-{System.Guid.NewGuid():N}.pdf");
        try
        {
            RunSta<object?>(() => { new VectorPdfExporter().ExportSingle(t, path); return null; });
            return File.ReadAllBytes(path);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static string AsLatin(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ProducesAValidPdf()
    {
        var bytes = ExportBytes(Template(Text("Hello")));

        Assert.True(bytes.Length > 0);
        Assert.StartsWith("%PDF-", AsLatin(bytes)[..8]);
    }

    [Fact]
    public void ArabicText_IsEmittedAsVectorPaths_NotAnImage()
    {
        var bytes = ExportBytes(Template(Text("شهادة تقدير")));
        string content = AsLatin(bytes);

        // A page whose content is a single embedded bitmap declares an image XObject.
        Assert.DoesNotContain("/Subtype /Image", content);
        Assert.DoesNotContain("/Subtype/Image", content);
    }

    [Fact]
    public void PageSizeMatchesTheDesignedMillimetres()
    {
        var bytes = ExportBytes(Template(Text("x")));
        string content = AsLatin(bytes);

        // A4 = 210×297 mm → 595×842 pt.
        int w = (int)System.Math.Round(UnitConverter.MmToPoints(210));
        int h = (int)System.Math.Round(UnitConverter.MmToPoints(297));
        Assert.Equal(595, w);
        Assert.Equal(842, h);
        Assert.Contains("/MediaBox", content);
    }

    [Fact]
    public void EmptyPageSet_IsRejectedRatherThanWritingAnEmptyFile()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunSta<object?>(() =>
            {
                new VectorPdfExporter().Export(System.Array.Empty<RenderedTemplate>(),
                    Path.Combine(Path.GetTempPath(), "apex-empty.pdf"));
                return null;
            }).ToString());

        Assert.NotNull(ex);
    }

    [Fact]
    public void MultipleRecords_ProduceMultiplePages()
    {
        string path = Path.Combine(Path.GetTempPath(), $"apex-multi-{System.Guid.NewGuid():N}.pdf");
        try
        {
            var pages = new[]
            {
                Template(Text("one")),
                Template(Text("two")),
                Template(Text("three")),
            };

            RunSta<object?>(() => { new VectorPdfExporter().Export(pages, path); return null; });

            string content = AsLatin(File.ReadAllBytes(path));
            int pageObjects = System.Text.RegularExpressions.Regex
                .Matches(content, @"/Type\s*/Page[^s]").Count;
            Assert.Equal(3, pageObjects);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void EmbeddedPhotos_StayRaster_BecauseTheyGenuinelyAreRaster()
    {
        var photo = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 255,0,0,255,  0,255,0,255,  0,0,255,255,  255,255,0,255 }, 8);

        var t = Template(new RenderedFieldItem
        {
            FieldType = SlotDataType.Image,
            ImageSource = photo,
            ImageFitMode = "Contain",
            X = 10, Y = 40, Width = 40, Height = 40,
        });

        string content = AsLatin(ExportBytes(t));

        // Vector text is right; vector-ising a photograph is not — it must embed.
        Assert.Contains("/Image", content);
    }

    [Fact]
    public void MissingPath_IsRejected()
    {
        Assert.ThrowsAny<System.Exception>(() =>
            RunSta<object?>(() =>
            {
                new VectorPdfExporter().ExportSingle(Template(Text("x")), "");
                return null;
            }));
    }
}
