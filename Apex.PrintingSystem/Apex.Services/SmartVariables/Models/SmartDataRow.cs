using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Apex.Services.SmartVariables.Models
{
    public enum RowStatus   { Valid, Warning, Error }
    public enum ImageStatus { NotRequired, Found, Missing, MultipleMatches, Corrupted, UnsupportedFormat }

    public class SmartDataRow
    {
        public int        RowIndex           { get; set; }
        public RowStatus  Status             { get; set; } = RowStatus.Valid;

        /// <summary>Raw string values keyed by column name (case-insensitive).</summary>
        public Dictionary<string, string> Values { get; set; } =
            new(System.StringComparer.OrdinalIgnoreCase);

        public List<string> Errors   { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        // Image matching
        public string?      ResolvedImagePath { get; set; }
        public ImageStatus  ImageStatus       { get; set; } = ImageStatus.NotRequired;

        // Manual image override (by user)
        public string? ManualImagePath { get; set; }

        /// <summary>Gets a value by column name, returns fallback if missing/empty.</summary>
        public string Get(string key, string fallback = "") =>
            Values.TryGetValue(key, out var v) ? v : fallback;

        [JsonIgnore]
        public string StatusIcon => Status switch
        {
            RowStatus.Valid   => "✓",
            RowStatus.Warning => "⚠",
            RowStatus.Error   => "✗",
            _                 => ""
        };

        [JsonIgnore]
        public string StatusColor => Status switch
        {
            RowStatus.Valid   => "#22C55E",
            RowStatus.Warning => "#F59E0B",
            RowStatus.Error   => "#EF4444",
            _                 => "#6B7280"
        };

        [JsonIgnore]
        public string ImageStatusLabel => ImageStatus switch
        {
            ImageStatus.NotRequired       => "",
            ImageStatus.Found             => "✓",
            ImageStatus.Missing           => "ناقصة",
            ImageStatus.MultipleMatches   => "أكثر من تطابق",
            ImageStatus.Corrupted         => "تالفة",
            ImageStatus.UnsupportedFormat => "غير مدعوم",
            _                             => ""
        };
    }
}
