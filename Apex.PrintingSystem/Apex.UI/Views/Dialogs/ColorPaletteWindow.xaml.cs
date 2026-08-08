using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Apex.UI.Views.Dialogs
{
    public partial class ColorPaletteWindow : Window
    {
        public string SelectedColor { get; private set; } = "#000000";

        private readonly List<string> _presetColors = new()
        {
            "#000000", "#FFFFFF", "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#FF00FF", "#00FFFF",
            "#800000", "#008000", "#000080", "#808000", "#800080", "#008080", "#C0C0C0", "#808080",
            "#FFA500", "#FFC0CB", "#A52A2A", "#FFD700", "#4B0082", "#9400D3", "#00008B", "#008B8B",
            "#B8860B", "#A9A9A9", "#006400", "#8B008B", "#556B2F", "#FF8C00", "#9932CC", "#8B0000"
        };

        public ColorPaletteWindow(string currentColor = "#000000")
        {
            InitializeComponent();
            SelectedColor = currentColor;
            InitializePresetColors();
            UpdateColorFromHex(currentColor);
        }

        private void InitializePresetColors()
        {
            var colors = _presetColors.Select(hex =>
            {
                try
                {
                    return (SolidColorBrush)new BrushConverter().ConvertFrom(hex);
                }
                catch
                {
                    return new SolidColorBrush(Colors.Black);
                }
            }).ToList();

            PresetColorsGrid.ItemsSource = colors;
        }

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Background is SolidColorBrush brush)
            {
                var color = brush.Color;
                SelectedColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                UpdateColorFromHex(SelectedColor);
            }
        }

        private void ColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateColorFromSliders();
        }

        private void UpdateColorFromSliders()
        {
            byte r = (byte)RedSlider.Value;
            byte g = (byte)GreenSlider.Value;
            byte b = (byte)BlueSlider.Value;

            var color = Color.FromRgb(r, g, b);
            SelectedColor = $"#{r:X2}{g:X2}{b:X2}";

            ColorPreview.Background = new SolidColorBrush(color);
            HexInput.Text = SelectedColor;
        }

        private void UpdateColorFromHex(string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                RedSlider.Value = color.R;
                GreenSlider.Value = color.G;
                BlueSlider.Value = color.B;
                ColorPreview.Background = new SolidColorBrush(color);
                HexInput.Text = hex;
            }
            catch
            {
                // Invalid hex, ignore
            }
        }

        private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                var text = textBox.Text;
                if (text.StartsWith("#") && text.Length == 7)
                {
                    try
                    {
                        UpdateColorFromHex(text);
                        SelectedColor = text;
                    }
                    catch
                    {
                        // Invalid hex, ignore
                    }
                }
            }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
