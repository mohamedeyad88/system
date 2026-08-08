using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.SmartVariables
{
    public class CleaningOptions
    {
        public bool TrimWhitespace { get; set; } = true;
        public bool NormalizeArabicLetters { get; set; } = true;  // أإآ → ا
        public bool RemoveDiacritics { get; set; } = false; // تشكيل
        public bool NormalizeArabicNumbers { get; set; } = true;  // ١٢٣ → 123
        public bool RemoveExtraSpaces { get; set; } = true;  // multi-space → single
        public bool UnifyQuotes { get; set; } = true;  // " " ' ' → " '
        public bool RemoveControlChars { get; set; } = true;
        public bool PreserveLeadingZeros { get; set; } = true;  // ALWAYS true — never convert to number
        public bool DetectAndFlagDuplicates { get; set; } = true;
        public List<string> DuplicateCheckColumns { get; set; } = new();
    }

    public class CleaningResult
    {
        public SmartDataSource Source { get; set; } = null!;
        public int RowsProcessed { get; set; }
        public int CellsModified { get; set; }
        public int DuplicatesFound { get; set; }
        public List<string> Log { get; set; } = new();
    }

    public interface IDataCleaningService
    {
        CleaningResult Clean(SmartDataSource source, CleaningOptions? options = null);
        string CleanCell(string value, CleaningOptions options);
    }

    public class DataCleaningService : IDataCleaningService
    {
        // Arabic diacritics Unicode range
        private static readonly Regex DiacriticsRegex =
            new(@"[ً-ٰٟـ]", RegexOptions.Compiled);

        // Multiple internal whitespace
        private static readonly Regex MultiSpaceRegex =
            new(@"[ \t]{2,}", RegexOptions.Compiled);

        // Arabic-Indic digits
        private static readonly Dictionary<char, char> ArabicIndicToWestern = new()
        {
            ['٠'] = '0',
            ['١'] = '1',
            ['٢'] = '2',
            ['٣'] = '3',
            ['٤'] = '4',
            ['٥'] = '5',
            ['٦'] = '6',
            ['٧'] = '7',
            ['٨'] = '8',
            ['٩'] = '9',
            // Extended Arabic-Indic (Farsi/Persian)
            ['۰'] = '0',
            ['۱'] = '1',
            ['۲'] = '2',
            ['۳'] = '3',
            ['۴'] = '4',
            ['۵'] = '5',
            ['۶'] = '6',
            ['۷'] = '7',
            ['۸'] = '8',
            ['۹'] = '9',
        };

        // Arabic alef normalization
        private static readonly Dictionary<char, char> ArabicAlefForms = new()
        {
            ['أ'] = 'ا',
            ['إ'] = 'ا',
            ['آ'] = 'ا',
            ['ٱ'] = 'ا',
        };

        // ── Public API ─────────────────────────────────────────────────────────

        public CleaningResult Clean(SmartDataSource source, CleaningOptions? options = null)
        {
            options ??= new CleaningOptions();
            var result = new CleaningResult
            {
                Source = source,
                RowsProcessed = source.Rows.Count,
            };

            // Step 1-8: Clean every cell value
            foreach (var row in source.Rows)
            {
                var keys = row.Values.Keys.ToList();
                foreach (var key in keys)
                {
                    string original = row.Values[key];
                    string cleaned = CleanCell(original, options);

                    if (!string.Equals(original, cleaned, StringComparison.Ordinal))
                    {
                        row.Values[key] = cleaned;
                        result.CellsModified++;
                    }
                }
            }

            // Step 9: Detect duplicate values in specified columns
            if (options.DetectAndFlagDuplicates && options.DuplicateCheckColumns.Count > 0)
            {
                result.DuplicatesFound = FlagDuplicates(source, options.DuplicateCheckColumns, result.Log);
            }

            result.Log.Add($"تم تنظيف {result.RowsProcessed} سجل، تم تعديل {result.CellsModified} خلية.");
            if (result.DuplicatesFound > 0)
                result.Log.Add($"تم اكتشاف {result.DuplicatesFound} قيمة مكررة.");

            return result;
        }

        /// <summary>
        /// Applies the cleaning pipeline to a single string value.
        /// NOTE: PreserveLeadingZeros = true means we NEVER cast to numeric type.
        /// All values stay as strings throughout.
        /// </summary>
        public string CleanCell(string value, CleaningOptions options)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // Step 1: Remove control characters (except \n \r \t)
            if (options.RemoveControlChars)
                value = RemoveControlChars(value);

            // Step 2: Trim leading/trailing whitespace
            if (options.TrimWhitespace)
                value = value.Trim();

            // Step 3: Normalize Arabic-Indic digits → Western digits (string form, no numeric conversion)
            if (options.NormalizeArabicNumbers)
                value = NormalizeArabicIndicDigits(value);

            // Step 4: Normalize Arabic alef forms (أإآ → ا)
            if (options.NormalizeArabicLetters)
                value = NormalizeArabicAlef(value);

            // Step 5: Remove Arabic diacritics (تشكيل) if requested
            if (options.RemoveDiacritics)
                value = DiacriticsRegex.Replace(value, "");

            // Step 6: Collapse multiple internal spaces into one
            if (options.RemoveExtraSpaces)
                value = MultiSpaceRegex.Replace(value, " ").Trim();

            // Step 7: Unify quotation marks
            if (options.UnifyQuotes)
                value = UnifyQuotes(value);

            // Step 8: Final trim (in case step 6 left edge spaces)
            if (options.TrimWhitespace)
                value = value.Trim();

            // NOTE: We deliberately do NOT parse numbers, dates, or booleans.
            // Storing "00125" as "00125" — never as 125. (PreserveLeadingZeros)

            return value;
        }

        // ── Step implementations ───────────────────────────────────────────────

        private static string RemoveControlChars(string value)
        {
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                // Allow printable, whitespace-like (\t, \n, \r, space)
                if (!char.IsControl(c) || c == '\t' || c == '\n' || c == '\r')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static string NormalizeArabicIndicDigits(string value)
        {
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(ArabicIndicToWestern.TryGetValue(c, out char w) ? w : c);
            }
            return sb.ToString();
        }

        private static string NormalizeArabicAlef(string value)
        {
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(ArabicAlefForms.TryGetValue(c, out char n) ? n : c);
            }
            return sb.ToString();
        }

        private static string UnifyQuotes(string value)
        {
            return value
                .Replace('“', '"')  // " LEFT DOUBLE QUOTATION MARK
                .Replace('”', '"')  // " RIGHT DOUBLE QUOTATION MARK
                .Replace('‘', '\'') // ' LEFT SINGLE QUOTATION MARK
                .Replace('’', '\'') // ' RIGHT SINGLE QUOTATION MARK
                .Replace('«', '"')  // « LEFT-POINTING DOUBLE ANGLE QUOTATION
                .Replace('»', '"'); // » RIGHT-POINTING DOUBLE ANGLE QUOTATION
        }

        private static int FlagDuplicates(
            SmartDataSource source,
            List<string> columns,
            List<string> log)
        {
            int total = 0;

            foreach (string col in columns)
            {
                // Group rows by value in this column (case-insensitive)
                var groups = source.Rows
                    .Where(r => r.Values.ContainsKey(col))
                    .GroupBy(r => r.Values[col], StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .ToList();

                foreach (var group in groups)
                {
                    var dupes = group.Skip(1).ToList(); // first occurrence is OK
                    foreach (var row in dupes)
                    {
                        string msg = $"قيمة مكررة في عمود '{col}': \"{group.Key}\"";
                        if (!row.Warnings.Contains(msg))
                        {
                            row.Warnings.Add(msg);
                            if (row.Status < RowStatus.Warning)
                                row.Status = RowStatus.Warning;
                        }
                        total++;
                    }

                    log.Add($"عمود '{col}': القيمة \"{group.Key}\" مكررة {group.Count()} مرات.");
                }
            }

            return total;
        }
    }
}
