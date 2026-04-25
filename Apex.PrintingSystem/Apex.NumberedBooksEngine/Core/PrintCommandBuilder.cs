using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Slot overlay command for streaming print.
    /// </summary>
    public record SlotOverlayCommand(
        string SlotId,
        float NormalizedX,
        float NormalizedY,
        string Text,
        string FontFamily,
        int FontSize,
        string ColorHex,
        string? Label = null  // Copy label (e.g., "Original", "Copy")
    );

    /// <summary>
    /// Page print command for streaming.
    /// </summary>
    public record PagePrintCommand(
        string TemplateId,
        IReadOnlyList<SlotOverlayCommand> Slots,
        int Copies
    );

    /// <summary>
    /// Builds print commands for different printer languages.
    /// </summary>
    public class PrintCommandBuilder
    {
        /// <summary>
        /// Builds PCL macro execution command with text overlays.
        /// </summary>
        public byte[] BuildPclMacroCommand(PagePrintCommand command, int macroId)
        {
            var sb = new StringBuilder();

            // Execute the stored macro
            sb.Append($"\x1b&f{macroId}Y"); // Call macro

            // Add text overlays
            foreach (var slot in command.Slots)
            {
                // Position cursor (PCL uses decipoints: 1/720 inch)
                int xDecipoints = (int)(slot.NormalizedX * 8.5 * 720); // Normalized -> Inches -> Decipoints
                int yDecipoints = (int)(slot.NormalizedY * 11.0 * 720);

                sb.Append($"\x1b*p{xDecipoints}x{yDecipoints}Y"); // Position
                sb.Append($"\x1b(s{slot.FontSize}V"); // Font size in points
                sb.Append(slot.Text);
            }

            // Form feed
            sb.Append("\f");

            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        /// <summary>
        /// Builds PostScript form execution command with text overlays.
        /// </summary>
        public byte[] BuildPostScriptFormCommand(PagePrintCommand command, string formName)
        {
            var sb = new StringBuilder();

            // Begin page
            sb.AppendLine("%%Page: 1 1");
            sb.AppendLine("gsave");

            // Execute stored form
            sb.AppendLine($"{formName} execform");

            // Add text overlays
            foreach (var slot in command.Slots)
            {
                // Parse color
                var color = ParseHexColor(slot.ColorHex);

                sb.AppendLine($"/{slot.FontFamily} findfont {slot.FontSize} scalefont setfont");
                sb.AppendLine($"{color.R / 255.0:F3} {color.G / 255.0:F3} {color.B / 255.0:F3} setrgbcolor");
                // PostScript uses points (1/72 inch). Assuming standard page.
                float xPts = slot.NormalizedX * 612; // 8.5 * 72
                float yPts = slot.NormalizedY * 792; // 11 * 72
                sb.AppendLine($"{xPts:F2} {yPts:F2} moveto");
                sb.AppendLine($"({EscapePostScriptString(slot.Text)}) show");
            }

            sb.AppendLine("grestore");
            sb.AppendLine("showpage");

            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        /// <summary>
        /// Builds GDI draw commands (as a structured object for GdiSpoolPrinter).
        /// </summary>
        public PagePrintCommand BuildGdiCommand(string templateId, long[] pageNumbers, IReadOnlyList<SlotSpec> slots, int dpi)
        {
            var overlays = new List<SlotOverlayCommand>();

            for (int i = 0; i < slots.Count && i < pageNumbers.Length; i++)
            {
                var slot = slots[i];
                var number = pageNumbers[i];

                if (number < 0) continue; // Skip empty slots

                overlays.Add(new SlotOverlayCommand(
                    SlotId: slot.Id,
                    NormalizedX: slot.X,
                    NormalizedY: slot.Y,
                    Text: number.ToString().PadLeft(6, '0'),
                    FontFamily: slot.FontFamily,
                    FontSize: (int)slot.FontSize,
                    ColorHex: slot.FontColorHex
                ));
            }

            return new PagePrintCommand(templateId, overlays, 1);
        }

        /// <summary>
        /// Builds GDI command with copy-specific styling (label, color).
        /// </summary>
        public PagePrintCommand BuildGdiCommandWithCopyStyle(
            string templateId, 
            long[] pageNumbers, 
            IReadOnlyList<SlotSpec> slots, 
            int dpi,
            CopyType copyType)
        {
            var overlays = new List<SlotOverlayCommand>();

            for (int i = 0; i < slots.Count && i < pageNumbers.Length; i++)
            {
                var slot = slots[i];
                var number = pageNumbers[i];
                if (number < 0) continue;

                // ═══════════════════════════════════════════════════════════════════
                // DIAGNOSTIC LOGGING: Track number assignment to slot
                // ═══════════════════════════════════════════════════════════════════
                System.Diagnostics.Debug.WriteLine($"[PrintCommandBuilder] Assigning number {number} to slot {slot.Id} (index {i})");

                // Get copy-specific style
                var style = GetCopyStyleForType(slot, copyType);
                string displayText = FormatNumberWithLabel(number, style);
                string colorHex = style?.ColorHex ?? slot.FontColorHex;

                overlays.Add(new SlotOverlayCommand(
                    SlotId: slot.Id,
                    NormalizedX: slot.X,
                    NormalizedY: slot.Y,
                    Text: displayText,
                    FontFamily: slot.FontFamily,
                    FontSize: (int)slot.FontSize,
                    ColorHex: colorHex,
                    Label: style?.Label
                ));
            }

            return new PagePrintCommand(templateId, overlays, 1);
        }

        private CopyStyle? GetCopyStyleForType(SlotSpec slot, CopyType copyType)
        {
            if (slot.CopyStyles == null || slot.CopyStyles.Length == 0)
                return null;

            int index = (int)copyType;
            if (index < slot.CopyStyles.Length)
                return slot.CopyStyles[index];

            return slot.CopyStyles[0]; // Fallback to first style
        }

        private string FormatNumberWithLabel(long number, CopyStyle? style)
        {
            string numStr = number.ToString().PadLeft(6, '0');

            if (style != null && !string.IsNullOrEmpty(style.Label))
            {
                return $"{numStr} / {style.Label}";
            }

            return numStr;
        }

        /// <summary>
        /// Builds JSON representation for debugging/logging.
        /// </summary>
        public string BuildJsonCommand(PagePrintCommand command)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"type\": \"PrintPage\",");
            sb.AppendLine($"  \"templateId\": \"{command.TemplateId}\",");
            sb.AppendLine($"  \"slots\": [");

            for (int i = 0; i < command.Slots.Count; i++)
            {
                var slot = command.Slots[i];
                sb.Append($"    {{\"slotId\":\"{slot.SlotId}\",\"x\":{slot.NormalizedX:F4},\"y\":{slot.NormalizedY:F4},\"text\":\"{slot.Text}\",\"font\":\"{slot.FontFamily}\",\"size\":{slot.FontSize},\"color\":\"{slot.ColorHex}\"}}");
                if (i < command.Slots.Count - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine($"  ],");
            sb.AppendLine($"  \"copies\": {command.Copies}");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private (byte R, byte G, byte B) ParseHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return (0, 0, 0);

            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                return (
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)
                );
            }
            return (0, 0, 0);
        }

        private string EscapePostScriptString(string text)
        {
            return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }
    }
}
