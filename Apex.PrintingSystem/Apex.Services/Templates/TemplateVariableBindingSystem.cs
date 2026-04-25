using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Apex.Services.Templates
{
    public enum DataSourceType { Manual, Csv, IncrementalCounter, DateTimeNow }

    public class TemplateDataRow
    {
        public int RowIndex { get; set; }
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string Get(string variableName, string defaultValue = "") =>
            Values.TryGetValue(variableName, out string? v) ? v : defaultValue;
    }

    public class CsvDataSource
    {
        public string FilePath { get; set; } = "";
        public char Delimiter { get; set; } = ',';
        public bool HasHeader { get; set; } = true;
        public Encoding FileEncoding { get; set; } = Encoding.UTF8;
    }

    public static class TemplateVariableBindingSystem
    {
        // Matches "{{VariableName}}" or "{{VariableName:FormatString}}"
        private static readonly Regex VariablePattern =
            new(@"\{\{([^{}:]+)(?::([^{}]*))?\}\}", RegexOptions.Compiled);

        /// <summary>
        /// Extract all unique variable names referenced in a template's slot VariableName fields.
        /// </summary>
        public static List<string> ExtractVariables(ApextTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            HashSet<string> vars = new(StringComparer.OrdinalIgnoreCase);

            foreach (TemplatePageDefinition page in template.Pages)
            {
                foreach (TemplateSlotDefinition slot in page.Slots)
                {
                    if (!string.IsNullOrWhiteSpace(slot.VariableName))
                        vars.Add(slot.VariableName.Trim());
                }
            }

            return vars.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Parse a CSV file into TemplateDataRow instances.
        /// Handles quoted fields, UTF-8 with BOM, and Arabic content.
        /// </summary>
        public static List<TemplateDataRow> LoadCsvData(CsvDataSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!File.Exists(source.FilePath))
                throw new FileNotFoundException($"CSV file not found: {source.FilePath}", source.FilePath);

            List<TemplateDataRow> rows = new();
            List<string> headers = new();

            // Use UTF-8 with BOM detection enabled
            Encoding encoding = source.FileEncoding ?? Encoding.UTF8;
            using StreamReader reader = new(source.FilePath, encoding, detectEncodingFromByteOrderMarks: true);

            int lineNumber = 0;
            int dataRowIndex = 0;
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line)) continue;

                List<string> fields = ParseCsvLine(line, source.Delimiter);

                if (lineNumber == 1 && source.HasHeader)
                {
                    headers = fields.Select(h => h.Trim()).ToList();
                    continue;
                }

                TemplateDataRow row = new() { RowIndex = dataRowIndex++ };

                if (source.HasHeader)
                {
                    // Map by column header name
                    for (int i = 0; i < Math.Min(headers.Count, fields.Count); i++)
                    {
                        string key = headers[i];
                        if (!string.IsNullOrEmpty(key))
                            row.Values[key] = fields[i];
                    }
                    // Extra columns beyond headers get numeric keys
                    for (int i = headers.Count; i < fields.Count; i++)
                        row.Values[$"Column{i + 1}"] = fields[i];
                }
                else
                {
                    // No header: use Column1, Column2, ...
                    for (int i = 0; i < fields.Count; i++)
                        row.Values[$"Column{i + 1}"] = fields[i];
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// Create a single-row data set from a dictionary.
        /// </summary>
        public static TemplateDataRow FromDictionary(Dictionary<string, string> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));

            TemplateDataRow row = new() { RowIndex = 0 };
            foreach (KeyValuePair<string, string> kv in values)
                row.Values[kv.Key] = kv.Value;
            return row;
        }

        /// <summary>
        /// Generate counter rows. e.g. GenerateCounterData("SerialNumber", 1, 100, "000000")
        /// produces rows with SerialNumber = "000001".."000100"
        /// </summary>
        public static List<TemplateDataRow> GenerateCounterData(
            string variableName,
            int startValue,
            int count,
            string format = "000000")
        {
            if (string.IsNullOrWhiteSpace(variableName))
                throw new ArgumentException("Variable name cannot be empty.", nameof(variableName));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than 0.");

            List<TemplateDataRow> rows = new(count);

            for (int i = 0; i < count; i++)
            {
                int value = startValue + i;
                string formatted = string.IsNullOrEmpty(format)
                    ? value.ToString()
                    : value.ToString(format);

                TemplateDataRow row = new() { RowIndex = i };
                row.Values[variableName] = formatted;
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// Merge data row with template defaults.
        /// For slots that have a DefaultValue, if the variable is not present in data, add the default.
        /// </summary>
        public static TemplateDataRow MergeWithDefaults(TemplateDataRow data, ApextTemplate template)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (template == null) throw new ArgumentNullException(nameof(template));

            TemplateDataRow merged = new() { RowIndex = data.RowIndex };
            foreach (KeyValuePair<string, string> kv in data.Values)
                merged.Values[kv.Key] = kv.Value;

            foreach (TemplatePageDefinition page in template.Pages)
            {
                foreach (TemplateSlotDefinition slot in page.Slots)
                {
                    if (string.IsNullOrWhiteSpace(slot.VariableName)) continue;
                    if (slot.DefaultValue == null) continue;

                    string key = slot.VariableName.Trim();
                    if (!merged.Values.ContainsKey(key))
                        merged.Values[key] = slot.DefaultValue;
                }
            }

            return merged;
        }

        /// <summary>
        /// Render a template string by resolving all {{VariableName}} and {{VariableName:format}} tokens.
        /// Supports date formatting, numeric formatting, and plain text substitution.
        /// </summary>
        public static string RenderText(string templateText, TemplateDataRow data)
        {
            if (string.IsNullOrEmpty(templateText)) return templateText ?? "";
            if (data == null) return templateText;

            return VariablePattern.Replace(templateText, match =>
            {
                string varName = match.Groups[1].Value.Trim();
                string? formatString = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null;

                if (!data.Values.TryGetValue(varName, out string? rawValue))
                    return match.Value; // leave unresolved token as-is

                if (string.IsNullOrEmpty(formatString))
                    return rawValue;

                // Try to apply format string
                return ApplyFormat(rawValue, formatString);
            });
        }

        /// <summary>
        /// Get the union of all variable names present across a data set.
        /// </summary>
        public static List<string> GetDataSetVariables(List<TemplateDataRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            HashSet<string> vars = new(StringComparer.OrdinalIgnoreCase);
            foreach (TemplateDataRow row in rows)
            {
                foreach (string key in row.Values.Keys)
                    vars.Add(key);
            }

            return vars.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Parse a single CSV line, respecting RFC 4180 quoting rules.
        /// </summary>
        private static List<string> ParseCsvLine(string line, char delimiter)
        {
            List<string> fields = new();
            StringBuilder current = new();
            bool inQuotes = false;
            int i = 0;

            while (i < line.Length)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // Peek ahead for escaped quote
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
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
                        current.Append(c);
                        i++;
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                        i++;
                    }
                    else if (c == delimiter)
                    {
                        fields.Add(current.ToString());
                        current.Clear();
                        i++;
                    }
                    else
                    {
                        current.Append(c);
                        i++;
                    }
                }
            }

            fields.Add(current.ToString());
            return fields;
        }

        /// <summary>
        /// Apply a .NET format string to a raw string value.
        /// Tries numeric, then date/time, then falls back to raw value.
        /// </summary>
        private static string ApplyFormat(string rawValue, string format)
        {
            // Try as double (numeric)
            if (double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double numericValue))
            {
                try { return numericValue.ToString(format, CultureInfo.CurrentCulture); }
                catch { /* fall through */ }
            }

            // Try as DateTime
            if (DateTime.TryParse(rawValue, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime dateValue))
            {
                try { return dateValue.ToString(format, CultureInfo.CurrentCulture); }
                catch { /* fall through */ }
            }

            // Return raw value unchanged
            return rawValue;
        }
    }
}
