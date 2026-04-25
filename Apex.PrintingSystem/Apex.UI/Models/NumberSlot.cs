using System;
using Apex.NumberedBooksEngine.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.UI.Models
{
    /// <summary>
    /// UI representation of a numbering slot with editable properties and helpers to map to engine SlotSpec.
    /// </summary>
    public partial class NumberSlot : ObservableObject
    {
        [ObservableProperty] private string id = Guid.NewGuid().ToString("N");
        [ObservableProperty] private float x;
        [ObservableProperty] private float y;
        [ObservableProperty] private float width = 0.1f;
        [ObservableProperty] private float height = 0.05f;
        [ObservableProperty] private string previewNumber = "0000";
        [ObservableProperty] private string fontFamily = "Arial";
        [ObservableProperty] private float fontSize = 24;
        [ObservableProperty] private string fontColor = "#000000";
        [ObservableProperty] private bool isBold;
        [ObservableProperty] private double rotation;
        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private double opacity = 1.0;
        [ObservableProperty] private string alignment = "Left";

        public SlotSpec ToSlotSpec()
        {
            var alignEnum = Alignment?.ToLowerInvariant() switch
            {
                "center" => TextAlign.Center,
                "right" => TextAlign.Right,
                _ => TextAlign.Left
            };

            return new SlotSpec(
                Id: Id,
                X: X,
                Y: Y,
                Width: Width,
                Height: Height,
                FontFamily: FontFamily,
                FontSize: FontSize,
                FontColorHex: FontColor,
                Align: alignEnum,
                Rotation: (float)Rotation,
                CopyStyles: null);
        }

        public static NumberSlot FromSlotSpec(SlotSpec spec)
        {
            return new NumberSlot
            {
                Id = spec.Id,
                X = spec.X,
                Y = spec.Y,
                Width = spec.Width,
                Height = spec.Height,
                FontFamily = spec.FontFamily,
                FontSize = spec.FontSize,
                FontColor = spec.FontColorHex,
                Rotation = spec.Rotation,
                Alignment = spec.Align switch
                {
                    TextAlign.Center => "Center",
                    TextAlign.Right => "Right",
                    _ => "Left"
                },
                PreviewNumber = "0000",
                IsBold = false,
                Opacity = 1.0
            };
        }
    }
}

