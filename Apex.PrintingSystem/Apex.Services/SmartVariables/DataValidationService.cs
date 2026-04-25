using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.SmartVariables
{
    public class ValidationOptions
    {
        public int MaxTextLength           { get; set; } = 500;
        public bool CheckImageFields       { get; set; } = true;
        public bool CheckRequiredFields    { get; set; } = true;
        public bool CheckImageDpi          { get; set; } = true;
        public int  MinImageDpi            { get; set; } = 72;
        public int  WarnImageDpi           { get; set; } = 150;  // warn below this, error below MinImageDpi
        public bool WarnOnEmptyOptional    { get; set; } = false;
    }

    public interface IDataValidationService
    {
        void ValidateAll(
            SmartDataSource        source,
            List<VariableMapping>  mappings,
            ValidationOptions?     options = null);

        void ValidateRow(
            SmartDataRow           row,
            List<VariableMapping>  mappings,
            ValidationOptions      options);
    }

    public class DataValidationService : IDataValidationService
    {
        public void ValidateAll(
            SmartDataSource       source,
            List<VariableMapping> mappings,
            ValidationOptions?    options = null)
        {
            options ??= new ValidationOptions();

            foreach (var row in source.Rows)
            {
                // Reset previous validation results (re-validate fresh)
                row.Errors.Clear();
                row.Warnings.Clear();
                row.Status = RowStatus.Valid;

                ValidateRow(row, mappings, options);
            }
        }

        public void ValidateRow(
            SmartDataRow          row,
            List<VariableMapping> mappings,
            ValidationOptions     options)
        {
            foreach (var mapping in mappings)
            {
                if (!mapping.IsMapped) continue;

                string colName = mapping.ColumnName!;
                string value   = row.Get(colName);

                // ── Required field check ───────────────────────────────────────
                if (options.CheckRequiredFields && mapping.IsRequired)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        row.Errors.Add($"الحقل المطلوب '{mapping.FieldLabel}' فارغ.");
                        row.Status = RowStatus.Error;
                        continue; // no point validating further for this field
                    }
                }

                // ── Text length check ──────────────────────────────────────────
                if (mapping.FieldType == SmartFieldType.TextVariable
                    || mapping.FieldType == SmartFieldType.NumberVariable
                    || mapping.FieldType == SmartFieldType.DateVariable)
                {
                    if (value.Length > options.MaxTextLength)
                    {
                        row.Warnings.Add(
                            $"قيمة الحقل '{mapping.FieldLabel}' طويلة جداً ({value.Length} حرف، الحد الأقصى {options.MaxTextLength}).");
                        if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
                    }
                }

                // ── Date format check ──────────────────────────────────────────
                if (mapping.FieldType == SmartFieldType.DateVariable
                    && !string.IsNullOrEmpty(value))
                {
                    if (!TryParseDate(value))
                    {
                        row.Warnings.Add(
                            $"قيمة الحقل '{mapping.FieldLabel}' لا تبدو تاريخاً صحيحاً: \"{value}\".");
                        if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
                    }
                }

                // ── Number format check ────────────────────────────────────────
                if (mapping.FieldType == SmartFieldType.NumberVariable
                    && !string.IsNullOrEmpty(value))
                {
                    if (!IsNumericString(value))
                    {
                        row.Warnings.Add(
                            $"قيمة الحقل '{mapping.FieldLabel}' ليست رقماً: \"{value}\".");
                        if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
                    }
                }

                // ── Image field: check resolution ──────────────────────────────
                if (options.CheckImageFields
                    && mapping.FieldType == SmartFieldType.ImageVariable
                    && row.ImageStatus == ImageStatus.Found
                    && !string.IsNullOrEmpty(row.ResolvedImagePath))
                {
                    CheckImageDpi(row, row.ResolvedImagePath, mapping.FieldLabel, options);
                }

                // ── Optional empty warning ─────────────────────────────────────
                if (options.WarnOnEmptyOptional
                    && !mapping.IsRequired
                    && string.IsNullOrWhiteSpace(value))
                {
                    row.Warnings.Add($"الحقل الاختياري '{mapping.FieldLabel}' فارغ.");
                    if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
                }
            }

            // ── Image missing check ────────────────────────────────────────────
            if (options.CheckImageFields)
            {
                if (row.ImageStatus == ImageStatus.Missing)
                {
                    row.Errors.Add("الصورة مطلوبة لهذا السجل ولم يتم العثور عليها.");
                    row.Status = RowStatus.Error;
                }
                else if (row.ImageStatus == ImageStatus.Corrupted)
                {
                    row.Errors.Add("ملف الصورة تالف أو لا يمكن فتحه.");
                    row.Status = RowStatus.Error;
                }
                else if (row.ImageStatus == ImageStatus.UnsupportedFormat)
                {
                    row.Warnings.Add("تنسيق الصورة غير مدعوم.");
                    if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
                }
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy",
            "d/M/yyyy",   "yyyy/MM/dd", "dd-MM-yyyy",
            "dd.MM.yyyy", "yyyy.MM.dd",
            // Hijri-ish numeric patterns (treated as string, just validated shape)
            "d/M/yy",     "dd/MM/yy",
        };

        private static bool TryParseDate(string value)
        {
            return DateTime.TryParseExact(
                       value, DateFormats,
                       System.Globalization.CultureInfo.InvariantCulture,
                       System.Globalization.DateTimeStyles.None,
                       out _)
                || DateTime.TryParse(value, out _);
        }

        // Accepts integers and decimals, with optional leading sign, commas as thousands separator
        private static readonly Regex NumericRegex =
            new(@"^[+-]?\d{1,3}(,\d{3})*(\.\d+)?$|^[+-]?\d+(\.\d+)?$", RegexOptions.Compiled);

        private static bool IsNumericString(string value)
            => NumericRegex.IsMatch(value.Trim());

        private static void CheckImageDpi(
            SmartDataRow      row,
            string            imagePath,
            string            fieldLabel,
            ValidationOptions options)
        {
            try
            {
                using var bmp = new System.Drawing.Bitmap(imagePath);
                float dpiX = bmp.HorizontalResolution;
                float dpiY = bmp.VerticalResolution;
                float dpi  = Math.Min(dpiX, dpiY);

                if (dpi < options.MinImageDpi)
                {
                    row.Errors.Add(
                        $"صورة الحقل '{fieldLabel}' دقتها منخفضة جداً ({dpi:F0} DPI). الحد الأدنى {options.MinImageDpi} DPI.");
                    row.Status = RowStatus.Error;
                }
                else if (dpi < options.WarnImageDpi)
                {
                    row.Warnings.Add(
                        $"صورة الحقل '{fieldLabel}' دقتها منخفضة ({dpi:F0} DPI). يُنصح بـ {options.WarnImageDpi} DPI أو أعلى.");
                    if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
                }
            }
            catch
            {
                // If we can't load, it's already flagged as Corrupted by ImageMatchingService
            }
        }
    }
}
