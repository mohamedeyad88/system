using SkiaSharp;
using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;

namespace Apex.NumberedBooksEngine.Core
{
    public class Composer
    {
        private readonly TemplateLoader _templateLoader;
        private readonly PatchGenerator _patchGenerator;

        public Composer()
        {
            _templateLoader = new TemplateLoader();
            _patchGenerator = new PatchGenerator();
        }

        /// <summary>
        /// Composes a single page with numbers rendered on slots.
        /// </summary>
        public SKImage ComposePage(SKImage template, long[] numbers, BookJobOptions options, int copyIndex)
        {
            return ComposePageWithCopyType(template, numbers, options, (CopyType)copyIndex);
        }

        /// <summary>
        /// Composes a page for a specific copy type (Original, Copy1, etc.).
        /// </summary>
        public SKImage ComposePageWithCopyType(SKImage template, long[] numbers, BookJobOptions options, CopyType copyType)
        {
            using var surface = SKSurface.Create(new SKImageInfo(template.Width, template.Height));
            var canvas = surface.Canvas;

            // Draw template
            canvas.DrawImage(template, 0, 0);

            // Draw slots
            for (int i = 0; i < numbers.Length; i++)
            {
                long number = numbers[i];
                if (number < 0) continue; // Empty slot

                if (i >= options.Slots.Count) break;
                var slot = options.Slots[i];

                // Determine copy style from slot or use defaults
                CopyStyle? style = GetCopyStyleForType(slot, copyType);

                // Generate display text with optional label
                string displayText = FormatNumberWithLabel(number, style);

                // Generate patch
                using var patch = _patchGenerator.GeneratePatch(displayText, slot, style);

                // Calculate position
                float x = slot.X * template.Width;
                float y = slot.Y * template.Height;

                // Adjust for alignment
                if (slot.Align == TextAlign.Center)
                {
                    x += (slot.Width * template.Width - patch.Width) / 2.0f;
                }
                else if (slot.Align == TextAlign.Right)
                {
                    x += slot.Width * template.Width - patch.Width;
                }

                // Apply rotation if needed
                if (slot.Rotation != 0)
                {
                    canvas.Save();
                    float centerX = x + patch.Width / 2;
                    float centerY = y + patch.Height / 2;
                    canvas.RotateDegrees(slot.Rotation, centerX, centerY);
                    canvas.DrawImage(patch, x, y);
                    canvas.Restore();
                }
                else
                {
                    canvas.DrawImage(patch, x, y);
                }
            }

            return surface.Snapshot();
        }

        /// <summary>
        /// Generates all pages for multi-copy printing.
        /// Yields (pageImage, copyType) for each page/copy combination.
        /// </summary>
        public IEnumerable<(SKImage Page, CopyType CopyType)> GenerateMultiCopyPages(
            SKImage template,
            BookJobOptions options,
            IEnumerable<CopyType> copyTypes)
        {
            var strategy = NumberingStrategyFactory.Create(options);

            foreach (var pageNumbers in strategy.GeneratePageNumbers(options))
            {
                foreach (var copyType in copyTypes)
                {
                    var page = ComposePageWithCopyType(template, pageNumbers, options, copyType);
                    yield return (page, copyType);
                }
            }
        }

        /// <summary>
        /// Generates pages for streaming print (numbers only, template cached).
        /// Returns number text and position data for overlay printing.
        /// </summary>
        public IEnumerable<PageOverlayData> GenerateOverlayData(BookJobOptions options)
        {
            var strategy = NumberingStrategyFactory.Create(options);

            foreach (var pageNumbers in strategy.GeneratePageNumbers(options))
            {
                var overlays = new List<SlotOverlay>();

                for (int i = 0; i < pageNumbers.Length; i++)
                {
                    if (pageNumbers[i] < 0 || i >= options.Slots.Count) continue;

                    var slot = options.Slots[i];
                    var style = slot.CopyStyles?.Length > 0 ? slot.CopyStyles[0] : null;

                    overlays.Add(new SlotOverlay(
                        SlotIndex: i,
                        Number: pageNumbers[i],
                        DisplayText: FormatNumberWithLabel(pageNumbers[i], style),
                        X: slot.X,
                        Y: slot.Y,
                        Width: slot.Width,
                        Height: slot.Height,
                        FontFamily: slot.FontFamily,
                        FontSize: slot.FontSize,
                        ColorHex: style?.ColorHex ?? slot.FontColorHex,
                        Align: slot.Align,
                        Rotation: slot.Rotation
                    ));
                }

                yield return new PageOverlayData(overlays);
            }
        }

        // ============ NEW API: PageAssignment-based Rendering ============

        /// <summary>
        /// Composes a page using PageAssignment (unified source for Preview and Print).
        /// This ensures preview matches printed output exactly.
        /// </summary>
        public SKImage ComposePageFromAssignment(
            SKImage template, 
            PageAssignment assignment, 
            IReadOnlyList<SlotSpec> slots,
            CopyType copyType = CopyType.Original)
        {
            using var surface = SKSurface.Create(new SKImageInfo(template.Width, template.Height));
            var canvas = surface.Canvas;

            // Draw template
            canvas.DrawImage(template, 0, 0);

            // Draw each assigned slot
            foreach (var slotAssignment in assignment.SlotNumbers)
            {
                var slot = FindSlotById(slots, slotAssignment.SlotId);
                if (slot == null) continue;

                CopyStyle? style = GetCopyStyleForType(slot, copyType);
                string displayText = FormatNumberWithLabel(slotAssignment.Number, style);

                using var patch = _patchGenerator.GeneratePatch(displayText, slot, style);

                float x = slot.X * template.Width;
                float y = slot.Y * template.Height;

                // Alignment adjustment
                if (slot.Align == TextAlign.Center)
                    x += (slot.Width * template.Width - patch.Width) / 2.0f;
                else if (slot.Align == TextAlign.Right)
                    x += slot.Width * template.Width - patch.Width;

                // Rotation
                if (slot.Rotation != 0)
                {
                    canvas.Save();
                    float centerX = x + patch.Width / 2;
                    float centerY = y + patch.Height / 2;
                    canvas.RotateDegrees(slot.Rotation, centerX, centerY);
                    canvas.DrawImage(patch, x, y);
                    canvas.Restore();
                }
                else
                {
                    canvas.DrawImage(patch, x, y);
                }
            }

            return surface.Snapshot();
        }

        /// <summary>
        /// Generates overlay data from PageAssignment (for streaming print).
        /// </summary>
        public PageOverlayData CreateOverlayFromAssignment(
            PageAssignment assignment,
            IReadOnlyList<SlotSpec> slots)
        {
            var overlays = new List<SlotOverlay>();

            foreach (var slotAssignment in assignment.SlotNumbers)
            {
                var slot = FindSlotById(slots, slotAssignment.SlotId);
                if (slot == null) continue;

                var style = slot.CopyStyles?.Length > 0 ? slot.CopyStyles[0] : null;

                overlays.Add(new SlotOverlay(
                    SlotIndex: IndexOfSlot(slots, slot),
                    Number: slotAssignment.Number,
                    DisplayText: FormatNumberWithLabel(slotAssignment.Number, style),
                    X: slot.X,
                    Y: slot.Y,
                    Width: slot.Width,
                    Height: slot.Height,
                    FontFamily: slot.FontFamily,
                    FontSize: slot.FontSize,
                    ColorHex: style?.ColorHex ?? slot.FontColorHex,
                    Align: slot.Align,
                    Rotation: slot.Rotation
                ));
            }

            return new PageOverlayData(overlays);
        }

        private static SlotSpec? FindSlotById(IReadOnlyList<SlotSpec> slots, string slotId)
        {
            foreach (var slot in slots)
            {
                if (slot.Id == slotId) return slot;
            }
            return null;
        }

        private static int IndexOfSlot(IReadOnlyList<SlotSpec> slots, SlotSpec target)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Id == target.Id) return i;
            }
            return -1;
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
            string numStr = number.ToString("D4"); // 4-digit format

            if (style != null && !string.IsNullOrEmpty(style.Label))
            {
                return $"{numStr} / {style.Label}";
            }

            return numStr;
        }
    }

    /// <summary>
    /// Overlay data for a single page (used in streaming print).
    /// </summary>
    public record PageOverlayData(IReadOnlyList<SlotOverlay> Overlays);

    /// <summary>
    /// Overlay data for a single slot.
    /// </summary>
    public record SlotOverlay(
        int SlotIndex,
        long Number,
        string DisplayText,
        float X, float Y,
        float Width, float Height,
        string FontFamily,
        float FontSize,
        string ColorHex,
        TextAlign Align,
        float Rotation
    );
}

