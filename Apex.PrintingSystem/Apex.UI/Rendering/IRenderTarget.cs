using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Apex.UI.Rendering
{
    /// <summary>How an image is fitted into its slot rectangle.</summary>
    public enum ImageFit
    {
        /// <summary>Scale to fit entirely inside the slot, preserving aspect ratio.</summary>
        Contain,
        /// <summary>Scale to cover the slot, preserving aspect ratio (overflow is clipped).</summary>
        Cover,
        /// <summary>Stretch to exactly fill the slot, ignoring aspect ratio.</summary>
        Stretch,
    }

    /// <summary>Everything the painter needs to lay out one run of text.</summary>
    public readonly struct TextStyle
    {
        public string FontFamily { get; init; }
        public double FontSize { get; init; }
        public FontWeight Weight { get; init; }
        public FontStyle Style { get; init; }
        public Brush Foreground { get; init; }
        public TextAlignment Alignment { get; init; }
        public FlowDirection FlowDirection { get; init; }
    }

    /// <summary>
    /// A surface the <see cref="TemplatePainter"/> can draw a template onto.
    ///
    /// This exists so the on-screen preview and the exported file are produced by
    /// ONE painting algorithm instead of two that drift apart (the preview used to
    /// be a XAML DataTemplate while export used a DrawingContext, which is how the
    /// two ended up disagreeing on image fitting and error styling).
    ///
    /// Coordinates are always WPF DIPs (see <c>UnitConverter</c>).
    /// </summary>
    public interface IRenderTarget
    {
        void FillRect(Rect rect, Brush brush);

        void StrokeRect(Rect rect, Brush stroke, double thickness);

        void DrawImage(BitmapSource image, Rect rect, ImageFit fit);

        void DrawText(string text, Rect rect, TextStyle style);

        /// <summary>Clip subsequent drawing to <paramref name="rect"/> until <see cref="Pop"/>.</summary>
        void PushClip(Rect rect);

        void Pop();
    }
}
