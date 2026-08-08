using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Apex.NumberedBooksEngine.UI.Visualizers
{
    /// <summary>
    /// Visualizes Cutting (Imposed Sheets) layout with cut lines.
    /// </summary>
    public class CuttingVisualizer
    {
        /// <summary>
        /// Draws cut lines on the canvas based on grid layout.
        /// </summary>
        public void DrawCutLines(Canvas canvas, int rows, int columns)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            var cutBrush = new SolidColorBrush(Color.FromRgb(255, 100, 100));

            // Draw horizontal cut lines
            for (int i = 1; i < rows; i++)
            {
                double y = height * i / rows;
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = cutBrush,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 10, 5 }
                };
                canvas.Children.Add(line);

                // Cut marker
                DrawScissors(canvas, 5, y);
            }

            // Draw vertical cut lines
            for (int j = 1; j < columns; j++)
            {
                double x = width * j / columns;
                var line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = cutBrush,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 10, 5 }
                };
                canvas.Children.Add(line);

                // Cut marker
                DrawScissors(canvas, x, 5);
            }
        }

        private void DrawScissors(Canvas canvas, double x, double y)
        {
            var marker = new TextBlock
            {
                Text = "✂",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100))
            };
            Canvas.SetLeft(marker, x - 7);
            Canvas.SetTop(marker, y - 10);
            canvas.Children.Add(marker);
        }

        /// <summary>
        /// Draws region labels (e.g., "A3 → 4 × A5").
        /// </summary>
        public void DrawRegionLabels(Canvas canvas, int rows, int columns, string sourceFormat, string targetFormat)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double cellWidth = width / columns;
            double cellHeight = height / rows;

            var labelBrush = new SolidColorBrush(Color.FromArgb(150, 100, 100, 100));

            // Draw main label at top
            var mainLabel = new TextBlock
            {
                Text = $"{sourceFormat} → {rows * columns} × {targetFormat}",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = labelBrush,
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
            };
            mainLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(mainLabel, (width - mainLabel.DesiredSize.Width) / 2);
            Canvas.SetTop(mainLabel, 5);
            canvas.Children.Add(mainLabel);

            // Draw cell numbers
            int cellIndex = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    cellIndex++;
                    var cellLabel = new TextBlock
                    {
                        Text = $"#{cellIndex}",
                        FontSize = 10,
                        Foreground = labelBrush
                    };

                    double x = c * cellWidth + 5;
                    double y = r * cellHeight + cellHeight - 20;

                    Canvas.SetLeft(cellLabel, x);
                    Canvas.SetTop(cellLabel, y);
                    canvas.Children.Add(cellLabel);
                }
            }
        }

        /// <summary>
        /// Draws numbering preview showing order after cutting.
        /// </summary>
        public void DrawNumberingOrder(Canvas canvas, int rows, int columns, long startNumber, long totalPages)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double cellWidth = width / columns;
            double cellHeight = height / rows;

            var textBrush = new SolidColorBrush(Colors.DarkGray);

            int slotIndex = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    // Imposition formula: value = start + 0 + slotIndex * totalPages
                    long number = startNumber + slotIndex * totalPages;

                    var label = new TextBlock
                    {
                        Text = number.ToString("N0"),
                        FontSize = Math.Min(cellWidth, cellHeight) * 0.15,
                        FontWeight = FontWeights.Bold,
                        Foreground = textBrush
                    };

                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double x = c * cellWidth + (cellWidth - label.DesiredSize.Width) / 2;
                    double y = r * cellHeight + (cellHeight - label.DesiredSize.Height) / 2;

                    Canvas.SetLeft(label, x);
                    Canvas.SetTop(label, y);
                    canvas.Children.Add(label);

                    slotIndex++;
                }
            }
        }

        /// <summary>
        /// Detect grid dimensions from slot positions.
        /// </summary>
        public (int Rows, int Columns) DetectGridFromSlots(IReadOnlyList<Rect> slotBounds, double tolerance = 0.05)
        {
            if (slotBounds == null || slotBounds.Count == 0)
                return (1, 1);

            var yPositions = new HashSet<double>();
            var xPositions = new HashSet<double>();

            foreach (var slot in slotBounds)
            {
                // Round to tolerance
                yPositions.Add(Math.Round(slot.Y / tolerance) * tolerance);
                xPositions.Add(Math.Round(slot.X / tolerance) * tolerance);
            }

            return (yPositions.Count, xPositions.Count);
        }
    }
}
