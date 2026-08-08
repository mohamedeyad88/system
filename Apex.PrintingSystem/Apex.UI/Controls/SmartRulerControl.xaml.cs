using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Apex.UI.Models;
using static Apex.UI.Models.PaperSizeHelper;

namespace Apex.UI.Controls
{
    public partial class SmartRulerControl : UserControl
    {
        public static readonly DependencyProperty OrientationProperty =
            DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(SmartRulerControl),
                new PropertyMetadata(Orientation.Horizontal, OnPropertyChanged));

        public static readonly DependencyProperty ZoomLevelProperty =
            DependencyProperty.Register(nameof(ZoomLevel), typeof(double), typeof(SmartRulerControl),
                new PropertyMetadata(1.0, OnPropertyChanged));

        public static readonly DependencyProperty CanvasWidthProperty =
            DependencyProperty.Register(nameof(CanvasWidth), typeof(double), typeof(SmartRulerControl),
                new PropertyMetadata(595.276, OnPropertyChanged));

        public static readonly DependencyProperty CanvasHeightProperty =
            DependencyProperty.Register(nameof(CanvasHeight), typeof(double), typeof(SmartRulerControl),
                new PropertyMetadata(841.890, OnPropertyChanged));

        public static readonly DependencyProperty MeasurementUnitProperty =
            DependencyProperty.Register(nameof(MeasurementUnit), typeof(MeasurementUnit), typeof(SmartRulerControl),
                new PropertyMetadata(MeasurementUnit.Centimeters, OnPropertyChanged));

        public Orientation Orientation
        {
            get => (Orientation)GetValue(OrientationProperty);
            set => SetValue(OrientationProperty, value);
        }

        public double ZoomLevel
        {
            get => (double)GetValue(ZoomLevelProperty);
            set => SetValue(ZoomLevelProperty, value);
        }

        public double CanvasWidth
        {
            get => (double)GetValue(CanvasWidthProperty);
            set => SetValue(CanvasWidthProperty, value);
        }

        public double CanvasHeight
        {
            get => (double)GetValue(CanvasHeightProperty);
            set => SetValue(CanvasHeightProperty, value);
        }

        public MeasurementUnit MeasurementUnit
        {
            get => (MeasurementUnit)GetValue(MeasurementUnitProperty);
            set => SetValue(MeasurementUnitProperty, value);
        }

        public SmartRulerControl()
        {
            InitializeComponent();
            SizeChanged += (s, e) => DrawRuler();
            Loaded += (s, e) => DrawRuler();
        }

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SmartRulerControl ruler)
                ruler.DrawRuler();
        }

        public void DrawRuler()
        {
            RulerCanvas.Children.Clear();

            double length = Orientation == Orientation.Horizontal ? ActualWidth : ActualHeight;
            double thickness = Orientation == Orientation.Horizontal ? ActualHeight : ActualWidth;

            if (length <= 0 || thickness <= 0) return;

            // Get canvas dimension for this ruler
            double canvasDimension = Orientation == Orientation.Horizontal ? CanvasWidth : CanvasHeight;

            // Calculate effective length in points (accounting for zoom)
            double effectiveLength = canvasDimension * ZoomLevel;

            // Calculate tick intervals based on unit and zoom
            (double majorIntervalPoints, double minorIntervalPoints) = CalculateTickIntervals();

            // Convert intervals to pixels for drawing
            double scaleFactor = length / effectiveLength;
            double majorIntervalPixels = majorIntervalPoints * scaleFactor;
            double minorIntervalPixels = minorIntervalPoints * scaleFactor;

            var tickBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            var textBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));

            // Draw background border
            var border = new Rectangle
            {
                Width = Orientation == Orientation.Horizontal ? length : thickness,
                Height = Orientation == Orientation.Horizontal ? thickness : length,
                Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
                StrokeThickness = 1
            };
            RulerCanvas.Children.Add(border);

            // Draw ticks and numbers
            for (double posPixels = 0; posPixels <= length; posPixels += minorIntervalPixels)
            {
                // Convert pixel position back to points
                double posPoints = (posPixels / scaleFactor);

                bool isMajor = Math.Abs(posPoints % majorIntervalPoints) < 0.1;
                double tickHeight = isMajor ? thickness * 0.6 : thickness * 0.3;

                DrawTick(posPixels, tickHeight, tickBrush, thickness);

                // Draw number at major ticks
                if (isMajor && posPoints > 0)
                {
                    DrawLabel(posPixels, posPoints, textBrush, thickness);
                }
            }
        }

        private (double major, double minor) CalculateTickIntervals()
        {
            // Base intervals in points
            double majorPoints, minorPoints;

            switch (MeasurementUnit)
            {
                case MeasurementUnit.Centimeters:
                    majorPoints = CentimetersToPoints(1.0); // 1 cm = ~28.35pt
                    minorPoints = CentimetersToPoints(0.5); // 0.5 cm
                    break;
                case MeasurementUnit.Inches:
                    majorPoints = InchesToPoints(1.0); // 1 inch = 72pt
                    minorPoints = InchesToPoints(0.5); // 0.5 inch
                    break;
                case MeasurementUnit.Points:
                default:
                    majorPoints = 10.0; // 10 points
                    minorPoints = 5.0;  // 5 points
                    break;
            }

            // Adjust based on zoom level
            if (ZoomLevel < 0.5)
            {
                // Low zoom: show fewer ticks
                majorPoints *= 2;
                minorPoints *= 2;
            }
            else if (ZoomLevel > 2.0)
            {
                // High zoom: show more granular ticks
                majorPoints *= 0.5;
                minorPoints *= 0.5;
            }

            return (majorPoints, minorPoints);
        }

        private void DrawTick(double pos, double tickHeight, Brush brush, double thickness)
        {
            Line tick;

            if (Orientation == Orientation.Horizontal)
            {
                tick = new Line
                {
                    X1 = pos,
                    Y1 = thickness,
                    X2 = pos,
                    Y2 = thickness - tickHeight,
                    Stroke = brush,
                    StrokeThickness = 1
                };
            }
            else
            {
                tick = new Line
                {
                    X1 = thickness,
                    Y1 = pos,
                    X2 = thickness - tickHeight,
                    Y2 = pos,
                    Stroke = brush,
                    StrokeThickness = 1
                };
            }

            RulerCanvas.Children.Add(tick);
        }

        private void DrawLabel(double posPixels, double posPoints, Brush brush, double thickness)
        {
            string labelText = FormatValue(posPoints, MeasurementUnit);

            var label = new TextBlock
            {
                Text = labelText,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = brush
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            if (Orientation == Orientation.Horizontal)
            {
                // Horizontal ruler: labels at top, centered
                Canvas.SetLeft(label, posPixels - label.DesiredSize.Width / 2);
                Canvas.SetTop(label, 2);
            }
            else
            {
                // Vertical ruler: labels rotated correctly, positioned at left side
                label.RenderTransform = new RotateTransform(-90);
                label.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

                // Position: left side of ruler, centered vertically
                Canvas.SetLeft(label, thickness / 2 - label.DesiredSize.Height / 2);
                Canvas.SetTop(label, posPixels - label.DesiredSize.Width / 2);
            }

            RulerCanvas.Children.Add(label);
        }

        private string FormatValue(double points, MeasurementUnit unit)
        {
            return PaperSizeHelper.FormatValue(points, unit);
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
