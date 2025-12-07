using Apex.NumberedBooksEngine.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Apex.NumberedBooksEngine.UI.Controls
{
    /// <summary>
    /// Panel for editing slot styling properties.
    /// </summary>
    public partial class SlotStylePanel : UserControl
    {
        public static readonly DependencyProperty SelectedSlotProperty =
            DependencyProperty.Register("SelectedSlot", typeof(SlotSpec), typeof(SlotStylePanel),
                new PropertyMetadata(null, OnSelectedSlotChanged));

        public SlotSpec? SelectedSlot
        {
            get => (SlotSpec?)GetValue(SelectedSlotProperty);
            set => SetValue(SelectedSlotProperty, value);
        }

        public event EventHandler<SlotSpec>? SlotChanged;

        public SlotStylePanel()
        {
            InitializeComponent();
        }

        private static void OnSelectedSlotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SlotStylePanel panel && e.NewValue is SlotSpec slot)
            {
                panel.LoadSlot(slot);
            }
        }

        private void LoadSlot(SlotSpec slot)
        {
            // Font Family
            FontFamilyCombo.Text = slot.FontFamily;

            // Font Size
            FontSizeSlider.Value = slot.FontSize;

            // Text Color
            TextColorHex.Text = slot.FontColorHex;
            UpdateColorPreview(TextColorPreview, slot.FontColorHex);

            // Rotation
            Rot0.IsChecked = Math.Abs(slot.Rotation) < 1;
            Rot90.IsChecked = Math.Abs(slot.Rotation - 90) < 1;
            Rot180.IsChecked = Math.Abs(slot.Rotation - 180) < 1;
            Rot270.IsChecked = Math.Abs(slot.Rotation - 270) < 1;

            // Alignment
            AlignLeft.IsChecked = slot.Align == TextAlign.Left;
            AlignCenter.IsChecked = slot.Align == TextAlign.Center;
            AlignRight.IsChecked = slot.Align == TextAlign.Right;

            // Copy styles
            if (slot.CopyStyles != null && slot.CopyStyles.Length > 0)
            {
                UpdateColorPreview(OriginalColorPreview, slot.CopyStyles[0].ColorHex);
                OriginalLabelCheck.IsChecked = !string.IsNullOrEmpty(slot.CopyStyles[0].Label);
            }

            UpdatePreview();
        }

        private void UpdateColorPreview(Border preview, string? hex)
        {
            try
            {
                if (!string.IsNullOrEmpty(hex))
                {
                    preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                }
            }
            catch
            {
                preview.Background = Brushes.Gray;
            }
        }

        private void TextColorHex_Changed(object sender, TextChangedEventArgs e)
        {
            UpdateColorPreview(TextColorPreview, TextColorHex.Text);
            UpdatePreview();
            NotifyChange();
        }

        private void BgColorHex_Changed(object sender, TextChangedEventArgs e)
        {
            UpdateColorPreview(BgColorPreview, BgColorHex.Text);
        }

        private void TextColorPreview_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Simple color picker (cycle through common colors)
            var colors = new[] { "#000000", "#FF0000", "#0000FF", "#008000", "#800080", "#FF8C00" };
            int idx = Array.IndexOf(colors, TextColorHex.Text);
            TextColorHex.Text = colors[(idx + 1) % colors.Length];
        }

        private void BgColorPreview_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Toggle between transparent and white
            BgColorHex.Text = string.IsNullOrEmpty(BgColorHex.Text) ? "#FFFFFF" : "";
        }

        private void UpdatePreview()
        {
            try
            {
                PreviewText.FontFamily = new FontFamily(FontFamilyCombo.Text ?? "Arial");
                PreviewText.FontSize = FontSizeSlider.Value;
                PreviewText.FontWeight = WeightBold.IsChecked == true ? FontWeights.Bold :
                                         WeightMedium.IsChecked == true ? FontWeights.Medium : FontWeights.Regular;
                PreviewText.FontStyle = ItalicCheck.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;

                if (!string.IsNullOrEmpty(TextColorHex.Text))
                {
                    PreviewText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(TextColorHex.Text));
                }

                // Rotation
                float rotation = Rot90.IsChecked == true ? 90 :
                                 Rot180.IsChecked == true ? 180 :
                                 Rot270.IsChecked == true ? 270 : 0;
                PreviewText.RenderTransform = new RotateTransform(rotation);
                PreviewText.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            catch { }
        }

        private void NotifyChange()
        {
            if (SelectedSlot == null) return;

            var updated = SelectedSlot with
            {
                FontFamily = FontFamilyCombo.Text ?? "Arial",
                FontSize = (float)FontSizeSlider.Value,
                FontColorHex = TextColorHex.Text,
                Rotation = Rot90.IsChecked == true ? 90 :
                           Rot180.IsChecked == true ? 180 :
                           Rot270.IsChecked == true ? 270 : 0,
                Align = AlignLeft.IsChecked == true ? TextAlign.Left :
                        AlignRight.IsChecked == true ? TextAlign.Right : TextAlign.Center
            };

            SlotChanged?.Invoke(this, updated);
        }

        /// <summary>
        /// Gets the current styling as a new SlotSpec.
        /// </summary>
        public SlotSpec? GetCurrentStyle()
        {
            if (SelectedSlot == null) return null;

            return SelectedSlot with
            {
                FontFamily = FontFamilyCombo.Text ?? "Arial",
                FontSize = (float)FontSizeSlider.Value,
                FontColorHex = TextColorHex.Text,
                Rotation = Rot90.IsChecked == true ? 90 :
                           Rot180.IsChecked == true ? 180 :
                           Rot270.IsChecked == true ? 270 : 0,
                Align = AlignLeft.IsChecked == true ? TextAlign.Left :
                        AlignRight.IsChecked == true ? TextAlign.Right : TextAlign.Center,
                CopyStyles = new[]
                {
                    new CopyStyle(OriginalLabelCheck.IsChecked == true ? "أصل" : "", "#000000", (float)(OpacitySlider.Value / 100)),
                    new CopyStyle(Copy1LabelCheck.IsChecked == true ? "صورة 1" : "", "#FF0000", 0.9f),
                    new CopyStyle(Copy2LabelCheck.IsChecked == true ? "صورة 2" : "", "#0000FF", 0.85f),
                    new CopyStyle(Copy3LabelCheck.IsChecked == true ? "صورة 3" : "", "#808080", 0.8f)
                }
            };
        }
    }
}
