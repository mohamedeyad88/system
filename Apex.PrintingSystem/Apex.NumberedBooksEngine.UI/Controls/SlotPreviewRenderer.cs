using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Apex.NumberedBooksEngine.UI.Controls
{
    /// <summary>
    /// Renders live number previews on slots.
    /// </summary>
    public class SlotPreviewRenderer
    {
        public long StartNumber { get; set; } = 1;
        public NumberingMode Mode { get; set; } = NumberingMode.Auto;
        public bool ShowMultiCopyPreview { get; set; } = false;
        public string NumberFormat { get; set; } = "D4"; // e.g., "0001"

        private readonly Canvas _canvas;

        public SlotPreviewRenderer(Canvas canvas)
        {
            _canvas = canvas;
        }

        /// <summary>
        /// Renders number previews on all slots.
        /// </summary>
        public void RenderPreviews(IReadOnlyList<SlotSpec> slots, Size canvasSize)
        {
            ClearPreviews();

            if (slots == null || slots.Count == 0) return;

            // Calculate numbers based on mode
            var numbers = CalculatePreviewNumbers(slots.Count);

            for (int i = 0; i < slots.Count && i < numbers.Length; i++)
            {
                var slot = slots[i];
                long number = numbers[i];

                RenderSlotPreview(slot, number, i, canvasSize);
            }
        }

        private long[] CalculatePreviewNumbers(int slotCount)
        {
            var numbers = new long[slotCount];

            // Use if-else instead of switch to handle aliased enum values
            if (Mode == NumberingMode.Linear || Mode == NumberingMode.Shershara)
            {
                // Linear: 1, 2, 3, 4...
                for (int i = 0; i < slotCount; i++)
                    numbers[i] = StartNumber + i;
            }
            else if (Mode == NumberingMode.Imposed || Mode == NumberingMode.Cutting)
            {
                // Imposed: assuming 100 pages for preview
                long totalPages = 100;
                for (int i = 0; i < slotCount; i++)
                    numbers[i] = StartNumber + (i * totalPages);
            }
            else
            {
                // Default/Auto
                for (int i = 0; i < slotCount; i++)
                    numbers[i] = StartNumber + i;
            }

            return numbers;
        }

        private void RenderSlotPreview(SlotSpec slot, long number, int index, Size canvasSize)
        {
            // Convert normalized coordinates to pixels
            double x = slot.X * canvasSize.Width;
            double y = slot.Y * canvasSize.Height;
            double width = slot.Width * canvasSize.Width;
            double height = slot.Height * canvasSize.Height;

            // Create preview container
            var container = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Tag = "SlotPreview"
            };

            // Create number text
            string displayText = number.ToString(NumberFormat);
            if (ShowMultiCopyPreview && slot.CopyStyles?.Length > 0)
            {
                displayText += $" / {slot.CopyStyles[0].Label}";
            }

            var textBlock = new TextBlock
            {
                Text = displayText,
                FontFamily = new FontFamily(slot.FontFamily ?? "Arial"),
                FontSize = Math.Min(height * 0.6, slot.FontSize),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = slot.Align switch
                {
                    TextAlign.Left => HorizontalAlignment.Left,
                    TextAlign.Right => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Center
                },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3)
            };

            // Apply color
            try
            {
                if (!string.IsNullOrEmpty(slot.FontColorHex))
                    textBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(slot.FontColorHex));
                else
                    textBlock.Foreground = Brushes.Black;
            }
            catch
            {
                textBlock.Foreground = Brushes.Black;
            }

            // Apply rotation
            if (slot.Rotation != 0)
            {
                textBlock.RenderTransform = new RotateTransform(slot.Rotation);
                textBlock.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            container.Child = textBlock;

            Canvas.SetLeft(container, x);
            Canvas.SetTop(container, y);
            _canvas.Children.Add(container);

            // Add slot index label
            var indexLabel = new TextBlock
            {
                Text = $"#{index + 1}",
                FontSize = 9,
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                Padding = new Thickness(3, 1, 3, 1),
                Tag = "SlotPreview"
            };
            Canvas.SetLeft(indexLabel, x);
            Canvas.SetTop(indexLabel, y - 14);
            _canvas.Children.Add(indexLabel);
        }

        /// <summary>
        /// Renders multi-copy color preview for a single slot.
        /// </summary>
        public void RenderMultiCopyPreview(SlotSpec slot, Size canvasSize)
        {
            if (slot.CopyStyles == null || slot.CopyStyles.Length == 0) return;

            double x = slot.X * canvasSize.Width;
            double y = slot.Y * canvasSize.Height;
            double height = slot.Height * canvasSize.Height;

            // Show stacked copy indicators
            for (int i = 0; i < slot.CopyStyles.Length && i < 4; i++)
            {
                var style = slot.CopyStyles[i];

                var indicator = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(2),
                    BorderBrush = Brushes.White,
                    Tag = "SlotPreview"
                };

                try
                {
                    indicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.ColorHex))
                    {
                        Opacity = style.Opacity
                    };
                }
                catch
                {
                    indicator.Background = Brushes.Gray;
                }

                Canvas.SetLeft(indicator, x + (i * 15));
                Canvas.SetTop(indicator, y + height + 5);
                _canvas.Children.Add(indicator);
            }
        }

        /// <summary>
        /// Clears all preview elements.
        /// </summary>
        public void ClearPreviews()
        {
            var toRemove = new List<UIElement>();
            foreach (UIElement child in _canvas.Children)
            {
                if (child is FrameworkElement fe && fe.Tag?.ToString() == "SlotPreview")
                {
                    toRemove.Add(child);
                }
            }
            foreach (var element in toRemove)
            {
                _canvas.Children.Remove(element);
            }
        }

        /// <summary>
        /// Updates preview for a single slot (used during drag).
        /// </summary>
        public void UpdateSingleSlotPreview(SlotSpec slot, int index, Size canvasSize)
        {
            long number = Mode == NumberingMode.Imposed || Mode == NumberingMode.Cutting
                ? StartNumber + (index * 100)
                : StartNumber + index;

            RenderSlotPreview(slot, number, index, canvasSize);
        }
    }
}
