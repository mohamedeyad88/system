using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Apex.NumberedBooksEngine.UI.Controls
{
    /// <summary>
    /// Grid overlay for the preview canvas with snap-to-grid support.
    /// </summary>
    public partial class GridOverlay : UserControl
    {
        public static readonly DependencyProperty GridSizeProperty =
            DependencyProperty.Register("GridSize", typeof(double), typeof(GridOverlay),
                new PropertyMetadata(20.0, OnGridPropertyChanged));

        public static readonly DependencyProperty IsGridVisibleProperty =
            DependencyProperty.Register("IsGridVisible", typeof(bool), typeof(GridOverlay),
                new PropertyMetadata(true, OnGridPropertyChanged));

        public static readonly DependencyProperty GridModeProperty =
            DependencyProperty.Register("GridMode", typeof(GridMode), typeof(GridOverlay),
                new PropertyMetadata(GridMode.Medium, OnGridPropertyChanged));

        public static readonly DependencyProperty ShowSafeMarginProperty =
            DependencyProperty.Register("ShowSafeMargin", typeof(bool), typeof(GridOverlay),
                new PropertyMetadata(true, OnGridPropertyChanged));

        public static readonly DependencyProperty SafeMarginProperty =
            DependencyProperty.Register("SafeMargin", typeof(double), typeof(GridOverlay),
                new PropertyMetadata(20.0, OnGridPropertyChanged));

        public double GridSize
        {
            get => (double)GetValue(GridSizeProperty);
            set => SetValue(GridSizeProperty, value);
        }

        public bool IsGridVisible
        {
            get => (bool)GetValue(IsGridVisibleProperty);
            set => SetValue(IsGridVisibleProperty, value);
        }

        public GridMode GridMode
        {
            get => (GridMode)GetValue(GridModeProperty);
            set => SetValue(GridModeProperty, value);
        }

        public bool ShowSafeMargin
        {
            get => (bool)GetValue(ShowSafeMarginProperty);
            set => SetValue(ShowSafeMarginProperty, value);
        }

        public double SafeMargin
        {
            get => (double)GetValue(SafeMarginProperty);
            set => SetValue(SafeMarginProperty, value);
        }

        public GridOverlay()
        {
            InitializeComponent();
            SizeChanged += (s, e) => DrawGrid();
            Loaded += (s, e) => DrawGrid();
        }

        private static void OnGridPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GridOverlay overlay)
                overlay.DrawGrid();
        }

        public void DrawGrid()
        {
            GridCanvas.Children.Clear();

            if (!IsGridVisible) return;

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0) return;

            double gridSize = GridMode switch
            {
                GridMode.Fine => 5,
                GridMode.Medium => 10,
                GridMode.Coarse => 20,
                _ => GridSize
            };

            var gridBrush = new SolidColorBrush(Color.FromArgb(60, 100, 100, 100));
            var majorBrush = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100));

            // Draw vertical lines
            for (double x = 0; x <= width; x += gridSize)
            {
                bool isMajor = (int)(x / gridSize) % 5 == 0;
                var line = new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = height,
                    Stroke = isMajor ? majorBrush : gridBrush,
                    StrokeThickness = isMajor ? 0.5 : 0.25,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                GridCanvas.Children.Add(line);
            }

            // Draw horizontal lines
            for (double y = 0; y <= height; y += gridSize)
            {
                bool isMajor = (int)(y / gridSize) % 5 == 0;
                var line = new Line
                {
                    X1 = 0, Y1 = y, X2 = width, Y2 = y,
                    Stroke = isMajor ? majorBrush : gridBrush,
                    StrokeThickness = isMajor ? 0.5 : 0.25,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                GridCanvas.Children.Add(line);
            }

            // Draw center lines
            DrawCenterLines(width, height);

            // Draw safe margin rectangle
            if (ShowSafeMargin)
            {
                DrawSafeMargin(width, height);
            }
        }

        private void DrawCenterLines(double width, double height)
        {
            var centerBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));

            // Vertical center
            GridCanvas.Children.Add(new Line
            {
                X1 = width / 2, Y1 = 0, X2 = width / 2, Y2 = height,
                Stroke = centerBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            });

            // Horizontal center
            GridCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = height / 2, X2 = width, Y2 = height / 2,
                Stroke = centerBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            });
        }

        private void DrawSafeMargin(double width, double height)
        {
            var marginBrush = new SolidColorBrush(Color.FromArgb(50, 255, 100, 100));
            var marginStroke = new SolidColorBrush(Color.FromArgb(150, 255, 0, 0));

            // Safe area rectangle
            var rect = new Rectangle
            {
                Width = width - (SafeMargin * 2),
                Height = height - (SafeMargin * 2),
                Stroke = marginStroke,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 5, 3 },
                Fill = Brushes.Transparent
            };

            Canvas.SetLeft(rect, SafeMargin);
            Canvas.SetTop(rect, SafeMargin);
            GridCanvas.Children.Add(rect);
        }

        /// <summary>
        /// Snaps a point to the nearest grid line.
        /// </summary>
        public Point SnapToGrid(Point point)
        {
            double gridSize = GridMode switch
            {
                GridMode.Fine => 5,
                GridMode.Medium => 10,
                GridMode.Coarse => 20,
                _ => GridSize
            };

            return new Point(
                Math.Round(point.X / gridSize) * gridSize,
                Math.Round(point.Y / gridSize) * gridSize
            );
        }

        /// <summary>
        /// Checks if a point is near a grid line (within threshold).
        /// </summary>
        public bool IsNearGridLine(Point point, double threshold = 5)
        {
            var snapped = SnapToGrid(point);
            return Math.Abs(point.X - snapped.X) < threshold ||
                   Math.Abs(point.Y - snapped.Y) < threshold;
        }
    }

    public enum GridMode
    {
        Fine,    // 5px
        Medium,  // 10px
        Coarse   // 20px
    }
}
