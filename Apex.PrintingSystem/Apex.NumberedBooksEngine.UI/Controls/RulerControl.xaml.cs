using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Apex.NumberedBooksEngine.UI.Controls
{
    /// <summary>
    /// Ruler control for horizontal or vertical measurement.
    /// </summary>
    public partial class RulerControl : UserControl
    {
        public static readonly DependencyProperty OrientationProperty =
            DependencyProperty.Register("Orientation", typeof(Orientation), typeof(RulerControl),
                new PropertyMetadata(Orientation.Horizontal, OnPropertyChanged));

        public static readonly DependencyProperty TickIntervalProperty =
            DependencyProperty.Register("TickInterval", typeof(double), typeof(RulerControl),
                new PropertyMetadata(10.0, OnPropertyChanged));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register("Unit", typeof(RulerUnit), typeof(RulerControl),
                new PropertyMetadata(RulerUnit.Pixels, OnPropertyChanged));

        public static readonly DependencyProperty HighlightStartProperty =
            DependencyProperty.Register("HighlightStart", typeof(double), typeof(RulerControl),
                new PropertyMetadata(-1.0, OnPropertyChanged));

        public static readonly DependencyProperty HighlightEndProperty =
            DependencyProperty.Register("HighlightEnd", typeof(double), typeof(RulerControl),
                new PropertyMetadata(-1.0, OnPropertyChanged));

        public static readonly DependencyProperty DpiProperty =
            DependencyProperty.Register("Dpi", typeof(double), typeof(RulerControl),
                new PropertyMetadata(96.0, OnPropertyChanged));

        public Orientation Orientation
        {
            get => (Orientation)GetValue(OrientationProperty);
            set => SetValue(OrientationProperty, value);
        }

        public double TickInterval
        {
            get => (double)GetValue(TickIntervalProperty);
            set => SetValue(TickIntervalProperty, value);
        }

        public RulerUnit Unit
        {
            get => (RulerUnit)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public double HighlightStart
        {
            get => (double)GetValue(HighlightStartProperty);
            set => SetValue(HighlightStartProperty, value);
        }

        public double HighlightEnd
        {
            get => (double)GetValue(HighlightEndProperty);
            set => SetValue(HighlightEndProperty, value);
        }

        public double Dpi
        {
            get => (double)GetValue(DpiProperty);
            set => SetValue(DpiProperty, value);
        }

        public RulerControl()
        {
            InitializeComponent();
            SizeChanged += (s, e) => DrawRuler();
            Loaded += (s, e) => DrawRuler();
        }

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RulerControl ruler)
                ruler.DrawRuler();
        }

        public void DrawRuler()
        {
            RulerCanvas.Children.Clear();

            double length = Orientation == Orientation.Horizontal ? ActualWidth : ActualHeight;
            double thickness = Orientation == Orientation.Horizontal ? ActualHeight : ActualWidth;
            if (length <= 0 || thickness <= 0) return;

            var tickBrush = new SolidColorBrush(Colors.Gray);
            var textBrush = new SolidColorBrush(Colors.DarkGray);
            var highlightBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));

            double interval = GetIntervalInPixels();

            // Draw highlight region
            if (HighlightStart >= 0 && HighlightEnd > HighlightStart)
            {
                DrawHighlight(highlightBrush, thickness);
            }

            // Draw ticks and labels
            for (double pos = 0; pos <= length; pos += interval)
            {
                bool isMajor = (int)(pos / interval) % 5 == 0;
                double tickHeight = isMajor ? thickness * 0.6 : thickness * 0.3;

                DrawTick(pos, tickHeight, tickBrush, thickness);

                if (isMajor && pos > 0)
                {
                    DrawLabel(pos, textBrush, thickness);
                }
            }
        }

        private double GetIntervalInPixels()
        {
            return Unit switch
            {
                RulerUnit.Pixels => TickInterval,
                RulerUnit.Millimeters => TickInterval * (Dpi / 25.4),
                RulerUnit.Inches => TickInterval * Dpi,
                RulerUnit.Centimeters => TickInterval * (Dpi / 2.54),
                _ => TickInterval
            };
        }

        private void DrawTick(double pos, double tickHeight, Brush brush, double thickness)
        {
            Line tick;

            if (Orientation == Orientation.Horizontal)
            {
                tick = new Line
                {
                    X1 = pos, Y1 = thickness,
                    X2 = pos, Y2 = thickness - tickHeight,
                    Stroke = brush, StrokeThickness = 1
                };
            }
            else
            {
                tick = new Line
                {
                    X1 = thickness, Y1 = pos,
                    X2 = thickness - tickHeight, Y2 = pos,
                    Stroke = brush, StrokeThickness = 1
                };
            }

            RulerCanvas.Children.Add(tick);
        }

        private void DrawLabel(double pos, Brush brush, double thickness)
        {
            string labelText = FormatValue(pos);

            var label = new TextBlock
            {
                Text = labelText,
                FontSize = 9,
                Foreground = brush
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            if (Orientation == Orientation.Horizontal)
            {
                Canvas.SetLeft(label, pos - label.DesiredSize.Width / 2);
                Canvas.SetTop(label, 2);
            }
            else
            {
                Canvas.SetLeft(label, 2);
                Canvas.SetTop(label, pos - label.DesiredSize.Height / 2);
            }

            RulerCanvas.Children.Add(label);
        }

        private string FormatValue(double pixels)
        {
            double value = Unit switch
            {
                RulerUnit.Millimeters => pixels * 25.4 / Dpi,
                RulerUnit.Inches => pixels / Dpi,
                RulerUnit.Centimeters => pixels * 2.54 / Dpi,
                _ => pixels
            };

            return Unit switch
            {
                RulerUnit.Millimeters => $"{value:F0}",
                RulerUnit.Inches => $"{value:F1}\"",
                RulerUnit.Centimeters => $"{value:F1}",
                _ => $"{value:F0}"
            };
        }

        private void DrawHighlight(Brush brush, double thickness)
        {
            Rectangle highlight;

            if (Orientation == Orientation.Horizontal)
            {
                highlight = new Rectangle
                {
                    Width = HighlightEnd - HighlightStart,
                    Height = thickness,
                    Fill = brush
                };
                Canvas.SetLeft(highlight, HighlightStart);
                Canvas.SetTop(highlight, 0);
            }
            else
            {
                highlight = new Rectangle
                {
                    Width = thickness,
                    Height = HighlightEnd - HighlightStart,
                    Fill = brush
                };
                Canvas.SetLeft(highlight, 0);
                Canvas.SetTop(highlight, HighlightStart);
            }

            RulerCanvas.Children.Add(highlight);
        }

        /// <summary>
        /// Updates the highlight to show a selected element's position.
        /// </summary>
        public void SetHighlight(double start, double end)
        {
            HighlightStart = start;
            HighlightEnd = end;
        }

        /// <summary>
        /// Clears the highlight.
        /// </summary>
        public void ClearHighlight()
        {
            HighlightStart = -1;
            HighlightEnd = -1;
        }
    }

    public enum RulerUnit
    {
        Pixels,
        Millimeters,
        Inches,
        Centimeters
    }
}
