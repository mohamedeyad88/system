using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Apex.UI.ViewModels;

namespace Apex.UI.Controls
{
    /// <summary>
    /// Draws the press sheet with its placed pages at the sheet's TRUE aspect ratio,
    /// scaled uniformly to the space available.
    ///
    /// This is what lets an operator catch a wrong layout before burning paper:
    /// page order, rotation, blanks and the margin band are all visible. (The old
    /// preview stretched ratios into a fixed 420×300 canvas, so a portrait SRA3
    /// sheet appeared landscape.)
    /// </summary>
    public sealed class ImpositionSheetPreview : FrameworkElement
    {
        // ── Dependency properties ─────────────────────────────────────────────

        public static readonly DependencyProperty SheetWidthMmProperty =
            DependencyProperty.Register(nameof(SheetWidthMm), typeof(double), typeof(ImpositionSheetPreview),
                new FrameworkPropertyMetadata(0.0, Affects));

        public static readonly DependencyProperty SheetHeightMmProperty =
            DependencyProperty.Register(nameof(SheetHeightMm), typeof(double), typeof(ImpositionSheetPreview),
                new FrameworkPropertyMetadata(0.0, Affects));

        public static readonly DependencyProperty MarginMmProperty =
            DependencyProperty.Register(nameof(MarginMm), typeof(double), typeof(ImpositionSheetPreview),
                new FrameworkPropertyMetadata(0.0, Affects));

        public static readonly DependencyProperty SlotsProperty =
            DependencyProperty.Register(nameof(Slots), typeof(IEnumerable), typeof(ImpositionSheetPreview),
                new FrameworkPropertyMetadata(null, OnSlotsChanged));

        public static readonly DependencyProperty EmptyHintProperty =
            DependencyProperty.Register(nameof(EmptyHint), typeof(string), typeof(ImpositionSheetPreview),
                new FrameworkPropertyMetadata("", Affects));

        public double SheetWidthMm
        {
            get => (double)GetValue(SheetWidthMmProperty);
            set => SetValue(SheetWidthMmProperty, value);
        }

        public double SheetHeightMm
        {
            get => (double)GetValue(SheetHeightMmProperty);
            set => SetValue(SheetHeightMmProperty, value);
        }

        /// <summary>Sheet margin (mm) drawn as a dashed guide.</summary>
        public double MarginMm
        {
            get => (double)GetValue(MarginMmProperty);
            set => SetValue(MarginMmProperty, value);
        }

        public IEnumerable? Slots
        {
            get => (IEnumerable?)GetValue(SlotsProperty);
            set => SetValue(SlotsProperty, value);
        }

        /// <summary>Message shown before a plan has been computed.</summary>
        public string EmptyHint
        {
            get => (string)GetValue(EmptyHintProperty);
            set => SetValue(EmptyHintProperty, value);
        }

        private static void Affects(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((ImpositionSheetPreview)d).InvalidateVisual();

        private static void OnSlotsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (ImpositionSheetPreview)d;
            if (e.OldValue is INotifyCollectionChanged oldCol) oldCol.CollectionChanged -= c.OnCollectionChanged;
            if (e.NewValue is INotifyCollectionChanged newCol) newCol.CollectionChanged += c.OnCollectionChanged;
            c.InvalidateVisual();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

        // ── Brushes (frozen, shared) ──────────────────────────────────────────

        private static readonly Brush Paper = Frozen(new SolidColorBrush(Colors.White));
        private static readonly Brush SheetEdge = Frozen(new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)));
        private static readonly Brush PageFill = Frozen(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)));
        private static readonly Brush RotatedFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)));
        private static readonly Brush BlankFill = Frozen(new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)));
        private static readonly Brush PageEdge = Frozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)));
        private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Colors.White));
        private static readonly Brush HintBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)));
        private static readonly Brush MarginBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)));

        private static Brush Frozen(Brush b) { b.Freeze(); return b; }

        // ── Layout ────────────────────────────────────────────────────────────

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) ? 460 : availableSize.Width;
            double h = double.IsInfinity(availableSize.Height) ? 380 : availableSize.Height;
            if (w <= 0 || h <= 0) return new Size(0, 0);

            // No plan yet: occupy the offered space so the empty hint stays visible.
            if (SheetWidthMm <= 0 || SheetHeightMm <= 0) return new Size(w, h);

            // Keep the sheet's aspect ratio within whatever space the parent offers.
            double scale = Math.Min(w / SheetWidthMm, h / SheetHeightMm);
            return new Size(SheetWidthMm * scale, SheetHeightMm * scale);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var slots = Slots?.OfType<ImpositionPreviewRect>().ToList() ?? new List<ImpositionPreviewRect>();

            if (SheetWidthMm <= 0 || SheetHeightMm <= 0 || slots.Count == 0)
            {
                DrawHint(dc);
                return;
            }

            double availW = ActualWidth, availH = ActualHeight;
            if (availW <= 0 || availH <= 0) return;

            // Uniform scale — never distort the sheet.
            double scale = Math.Min(availW / SheetWidthMm, availH / SheetHeightMm);
            double sheetW = SheetWidthMm * scale;
            double sheetH = SheetHeightMm * scale;
            double ox = (availW - sheetW) / 2.0;
            double oy = (availH - sheetH) / 2.0;

            // ── Paper ──────────────────────────────────────────────────────────
            var sheetRect = new Rect(ox, oy, sheetW, sheetH);
            dc.DrawRectangle(Paper, new Pen(SheetEdge, 1), sheetRect);

            // ── Margin guide ───────────────────────────────────────────────────
            if (MarginMm > 0 && MarginMm * 2 < SheetWidthMm && MarginMm * 2 < SheetHeightMm)
            {
                var m = MarginMm * scale;
                var guide = new Pen(MarginBrush, 0.7)
                {
                    DashStyle = new DashStyle(new double[] { 3, 3 }, 0),
                };
                guide.Freeze();
                dc.DrawRectangle(null, guide,
                    new Rect(ox + m, oy + m, Math.Max(0, sheetW - 2 * m), Math.Max(0, sheetH - 2 * m)));
            }

            // ── Placed pages ───────────────────────────────────────────────────
            foreach (var s in slots)
            {
                double x = ox + s.XMm * scale;
                double y = oy + s.YMm * scale;
                double w = s.WidthMm * scale;
                double h = s.HeightMm * scale;
                if (w < 1 || h < 1) continue;

                var rect = new Rect(x, y, w, h);
                Brush fill = s.IsBlank ? BlankFill : (s.IsRotated ? RotatedFill : PageFill);
                dc.DrawRectangle(fill, new Pen(PageEdge, 0.6), rect);

                if (s.IsBlank) continue;

                DrawPageLabel(dc, s, rect);
            }
        }

        /// <summary>Draws the page number, rotated with the page so the order reads correctly.</summary>
        private void DrawPageLabel(DrawingContext dc, ImpositionPreviewRect s, Rect rect)
        {
            double fontSize = Math.Max(8, Math.Min(rect.Width, rect.Height) * 0.28);
            var ft = new FormattedText(
                s.PageLabel,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                fontSize,
                LabelBrush,
                pixelsPerDip: 1.0);

            var centre = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            bool rotated = s.Rotation % 360 != 0;

            if (rotated)
                dc.PushTransform(new RotateTransform(s.Rotation, centre.X, centre.Y));

            dc.DrawText(ft, new Point(centre.X - ft.Width / 2, centre.Y - ft.Height / 2));

            if (rotated) dc.Pop();
        }

        private void DrawHint(DrawingContext dc)
        {
            if (string.IsNullOrWhiteSpace(EmptyHint) || ActualWidth <= 0 || ActualHeight <= 0) return;

            var ft = new FormattedText(
                EmptyHint,
                CultureInfo.CurrentUICulture,
                FlowDirection.RightToLeft,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                13,
                HintBrush,
                pixelsPerDip: 1.0);

            dc.DrawText(ft, new Point(
                Math.Max(0, (ActualWidth - ft.Width) / 2),
                Math.Max(0, (ActualHeight - ft.Height) / 2)));
        }
    }
}
