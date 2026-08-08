using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.SmartVariables
{
    public class ParseOptions
    {
        public bool HasHeader { get; set; } = true;
        public bool TrimWhitespace { get; set; } = true;
        public bool IgnoreEmptyRows { get; set; } = true;
        public bool AllValuesAsRawString { get; set; } = true;
        /// <summary>Force a specific separator; null = auto-detect.</summary>
        public char? ForceSeparator { get; set; } = null;
    }

    public interface ISmartPasteParser
    {
        SmartDataSource Parse(string rawText, ParseOptions? options = null);
        char DetectSeparator(string text);
    }

    public class SmartPasteParser : ISmartPasteParser
    {
        // ── Invisible / problematic characters ────────────────────────────────
        // Built at runtime using \uXXXX string escapes to avoid compiler
        // treating U+2028 / U+2029 as newlines inside char literals.
        private static readonly HashSet<char> DropChars = BuildDropChars();

        private static HashSet<char> BuildDropChars()
        {
            // The characters we want to drop are encoded as \uXXXX inside a string.
            const string chars =
                "﻿" + // BOM / Zero-Width No-Break Space
                "​" + // Zero-Width Space
                "‌" + // Zero-Width Non-Joiner
                "‍" + // Zero-Width Joiner
                "‎" + // Left-to-Right Mark
                "‏" + // Right-to-Left Mark
                "‪" + // Left-to-Right Embedding
                "‫" + // Right-to-Left Embedding
                "‬" + // Pop Directional Formatting
                "‭" + // Left-to-Right Override
                "‮" + // Right-to-Left Override
                      // U+2028 Line Separator added below at runtime
                      // U+2029 Paragraph Separator added below at runtime
                "�";  // Replacement Character

            var set = new HashSet<char>();

            // U+2028 Line Separator and U+2029 Paragraph Separator cannot appear
            // as source-code literals (C# treats them as line terminators).
            set.Add((char)0x2028); // Line Separator
            set.Add((char)0x2029); // Paragraph Separator
            foreach (char c in chars)
                set.Add(c);
            return set;
        }

        // NBSP (U+00A0) is replaced with a regular space, not dropped
        private const char Nbsp = ' ';

        // ── Public API ─────────────────────────────────────────────────────────

        public SmartDataSource Parse(string rawText, ParseOptions? options = null)
        {
            options ??= new ParseOptions();

            var source = new SmartDataSource
            {
                RawPastedText = rawText,
                HasHeader = options.HasHeader,
                TrimWhitespace = options.TrimWhitespace,
                IgnoreEmptyRows = options.IgnoreEmptyRows,
                AllValuesAsRawString = options.AllValuesAsRawString,
                ParsedAt = DateTime.Now,
            };

            if (string.IsNullOrWhiteSpace(rawText))
                return source;

            // 1. Remove invisible chars + normalize NBSP
            string cleaned = CleanInvisibleChars(rawText);

            // 2. Normalize line endings
            cleaned = cleaned.Replace("\r\n", "\n").Replace('\r', '\n');

            // 3. Detect separator
            char sep = options.ForceSeparator ?? DetectSeparator(cleaned);
            source.DetectedSeparator = sep;

            // 4. Split into lines
            string[] lines = cleaned.Split('\n');

            // 5. Filter fully-blank lines
            var nonEmptyLines = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (nonEmptyLines.Count == 0)
                return source;

            // 6. Parse header
            List<string> columns;
            int dataStartLine;

            if (options.HasHeader)
            {
                columns = ParseLine(nonEmptyLines[0], sep, options.TrimWhitespace);
                columns = NormalizeHeaders(columns);
                dataStartLine = 1;
            }
            else
            {
                var firstRow = ParseLine(nonEmptyLines[0], sep, options.TrimWhitespace);
                columns = firstRow.Select((_, i) => "عمود" + (i + 1)).ToList();
                // "عمودN" — Arabic for "ColumnN"
                dataStartLine = 0;
            }

            source.Columns = columns;
            int colCount = columns.Count;

            // 7. Parse data rows
            int rowIndex = 0;
            for (int i = dataStartLine; i < nonEmptyLines.Count; i++)
            {
                string rawLine = nonEmptyLines[i];

                if (options.IgnoreEmptyRows && string.IsNullOrWhiteSpace(rawLine))
                    continue;

                var cells = ParseLine(rawLine, sep, options.TrimWhitespace);

                // Skip entirely-empty rows
                if (options.IgnoreEmptyRows && cells.All(string.IsNullOrEmpty))
                    continue;

                var dataRow = new SmartDataRow { RowIndex = rowIndex };

                for (int c = 0; c < colCount; c++)
                {
                    string value = c < cells.Count ? cells[c] : "";
                    // Raw string storage: never cast to number
                    dataRow.Values[columns[c]] = value;
                }

                // Extra cells beyond header count
                for (int c = colCount; c < cells.Count; c++)
                {
                    string extraKey = "_extra" + (c - colCount + 1);
                    dataRow.Values[extraKey] = cells[c];
                }

                source.Rows.Add(dataRow);
                rowIndex++;
            }

            return source;
        }

        /// <summary>
        /// Detects column separator. Strongly prefers Tab (Excel/Sheets default).
        /// Falls back to comma, semicolon, or pipe.
        /// </summary>
        public char DetectSeparator(string text)
        {
            if (string.IsNullOrEmpty(text))
                return '\t';

            string sample = text.Length > 2000 ? text[..2000] : text;

            int tabs = CountChar(sample, '\t');
            int commas = CountChar(sample, ',');
            int semicolons = CountChar(sample, ';');
            int pipes = CountChar(sample, '|');

            // Tab wins decisively — Excel and Google Sheets always produce tabs
            if (tabs > 0)
                return '\t';

            var candidates = new[] { (',', commas), (';', semicolons), ('|', pipes) };
            var best = candidates.OrderByDescending(c => c.Item2).First();
            return best.Item2 > 0 ? best.Item1 : '\t';
        }

        // ── Internal helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Parses a single line with RFC4180-style quoting support.
        /// </summary>
        internal List<string> ParseLine(string line, char sep, bool trim)
        {
            var fields = new List<string>();
            if (string.IsNullOrEmpty(line))
            {
                fields.Add("");
                return fields;
            }

            var sb = new StringBuilder();
            bool inQuotes = false;
            int i = 0;

            while (i < line.Length)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // Doubled quote -> literal quote
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            inQuotes = false;
                            i++;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                        i++;
                    }
                }
                else
                {
                    if (c == '"' && sb.Length == 0)
                    {
                        inQuotes = true;
                        i++;
                    }
                    else if (c == sep)
                    {
                        fields.Add(FinalizeCell(sb.ToString(), trim));
                        sb.Clear();
                        i++;
                    }
                    else
                    {
                        sb.Append(c);
                        i++;
                    }
                }
            }

            fields.Add(FinalizeCell(sb.ToString(), trim));
            return fields;
        }

        /// <summary>
        /// Deduplicates column headers; replaces blanks with "عمودN".
        /// </summary>
        internal List<string> NormalizeHeaders(List<string> raw)
        {
            var result = new List<string>(raw.Count);
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < raw.Count; i++)
            {
                string h = raw[i].Trim();

                if (string.IsNullOrEmpty(h))
                    h = "عمود" + (i + 1); // "عمودN"

                if (seen.TryGetValue(h, out int count))
                {
                    seen[h] = count + 1;
                    h = h + "_" + (count + 1);
                }
                else
                {
                    seen[h] = 1;
                }

                result.Add(h);
            }

            return result;
        }

        /// <summary>
        /// Strips invisible Unicode characters; replaces NBSP with regular space.
        /// </summary>
        internal string CleanInvisibleChars(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == Nbsp)
                    sb.Append(' ');
                else if (!DropChars.Contains(c))
                    sb.Append(c);
                // else: silently drop
            }
            return sb.ToString();
        }

        // ── Utilities ──────────────────────────────────────────────────────────

        private static string FinalizeCell(string value, bool trim)
            => trim ? value.Trim() : value;

        private static int CountChar(string text, char ch)
        {
            int n = 0;
            foreach (char c in text)
                if (c == ch) n++;
            return n;
        }
    }
}
