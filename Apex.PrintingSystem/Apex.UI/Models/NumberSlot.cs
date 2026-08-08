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

        // What this slot prints: the number as text, or the same number encoded as a
        // scannable code. Stored as a string for easy ComboBox binding.
        [ObservableProperty] private string slotKind = "Text";       // Text | Barcode | QrCode
        [ObservableProperty] private string barcodeType = "CODE128";

        /// <summary>Kinds offered in the designer.</summary>
        public static string[] AvailableKinds { get; } = { "Text", "Barcode", "QrCode" };

        /// <summary>Symbologies offered for barcode slots.</summary>
        public static string[] AvailableBarcodeTypes { get; } = { "CODE128", "CODE39", "EAN13" };

        /// <summary>True when the barcode symbology picker is relevant.</summary>
        public bool IsBarcode => string.Equals(SlotKind, "Barcode", StringComparison.OrdinalIgnoreCase);

        partial void OnSlotKindChanged(string value) => OnPropertyChanged(nameof(IsBarcode));

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
                CopyStyles: null,
                Kind: SlotKind?.ToLowerInvariant() switch
                {
                    "barcode" => Apex.NumberedBooksEngine.Core.SlotKind.Barcode,
                    "qrcode" or "qr" => Apex.NumberedBooksEngine.Core.SlotKind.QrCode,
                    _ => Apex.NumberedBooksEngine.Core.SlotKind.Text
                },
                BarcodeType: BarcodeType);
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
                Opacity = 1.0,
                SlotKind = spec.Kind switch
                {
                    Apex.NumberedBooksEngine.Core.SlotKind.Barcode => "Barcode",
                    Apex.NumberedBooksEngine.Core.SlotKind.QrCode => "QrCode",
                    _ => "Text"
                },
                BarcodeType = spec.BarcodeType ?? "CODE128"
            };
        }
    }
}

