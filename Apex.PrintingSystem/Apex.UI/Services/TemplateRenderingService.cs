using Apex.Services.SmartVariables;
using Apex.Services.SmartVariables.Models;
using Apex.Services.Templates;
using Apex.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace Apex.UI.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    public interface ITemplateRenderingService
    {
        /// <summary>
        /// Merges a template page definition with one data record and returns a
        /// fully-resolved <see cref="RenderedTemplate"/> ready for display and export.
        /// The same service instance is used by both live preview and PNG export,
        /// guaranteeing WYSIWYG.
        /// </summary>
        RenderedTemplate Render(
            TemplatePageDefinition page,
            SmartDataSource dataSource,
            List<VariableMapping> mappings,
            int recordIndex,
            Dictionary<string, byte[]> assets,
            string? imageFolderPath = null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Produces a <see cref="RenderedTemplate"/> for a single record.
    ///
    /// Field-type rendering rules:
    /// • Text / Number / Date  — value from mapped column; date FormatString applied when valid.
    /// • Counter               — recordIndex+1 formatted by optional FormatString.
    /// • Image                 — resolved from: template asset → ResolvedImagePath → folder+column.
    ///                           Shows [صورة ناقصة] when none found.
    /// • QrCode / Barcode      — shows mapped value or placeholder; real encoder deferred.
    /// • Unmapped required     — shows [قيمة ناقصة] with error border.
    /// • Unmapped optional     — shows DefaultValue or [غير مربوط] with error border.
    /// </summary>
    public class TemplateRenderingService : ITemplateRenderingService
    {
        public RenderedTemplate Render(
            TemplatePageDefinition page,
            SmartDataSource dataSource,
            List<VariableMapping> mappings,
            int recordIndex,
            Dictionary<string, byte[]> assets,
            string? imageFolderPath = null)
        {
            var result = new RenderedTemplate
            {
                WidthMm = page.WidthMm,
                HeightMm = page.HeightMm,
                BackgroundColor = page.BackgroundColor ?? "#FFFFFF",
                RecordIndex = recordIndex,
                TotalRecords = dataSource.Rows.Count,
            };

            // Background image from embedded template assets
            if (!string.IsNullOrEmpty(page.BackgroundImageAssetId) &&
                assets.TryGetValue(page.BackgroundImageAssetId, out var bgBytes))
            {
                result.BackgroundImage = LoadBitmapFromBytes(bgBytes);
            }

            // Data row — null when no data has been pasted yet
            var row = dataSource.Rows.Count > 0
                ? dataSource.Rows[Math.Clamp(recordIndex, 0, dataSource.Rows.Count - 1)]
                : null;

            foreach (var slot in page.Slots ?? Enumerable.Empty<TemplateSlotDefinition>())
            {
                // Conditional rules decide whether this slot prints for THIS record.
                // Evaluated here, in the single place every output path goes through,
                // so the preview, the PNG export and the press all agree.
                if (!ShouldPrintSlot(slot, row)) continue;

                var field = new RenderedFieldItem
                {
                    FieldId = slot.Id,
                    FieldType = slot.DataType,
                    X = slot.X,
                    Y = slot.Y,
                    Width = slot.Width,
                    Height = slot.Height,
                    RotationDegrees = slot.RotationDegrees,
                    FontFamily = slot.FontFamily ?? "Tahoma",
                    FontSize = slot.FontSize,
                    Bold = slot.Bold,
                    Italic = slot.Italic,
                    TextColor = slot.TextColor ?? "#000000",
                    TextAlign = slot.TextAlign,
                    IsRtl = slot.IsRtl,
                    ImageFitMode = slot.ImageFitMode ?? "Contain",
                };

                var mapping = mappings.FirstOrDefault(m => m.FieldId == slot.Id);

                switch (slot.DataType)
                {
                    case SlotDataType.Text:
                    case SlotDataType.Number:
                    case SlotDataType.Date:
                        RenderTextSlot(field, slot, mapping, row);
                        break;

                    case SlotDataType.Counter:
                        RenderCounter(field, slot, recordIndex);
                        break;

                    case SlotDataType.Image:
                        RenderImageSlot(field, slot, mapping, row, assets, imageFolderPath);
                        break;

                    case SlotDataType.QrCode:
                        RenderCodeSlot(field, slot, mapping, row, "QR");
                        break;

                    case SlotDataType.Barcode:
                        RenderCodeSlot(field, slot, mapping, row, slot.BarcodeType);
                        break;
                }

                result.Fields.Add(field);
            }

            return result;
        }

        /// <summary>
        /// Whether a slot's conditions are satisfied by the current record.
        ///
        /// A slot with no rules always prints, so existing templates are untouched.
        /// While designing — before any data is pasted — rules are NOT applied: hiding
        /// fields on an empty data set would leave the designer looking at a blank
        /// page and no way to select what it is trying to lay out.
        /// </summary>
        private static bool ShouldPrintSlot(TemplateSlotDefinition slot, SmartDataRow? row)
        {
            if (slot.Rules == null || slot.Rules.Count == 0) return true;
            if (row == null) return true;

            return FieldRuleEvaluator.ShouldPrint(slot.Rules, row.Values!);
        }

        // ── Text / Number / Date ──────────────────────────────────────────────

        private static void RenderTextSlot(
            RenderedFieldItem field,
            TemplateSlotDefinition slot,
            VariableMapping? mapping,
            SmartDataRow? row)
        {
            // No data pasted → design-time placeholder
            if (row == null)
            {
                field.RenderedText = !string.IsNullOrEmpty(slot.DefaultValue)
                    ? slot.DefaultValue
                    : $"«{(string.IsNullOrWhiteSpace(slot.Name) ? slot.VariableName : slot.Name)}»";
                return;
            }

            // No column bound
            if (mapping == null || !mapping.IsMapped)
            {
                if (!string.IsNullOrEmpty(slot.DefaultValue))
                    field.RenderedText = slot.DefaultValue;
                else
                {
                    field.Error = FieldRenderError.NotMapped;
                    field.RenderedText = field.ErrorMessage;
                }
                return;
            }

            string value = row.Get(mapping.ColumnName!, mapping.DefaultValue ?? "");

            if (string.IsNullOrEmpty(value))
            {
                if (slot.Required)
                {
                    field.Error = FieldRenderError.MissingValue;
                    field.RenderedText = field.ErrorMessage;
                }
                else
                {
                    field.RenderedText = mapping.DefaultValue ?? "";
                }
                return;
            }

            // Apply date format string (silently fall back to raw value on error)
            if (slot.DataType == SlotDataType.Date && !string.IsNullOrEmpty(slot.FormatString))
            {
                if (DateTime.TryParse(value, out var dt))
                {
                    try { value = dt.ToString(slot.FormatString); }
                    catch { /* keep raw value */ }
                }
            }

            field.RenderedText = value;
        }

        // ── Counter ───────────────────────────────────────────────────────────

        private static void RenderCounter(
            RenderedFieldItem field,
            TemplateSlotDefinition slot,
            int recordIndex)
        {
            string text = (recordIndex + 1).ToString();
            if (!string.IsNullOrEmpty(slot.FormatString))
                try { text = (recordIndex + 1).ToString(slot.FormatString); } catch { }
            field.RenderedText = text;
        }

        // ── QR / Barcode ──────────────────────────────────────────────────────

        /// <summary>Render codes at 300 DPI so they stay scannable in print.</summary>
        private const double CodeDpi = 300.0;
        private const int MaxCodePx = 2000;

        /// <summary>
        /// Encodes the slot's value into a real, scannable symbol via
        /// <see cref="CodeRenderer"/> and puts the bitmap on the field. Falls back to
        /// drawing the raw value as text when there is no value or the content is not
        /// valid for the chosen symbology (e.g. EAN-13 with non-numeric data), so the
        /// operator can still see and fix the data.
        /// </summary>
        private static void RenderCodeSlot(
            RenderedFieldItem field,
            TemplateSlotDefinition slot,
            VariableMapping? mapping,
            SmartDataRow? row,
            string? symbology)
        {
            string? content = GetMappedValue(mapping, row) ?? slot.DefaultValue;

            if (string.IsNullOrWhiteSpace(content))
            {
                // Nothing to encode: required → error, otherwise a design-time hint.
                if (slot.Required)
                {
                    field.Error = FieldRenderError.MissingValue;
                    field.RenderedText = field.ErrorMessage;
                }
                else
                {
                    field.RenderedText = $"«{(string.IsNullOrWhiteSpace(slot.Name) ? slot.VariableName : slot.Name)}»";
                }
                return;
            }

            int widthPx = MmToPx(slot.Width);
            int heightPx = MmToPx(slot.Height);
            byte[]? png = CodeRenderer.TryRenderPng(content, symbology, widthPx, heightPx);

            if (png != null)
            {
                field.ImageSource = LoadBitmapFromBytes(png);
                if (field.ImageSource != null)
                {
                    // Keep the encoded value so the operator can verify it.
                    field.RenderedText = content;
                    return;
                }
            }

            // Could not encode → show the value as text rather than an empty box.
            field.Error = FieldRenderError.InvalidFormat;
            field.RenderedText = content;
        }

        private static int MmToPx(double mm) =>
            Math.Clamp((int)Math.Round(mm * CodeDpi / 25.4), 1, MaxCodePx);

        // ── Image ─────────────────────────────────────────────────────────────

        private static void RenderImageSlot(
            RenderedFieldItem field,
            TemplateSlotDefinition slot,
            VariableMapping? mapping,
            SmartDataRow? row,
            Dictionary<string, byte[]> assets,
            string? imageFolderPath)
        {
            // Priority 1: static asset embedded in the template file
            if (!string.IsNullOrEmpty(slot.ImageAssetId) &&
                assets.TryGetValue(slot.ImageAssetId, out var assetBytes))
            {
                field.ImageSource = LoadBitmapFromBytes(assetBytes);
                if (field.ImageSource != null) return;
            }

            // Priority 2: resolved path from the Image Manager matching step
            if (!string.IsNullOrEmpty(row?.ResolvedImagePath) && File.Exists(row!.ResolvedImagePath))
            {
                field.ImageSource = LoadBitmapFromFile(row.ResolvedImagePath);
                if (field.ImageSource != null) return;
            }

            // Priority 3: imageFolderPath + mapped column value
            if (!string.IsNullOrEmpty(imageFolderPath) && mapping?.IsMapped == true && row != null)
            {
                string colValue = row.Get(mapping.ColumnName!, "");
                if (!string.IsNullOrEmpty(colValue))
                {
                    string? resolved = TryResolveImagePath(imageFolderPath, colValue);
                    if (resolved != null)
                    {
                        field.ImageSource = LoadBitmapFromFile(resolved);
                        if (field.ImageSource != null) return;
                    }
                }
            }

            // Nothing worked → clear error placeholder
            field.Error = FieldRenderError.MissingImage;
            field.RenderedText = field.ErrorMessage;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string? GetMappedValue(VariableMapping? mapping, SmartDataRow? row)
        {
            if (mapping?.IsMapped != true || row == null) return null;
            string v = row.Get(mapping.ColumnName!, "");
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static string? TryResolveImagePath(string folder, string fileNameOrValue)
        {
            string candidate = Path.Combine(folder, fileNameOrValue);
            if (File.Exists(candidate)) return candidate;

            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" })
            {
                string withExt = candidate + ext;
                if (File.Exists(withExt)) return withExt;
            }
            return null;
        }

        internal static BitmapImage? LoadBitmapFromBytes(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        internal static BitmapImage? LoadBitmapFromFile(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
