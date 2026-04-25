using System.Text.Json.Serialization;

namespace Apex.Services.SmartVariables.Models
{
    public enum MappingConfidence { High, Medium, Low, None }

    public class VariableMapping
    {
        // ── Field info (from template) ─────────────────────────────────────────
        public string        FieldId    { get; set; } = "";
        public string        FieldLabel { get; set; } = "";
        public SmartFieldType FieldType { get; set; }
        public bool          IsRequired { get; set; }

        // ── Binding ───────────────────────────────────────────────────────────
        /// <summary>The column name from the pasted data. Null = unmapped.</summary>
        public string? ColumnName    { get; set; }
        public string? DefaultValue  { get; set; }

        // ── Auto-mapping ──────────────────────────────────────────────────────
        public MappingConfidence Confidence { get; set; } = MappingConfidence.None;

        // ── Computed ─────────────────────────────────────────────────────────
        [JsonIgnore] public bool IsMapped   => !string.IsNullOrEmpty(ColumnName);
        [JsonIgnore] public bool HasError   => IsRequired && !IsMapped;

        [JsonIgnore]
        public string StatusLabel =>
            HasError     ? "⚠ مطلوب — غير مربوط" :
            IsMapped     ? "✓ مربوط"              :
            IsRequired   ? "⚠ مطلوب"              : "—";

        [JsonIgnore]
        public string StatusColor =>
            HasError     ? "#EF4444" :
            IsMapped     ? "#22C55E" :
            IsRequired   ? "#F59E0B" : "#6B7280";

        [JsonIgnore]
        public string ConfidenceLabel => Confidence switch
        {
            MappingConfidence.High   => "عالية",
            MappingConfidence.Medium => "متوسطة",
            MappingConfidence.Low    => "منخفضة",
            _                        => "—"
        };
    }
}
