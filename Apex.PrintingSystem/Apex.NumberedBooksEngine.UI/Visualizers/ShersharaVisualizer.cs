using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Apex.NumberedBooksEngine.UI.Visualizers
{
    /// <summary>
    /// Visualizes Shershara (Perforated Pads) layout with perforation lines.
    /// </summary>
    public class ShersharaVisualizer
    {
        public double PerforationGap { get; set; } = 5; // Spacing between perforations

        /// <summary>
        /// Draws perforation lines on the canvas between slots.
        /// </summary>
        public void DrawPerforationLines(Canvas canvas, IReadOnlyList<Rect> slotBounds, double pageHeight)
        {
            if (slotBounds == null || slotBounds.Count < 2) return;

            var perfBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            double canvasWidth = canvas.ActualWidth;

            // Sort slots by Y position
            var sortedSlots = new List<Rect>(slotBounds);
            sortedSlots.Sort((a, b) => a.Y.CompareTo(b.Y));

            // Draw perforation between each pair of slots
            for (int i = 0; i < sortedSlots.Count - 1; i++)
            {
                double y = (sortedSlots[i].Bottom + sortedSlots[i + 1].Top) / 2;

                // Draw dashed line
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = canvasWidth,
                    Y2 = y,
                    Stroke = perfBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 3 }
                };

                canvas.Children.Add(line);

                // Add label
                var label = new TextBlock
                {
                    Text = "─── perforation ───",
                    FontSize = 9,
                    Foreground = perfBrush,
                    Opacity = 0.7
                };

                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, (canvasWidth - label.DesiredSize.Width) / 2);
                Canvas.SetTop(label, y - label.DesiredSize.Height / 2);
                canvas.Children.Add(label);
            }
        }

        /// <summary>
        /// Draws a preview of numbering progression for a pad.
        /// </summary>
        public void DrawNumberingPreview(Canvas canvas, IReadOnlyList<Rect> slotBounds, long startNumber)
        {
            if (slotBounds == null) return;

            var textBrush = new SolidColorBrush(Colors.DarkGray);

            // Sort slots by Y position
            var sortedSlots = new List<Rect>(slotBounds);
            sortedSlots.Sort((a, b) => a.Y.CompareTo(b.Y));

            for (int i = 0; i < sortedSlots.Count; i++)
            {
                var slot = sortedSlots[i];
                long number = startNumber + i;

                var label = new TextBlock
                {
                    Text = number.ToString("D4"),
                    FontSize = Math.Min(slot.Width, slot.Height) * 0.6,
                    Foreground = textBrush,
                    FontWeight = FontWeights.Bold
                };

                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, slot.X + (slot.Width - label.DesiredSize.Width) / 2);
                Canvas.SetTop(label, slot.Y + (slot.Height - label.DesiredSize.Height) / 2);
                canvas.Children.Add(label);
            }
        }

        /// <summary>
        /// Draws pad layer separation indicators.
        /// </summary>
        public void DrawPadLayers(Canvas canvas, int padSize, double pageHeight)
        {
            var layerBrush = new SolidColorBrush(Color.FromArgb(40, 100, 100, 255));
            double layerHeight = pageHeight / padSize;

            for (int i = 0; i < padSize; i++)
            {
                var rect = new Rectangle
                {
                    Width = canvas.ActualWidth,
                    Height = layerHeight,
                    Fill = i % 2 == 0 ? Brushes.Transparent : layerBrush,
                    Opacity = 0.3
                };

                Canvas.SetTop(rect, i * layerHeight);
                canvas.Children.Add(rect);
            }
        }
    }
}
