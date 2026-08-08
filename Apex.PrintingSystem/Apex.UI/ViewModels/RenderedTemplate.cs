using Apex.Core.Utilities;
using Apex.Services.Templates;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Apex.UI.ViewModels
{
    // ── Error states ──────────────────────────────────────────────────────────
    public enum FieldRenderError
    {
        None,
        NotMapped,      // field has no column binding
        MissingValue,   // bound but value empty and field is Required
        MissingImage,   // image type, but image file not found
        InvalidFormat   // format string caused a parse exception
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// A fully-resolved slot ready to paint on the template preview canvas.
    ///
    /// All geometry is stored in mm (same unit as <see cref="TemplateSlotDefinition"/>).
    /// The *Dip computed properties convert to WPF device-independent units
    /// (mm × 96/25.4) so they can bind directly to Canvas.Left / Canvas.Top /
    /// Width / Height on a ContentPresenter in an ItemsControl. PDF geometry uses
    /// points instead — see <see cref="UnitConverter"/>.
    ///
    /// WPF-typed style properties (TextBrush, WpfFontWeight, …) are also
    /// computed here — keeping the DataTemplate free of converter clutter while
    /// keeping WPF types confined to the UI project.
    /// </summary>
    public class RenderedFieldItem
    {
        /// <summary>Minimum on-screen size so a tiny slot stays visible (≈4.2 mm).</summary>
        private const double MinSizeDip = 16;

        // ── Geometry (mm) ─────────────────────────────────────────────────────
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double RotationDegrees { get; set; }
        public int ZIndex { get; set; }

        // WPF canvas coordinates in DIPs (1/96") — bound by ItemContainerStyle and
        // used when drawing into a DrawingVisual. NEVER use these as PDF points.
        public double LeftDip => UnitConverter.MmToDips(X);
        public double TopDip => UnitConverter.MmToDips(Y);
        public double WidthDip => Math.Max(UnitConverter.MmToDips(Width), MinSizeDip);
        public double HeightDip => Math.Max(UnitConverter.MmToDips(Height), MinSizeDip);

        // ── Identity ──────────────────────────────────────────────────────────
        public string FieldId { get; set; } = "";
        public SlotDataType FieldType { get; set; }

        // ── Content ───────────────────────────────────────────────────────────
        /// <summary>Resolved text value (text/number/date/counter/qr-value/barcode-value).</summary>
        public string RenderedText { get; set; } = "";
        /// <summary>Loaded bitmap for Image-type slots (null when image not found).</summary>
        public BitmapSource? ImageSource { get; set; }
        public string ImageFitMode { get; set; } = "Contain";

        // ── Error state ───────────────────────────────────────────────────────
        public FieldRenderError Error { get; set; } = FieldRenderError.None;
        public bool IsError => Error != FieldRenderError.None;

        public string ErrorMessage => Error switch
        {
            FieldRenderError.NotMapped => "[غير مربوط]",
            FieldRenderError.MissingValue => "[قيمة ناقصة]",
            FieldRenderError.MissingImage => "[صورة ناقصة]",
            FieldRenderError.InvalidFormat => "[تنسيق خاطئ]",
            _ => ""
        };

        // ── Field-type flags (for XAML DataTriggers, no converter needed) ─────
        public bool IsTextField => FieldType is SlotDataType.Text
                                                or SlotDataType.Number
                                                or SlotDataType.Date
                                                or SlotDataType.Counter;
        public bool IsImageField => FieldType == SlotDataType.Image;
        public bool IsQrField => FieldType == SlotDataType.QrCode;
        public bool IsBarcodeField => FieldType == SlotDataType.Barcode;
        public bool HasImage => ImageSource != null;

        // ── Text styling ──────────────────────────────────────────────────────
        public string FontFamily { get; set; } = "Tahoma";
        public double FontSize { get; set; } = 12;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string TextColor { get; set; } = "#000000";
        public HorizontalAlign TextAlign { get; set; } = HorizontalAlign.Right;
        public bool IsRtl { get; set; } = true;

        // WPF-ready computed properties — no converters needed in XAML.
        // The entire RenderedTemplate is replaced on each record navigation so
        // these do not need to raise PropertyChanged.
        public FontWeight WpfFontWeight => Bold ? FontWeights.Bold : FontWeights.Normal;
        public FontStyle WpfFontStyle => Italic ? FontStyles.Italic : FontStyles.Normal;

        public TextAlignment WpfTextAlignment => TextAlign switch
        {
            HorizontalAlign.Left => TextAlignment.Left,
            HorizontalAlign.Center => TextAlignment.Center,
            HorizontalAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Right
        };

        public FlowDirection WpfFlowDirection =>
            IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        /// <summary>
        /// A colour this close to black on every channel is treated as "meant to be
        /// black" and snapped to pure #000000 — see <see cref="TextBrush"/>.
        /// </summary>
        private const byte NearBlackThreshold = 24;

        /// <summary>
        /// Text colour as a WPF SolidColorBrush (hex string → Brush).
        ///
        /// Near-black values are snapped to PURE black. On press, an off-black such
        /// as #1A1A1A separates into a four-colour "rich black", so small text is
        /// printed with all four inks and any misregistration shows as coloured
        /// fringing. Pure black is what a RIP maps to K-only.
        ///
        /// NOTE: this is a mitigation, not true K100 — that requires CMYK output,
        /// which this raster path cannot express. It removes the common failure
        /// (designer picked a near-black swatch) but does not replace colour
        /// management.
        /// </summary>
        public SolidColorBrush TextBrush
        {
            get
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(TextColor);
                    if (c.R <= NearBlackThreshold && c.G <= NearBlackThreshold && c.B <= NearBlackThreshold)
                        c = Color.FromArgb(c.A, 0, 0, 0);
                    return new SolidColorBrush(c);
                }
                catch { return Brushes.Black; }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// A fully-rendered page (background + all resolved fields) for one data record.
    /// Replaced as a whole by <see cref="Services.TemplateRenderingService"/> each
    /// time the user navigates to a different record.
    /// </summary>
    public class RenderedTemplate
    {
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }

        /// <summary>Page size in WPF DIPs (1/96") — for on-screen layout and raster export.</summary>
        public double WidthDip => UnitConverter.MmToDips(WidthMm);
        public double HeightDip => UnitConverter.MmToDips(HeightMm);

        /// <summary>Page size in PDF points (1/72") — for <c>SKDocument.BeginPage</c>.</summary>
        public double WidthPoints => UnitConverter.MmToPoints(WidthMm);
        public double HeightPoints => UnitConverter.MmToPoints(HeightMm);
        public string BackgroundColor { get; set; } = "#FFFFFF";
        public BitmapSource? BackgroundImage { get; set; }
        public int RecordIndex { get; set; }
        public int TotalRecords { get; set; }

        /// <summary>Background as a WPF SolidColorBrush (for XAML Rectangle.Fill).</summary>
        public SolidColorBrush BackgroundBrush
        {
            get
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(BackgroundColor);
                    return new SolidColorBrush(c);
                }
                catch { return Brushes.White; }
            }
        }

        public ObservableCollection<RenderedFieldItem> Fields { get; } = new();
    }
}
