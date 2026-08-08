namespace Apex.Services.SmartVariables.Models
{
    public enum IssueLevel { Error, Warning, Info }
    public enum IssueCategory
    {
        MissingRequiredField,
        MissingImage,
        ImageQuality,
        DuplicateValue,
        UnmappedField,
        TextTooLong,
        InvalidFormat,
        UnusedImage,
        EmptyOptionalField,
        NoData,
        General
    }

    public class ValidationIssue
    {
        public IssueLevel Level { get; set; }
        public IssueCategory Category { get; set; }
        public int RowIndex { get; set; } = -1;   // -1 = global
        public string FieldLabel { get; set; } = "";
        public string Value { get; set; } = "";
        public string Message { get; set; } = "";
        public string Suggestion { get; set; } = "";

        public string LevelLabel => Level switch
        {
            IssueLevel.Error => "خطأ",
            IssueLevel.Warning => "تحذير",
            IssueLevel.Info => "ملاحظة",
            _ => ""
        };

        public string LevelIcon => Level switch
        {
            IssueLevel.Error => "✗",
            IssueLevel.Warning => "⚠",
            IssueLevel.Info => "ℹ",
            _ => ""
        };

        public string LevelColor => Level switch
        {
            IssueLevel.Error => "#EF4444",
            IssueLevel.Warning => "#F59E0B",
            IssueLevel.Info => "#3B82F6",
            _ => "#6B7280"
        };

        public string RowLabel => RowIndex >= 0 ? $"السجل {RowIndex}" : "عام";
    }
}
