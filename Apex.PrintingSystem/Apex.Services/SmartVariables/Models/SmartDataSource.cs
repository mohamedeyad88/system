using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Apex.Services.SmartVariables.Models
{
    public class SmartDataSource
    {
        // ── Raw input ─────────────────────────────────────────────────────────
        public string RawPastedText { get; set; } = "";

        // ── Parse metadata ────────────────────────────────────────────────────
        public char     DetectedSeparator     { get; set; } = '\t';
        public bool     HasHeader             { get; set; } = true;
        public bool     TrimWhitespace        { get; set; } = true;
        public bool     IgnoreEmptyRows       { get; set; } = true;
        public bool     AllValuesAsRawString  { get; set; } = true;
        public DateTime ParsedAt              { get; set; }

        // ── Parsed data ───────────────────────────────────────────────────────
        public List<string>       Columns { get; set; } = new();
        public List<SmartDataRow> Rows    { get; set; } = new();

        // ── Stats (computed) ─────────────────────────────────────────────────
        [JsonIgnore] public bool HasData     => Rows.Count > 0 && Columns.Count > 0;
        [JsonIgnore] public int  TotalRows   => Rows.Count;
        [JsonIgnore] public int  TotalColumns=> Columns.Count;
        [JsonIgnore] public int  ValidRows   => Rows.Count(r => r.Status == RowStatus.Valid);
        [JsonIgnore] public int  WarningRows => Rows.Count(r => r.Status == RowStatus.Warning);
        [JsonIgnore] public int  ErrorRows   => Rows.Count(r => r.Status == RowStatus.Error);

        [JsonIgnore]
        public string SummaryText =>
            HasData
                ? $"تم اكتشاف {TotalRows} سجل في {TotalColumns} عمود"
                : "لا توجد بيانات — انسخ الجدول من Excel والصقه هنا";

        [JsonIgnore]
        public string SeparatorName => DetectedSeparator switch
        {
            '\t' => "Tab (Excel/Sheets)",
            ','  => "فاصلة",
            ';'  => "فاصلة منقوطة",
            _    => "غير معروف"
        };
    }
}
