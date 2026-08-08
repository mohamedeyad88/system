using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using Apex.Services.Templates;

namespace Apex.Services.Tests.Templates;

/// <summary>
/// Real end-to-end exercise of the Template Designer render engine: builds a
/// 100×70 mm employee-badge template that uses EVERY available field type
/// (StaticText, Text, Number, Date, Image, Barcode, QrCode, Counter), loads a
/// 30-person CSV data source, and renders one PNG per person via the same
/// <see cref="BatchTemplateProcessor"/> the app uses. Output lands in
/// D:\Apex\publish\template-designer-test\output for visual inspection.
/// </summary>
public class TemplateDesignerRealDataTests
{
    private const string TestRoot = @"D:\Apex\publish\template-designer-test";
    private static string CsvPath => Path.Combine(TestRoot, "employees30.csv");
    private static string OutDir => Path.Combine(TestRoot, "output");

    [Fact]
    public void Renders_All_Field_Types_For_30_People()
    {
        // ── Arrange: load the real CSV data source ────────────────────────────
        Assert.True(File.Exists(CsvPath), $"CSV data source missing: {CsvPath}");

        List<TemplateDataRow> rows = TemplateVariableBindingSystem.LoadCsvData(
            new CsvDataSource { FilePath = CsvPath, Delimiter = ',', HasHeader = true });

        Assert.Equal(30, rows.Count);

        ApextTemplate template = BuildEmployeeBadgeTemplate();

        // Every variable referenced by a slot must exist in the data set.
        List<string> templateVars = TemplateVariableBindingSystem.ExtractVariables(template);
        List<string> dataVars = TemplateVariableBindingSystem.GetDataSetVariables(rows);
        foreach (string v in templateVars)
            Assert.Contains(v, dataVars);

        // Synthesize a distinct avatar image per person, keyed by the CSV "photo" value.
        Dictionary<string, byte[]> assets = BuildAvatarAssets(rows);

        Directory.CreateDirectory(OutDir);
        foreach (string old in Directory.GetFiles(OutDir, "*.png")) File.Delete(old);

        // ── Act: render every row through the real engine → PNG ───────────────
        var processor = BatchTemplateProcessor.Instance;
        var produced = new List<string>();

        foreach (TemplateDataRow row in rows)
        {
            List<Bitmap> pages = processor.RenderTemplate(template, row, assets, dpi: 300);
            try
            {
                Assert.Single(pages); // single-page badge

                string serial = row.Get("serial", (row.RowIndex + 1).ToString());
                string outPath = Path.Combine(OutDir, $"emp_{int.Parse(serial):00}.png");
                pages[0].Save(outPath, ImageFormat.Png);
                produced.Add(outPath);
            }
            finally
            {
                foreach (Bitmap b in pages) b.Dispose();
            }
        }

        // ── Assert: 30 non-blank PNGs on disk ─────────────────────────────────
        Assert.Equal(30, produced.Count);
        foreach (string path in produced)
        {
            Assert.True(File.Exists(path), $"missing output: {path}");
            Assert.True(new FileInfo(path).Length > 3000, $"suspiciously small render: {path}");
        }

        // ── Assert: per-field-type value resolution is correct ────────────────
        TemplateDataRow first = rows[0];
        Assert.Equal("أحمد محمود عبد الله", first.Get("name"));                       // Text
        Assert.Equal("الإنتاج", first.Get("dept"));                                    // Text
        Assert.Equal("EMP-0001", first.Get("code"));                                   // Barcode value
        Assert.Equal("https://apexprint.me/e/0001", first.Get("qr"));                  // QrCode value
        Assert.Equal("1", first.Get("serial"));                                        // Counter value

        // Number format: 7500 → grouped thousands via the slot's FormatString.
        string salary = TemplateVariableBindingSystem.RenderText("{{salary:N0}}", first);
        Assert.Equal((7500d).ToString("N0", CultureInfo.CurrentCulture), salary);

        // Date format: ISO 2019-03-12 → dd/MM/yyyy.
        string joined = TemplateVariableBindingSystem.RenderText("{{join:dd/MM/yyyy}}", first);
        Assert.Equal("12/03/2019", joined);

        // QR + Barcode are REAL, scannable codes — verify by decoding round-trip.
        using (Bitmap qr = CodeRenderer.TryRender(first.Get("qr"), ZXing.BarcodeFormat.QR_CODE, 248, 248)!)
        {
            Assert.NotNull(qr);
            Assert.Equal(first.Get("qr"), CodeRenderer.TryDecode(qr));
        }
        using (Bitmap bc = CodeRenderer.TryRender(first.Get("code"), ZXing.BarcodeFormat.CODE_128, 283, 71)!)
        {
            Assert.NotNull(bc);
            Assert.Equal(first.Get("code"), CodeRenderer.TryDecode(bc));
        }

        // Write a manifest so the run is self-documenting.
        File.WriteAllText(
            Path.Combine(OutDir, "_manifest.txt"),
            $"Rendered {produced.Count} badges from {Path.GetFileName(CsvPath)}\n" +
            $"Field types: StaticText, Text, Number({first.Get("salary")}→{salary}), " +
            $"Date({first.Get("join")}→{joined}), Image, Barcode, QrCode, Counter\n" +
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
    }

    // ── Template with every field type ────────────────────────────────────────
    private static ApextTemplate BuildEmployeeBadgeTemplate()
    {
        var page = new TemplatePageDefinition
        {
            WidthMm = 100,
            HeightMm = 70,
            Orientation = PageOrientation.Landscape,
            BackgroundColor = "#FFFFFF",
            MarginTopMm = 3, MarginBottomMm = 3, MarginLeftMm = 3, MarginRightMm = 3,
            Slots = new List<TemplateSlotDefinition>
            {
                // StaticText header (no variable → uses DefaultValue)
                Slot("العنوان", "", SlotDataType.Text, 2, 2, 96, 10,
                    def: "بطاقة موظف — شركة أبِكس للطباعة", bold: true, size: 13,
                    align: HorizontalAlign.Center, color: "#0B3D5C", bg: "#E8F1F7"),

                // Image (data-driven avatar)
                Slot("الصورة", "photo", SlotDataType.Image, 3, 14, 24, 30),

                // Text fields
                Slot("الاسم", "name", SlotDataType.Text, 29, 14, 68, 9, bold: true, size: 13),
                Slot("الوظيفة", "title", SlotDataType.Text, 29, 23, 68, 7, size: 10, color: "#333333"),
                Slot("القسم", "dept", SlotDataType.Text, 29, 30, 40, 7, size: 9, color: "#555555"),

                // Number (grouped) + Date (formatted)
                Slot("الراتب", "salary", SlotDataType.Number, 29, 38, 40, 7, size: 9,
                    fmt: "N0"),
                Slot("التعيين", "join", SlotDataType.Date, 29, 45, 40, 7, size: 9,
                    fmt: "dd/MM/yyyy", color: "#555555"),

                // Barcode + Counter (bottom-left), QrCode (bottom-right)
                Slot("الكود", "code", SlotDataType.Barcode, 3, 46, 24, 6, size: 8,
                    align: HorizontalAlign.Center),
                Slot("المسلسل", "serial", SlotDataType.Counter, 3, 53, 24, 7, size: 11,
                    bold: true, align: HorizontalAlign.Center, color: "#0B3D5C"),
                Slot("QR", "qr", SlotDataType.QrCode, 76, 45, 21, 21, size: 5,
                    align: HorizontalAlign.Center, bg: "#F2F2F2"),
            },
        };

        return new ApextTemplate
        {
            Name = "بطاقة موظف - اختبار كل الحقول",
            Category = "Badges",
            Pages = new List<TemplatePageDefinition> { page },
        };
    }

    private static TemplateSlotDefinition Slot(
        string name, string variable, SlotDataType type,
        double x, double y, double w, double h,
        string? def = null, bool bold = false, double size = 12,
        HorizontalAlign align = HorizontalAlign.Right, string color = "#000000",
        string? bg = null, string? fmt = null) => new()
    {
        Name = name,
        VariableName = variable,
        DataType = type,
        X = x, Y = y, Width = w, Height = h,
        DefaultValue = def,
        Bold = bold,
        FontSize = size,
        TextAlign = align,
        TextColor = color,
        BackgroundColor = bg ?? "Transparent",
        FormatString = fmt,
        IsRtl = true,
    };

    // ── One synthesized avatar per person (colored tile + initial) ────────────
    private static Dictionary<string, byte[]> BuildAvatarAssets(IEnumerable<TemplateDataRow> rows)
    {
        var assets = new Dictionary<string, byte[]>();
        string[] palette = { "#1F6F8B", "#C1666B", "#4E937A", "#B07C4F", "#6C5B7B", "#2E86AB" };

        foreach (TemplateDataRow row in rows)
        {
            string key = row.Get("photo");
            if (string.IsNullOrEmpty(key) || assets.ContainsKey(key)) continue;

            string name = row.Get("name", "؟");
            Color bg = ColorTranslator.FromHtml(palette[Math.Abs(name.GetHashCode()) % palette.Length]);

            using var bmp = new Bitmap(200, 250, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(bg);
                string initial = name.Length > 0 ? name.Substring(0, 1) : "؟";
                using var font = new Font("Tahoma", 96, FontStyle.Bold);
                using var brush = new SolidBrush(Color.White);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(initial, font, brush, new RectangleF(0, 0, 200, 250), sf);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            assets[key] = ms.ToArray();
        }

        return assets;
    }
}
