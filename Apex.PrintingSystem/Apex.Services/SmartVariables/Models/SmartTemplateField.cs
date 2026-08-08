using System;
using System.Text.Json.Serialization;

namespace Apex.Services.SmartVariables.Models
{
    public enum SmartFieldType
    {
        StaticText,
        TextVariable,
        NumberVariable,
        DateVariable,
        ImageVariable,
        QRCode,
        Barcode,
        Formula,
        AutoSerial
    }

    public enum TextOverflowMode { ShrinkToFit, Wrap, Clip, Ellipsis }
    public enum ImageFitMode { Cover, Contain, Stretch, Original }
    public enum TextDirectionMode { Rtl, Ltr, Auto }
    public enum SmartBarcodeType { QR, Code128, EAN13 }
    public enum TextHAlign { Left, Center, Right, Justify }
    public enum TextVAlign { Top, Middle, Center, Bottom }

    public class SmartTemplateField
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string TemplateId { get; set; } = "";
        public string Label { get; set; } = "";
        public SmartFieldType FieldType { get; set; } = SmartFieldType.TextVariable;

        // ── Variable binding ──────────────────────────────────────────────────
        public string VariableKey { get; set; } = "";
        public string? DefaultValue { get; set; }
        public bool IsRequired { get; set; }

        // ── Layout ────────────────────────────────────────────────────────────
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 50;
        public double Height { get; set; } = 15;
        public int Rotation { get; set; }
        public int ZIndex { get; set; }
        public bool IsLocked { get; set; }
        public bool IsVisible { get; set; } = true;
        public double Opacity { get; set; } = 1.0;

        // ── Appearance ────────────────────────────────────────────────────────
        public string BackgroundColor { get; set; } = "Transparent";
        public string BorderColor { get; set; } = "Transparent";
        public double BorderThickness { get; set; }
        public double BorderRadius { get; set; }
        public double PaddingLeft { get; set; } = 2;
        public double PaddingTop { get; set; } = 2;
        public double PaddingRight { get; set; } = 2;
        public double PaddingBottom { get; set; } = 2;

        // ── Text properties ───────────────────────────────────────────────────
        public string FontFamily { get; set; } = "Tahoma";
        public double FontSize { get; set; } = 12;
        public double MinFontSize { get; set; } = 6;
        public double MaxFontSize { get; set; } = 72;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string TextColor { get; set; } = "#000000";
        public TextHAlign TextAlign { get; set; } = TextHAlign.Right;
        public TextVAlign VerticalAlign { get; set; } = TextVAlign.Middle;
        public TextDirectionMode Direction { get; set; } = TextDirectionMode.Auto;
        public bool AutoFit { get; set; } = true;
        public TextOverflowMode OverflowMode { get; set; } = TextOverflowMode.ShrinkToFit;
        public double LineHeight { get; set; } = 1.2;
        public double LetterSpacing { get; set; }
        public string? DateFormat { get; set; } = "dd/MM/yyyy";

        // ── Image properties ──────────────────────────────────────────────────
        public ImageFitMode ImageFit { get; set; } = ImageFitMode.Contain;
        public string? FallbackImagePath { get; set; }
        public bool ClipToShape { get; set; }
        public double ImageBorderRadius { get; set; }

        // ── QR / Barcode ──────────────────────────────────────────────────────
        public string? CodeSourceVariable { get; set; }
        public SmartBarcodeType CodeType { get; set; } = SmartBarcodeType.QR;
        public string ErrorCorrectionLevel { get; set; } = "M";
        public bool ShowTextBelowBarcode { get; set; }

        // ── AutoSerial ────────────────────────────────────────────────────────
        public int SerialStart { get; set; } = 1;
        public int SerialStep { get; set; } = 1;
        public string SerialPrefix { get; set; } = "";
        public string SerialSuffix { get; set; } = "";
        public int SerialPadding { get; set; } = 4;

        // ── Formula ───────────────────────────────────────────────────────────
        public string? FormulaTemplate { get; set; }  // "{{name}} - {{class}}"

        // ── Computed helpers ─────────────────────────────────────────────────
        [JsonIgnore]
        public bool IsVariable => FieldType is
            SmartFieldType.TextVariable or SmartFieldType.NumberVariable or
            SmartFieldType.DateVariable or SmartFieldType.ImageVariable or
            SmartFieldType.QRCode or SmartFieldType.Barcode or
            SmartFieldType.Formula;

        [JsonIgnore]
        public string FieldTypeLabel => FieldType switch
        {
            SmartFieldType.StaticText => "نص ثابت",
            SmartFieldType.TextVariable => "متغير نصي",
            SmartFieldType.NumberVariable => "رقم / كود",
            SmartFieldType.DateVariable => "تاريخ",
            SmartFieldType.ImageVariable => "صورة",
            SmartFieldType.QRCode => "QR كود",
            SmartFieldType.Barcode => "باركود",
            SmartFieldType.Formula => "صيغة",
            SmartFieldType.AutoSerial => "مسلسل تلقائي",
            _ => "غير معروف"
        };

        [JsonIgnore]
        public string TypeIcon => FieldType switch
        {
            SmartFieldType.StaticText => "T",
            SmartFieldType.TextVariable => "T",
            SmartFieldType.NumberVariable => "#",
            SmartFieldType.DateVariable => "D",
            SmartFieldType.ImageVariable => "I",
            SmartFieldType.QRCode => "Q",
            SmartFieldType.Barcode => "B",
            SmartFieldType.Formula => "F",
            SmartFieldType.AutoSerial => "S",
            _ => "?"
        };
    }
}
