using System.Windows;
using System.Windows.Media;
using Apex.UI.Rendering;
using Apex.UI.ViewModels;

namespace Apex.UI.Controls
{
    /// <summary>
    /// Renders a <see cref="RenderedTemplate"/> using <see cref="TemplatePainter"/> —
    /// the exact same code path the PNG/PDF export uses, so the preview is a true
    /// WYSIWYG of the output rather than a look-alike built from XAML templates.
    ///
    /// The control sizes itself to the template's page size in DIPs; host it in a
    /// Viewbox to scale it to the available space.
    /// </summary>
    public sealed class TemplatePreviewControl : FrameworkElement
    {
        public static readonly DependencyProperty TemplateSourceProperty =
            DependencyProperty.Register(
                nameof(TemplateSource),
                typeof(RenderedTemplate),
                typeof(TemplatePreviewControl),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.AffectsMeasure |
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public RenderedTemplate? TemplateSource
        {
            get => (RenderedTemplate?)GetValue(TemplateSourceProperty);
            set => SetValue(TemplateSourceProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var t = TemplateSource;
            return t == null ? new Size(0, 0) : new Size(t.WidthDip, t.HeightDip);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var t = TemplateSource;
            if (t == null) return;

            TemplatePainter.Paint(t, new WpfRenderTarget(drawingContext));
        }
    }
}
