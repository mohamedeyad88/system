using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Apex.Services.SmartVariables.Models
{
    public class PreflightReport
    {
        public List<ValidationIssue> Issues { get; set; } = new();

        [JsonIgnore] public IEnumerable<ValidationIssue> Errors   => Issues.Where(i => i.Level == IssueLevel.Error);
        [JsonIgnore] public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Level == IssueLevel.Warning);
        [JsonIgnore] public IEnumerable<ValidationIssue> Infos    => Issues.Where(i => i.Level == IssueLevel.Info);

        [JsonIgnore] public bool HasErrors    => Errors.Any();
        [JsonIgnore] public bool HasWarnings  => Warnings.Any();
        [JsonIgnore] public bool CanExport    => !HasErrors;

        [JsonIgnore] public int ErrorCount    => Errors.Count();
        [JsonIgnore] public int WarningCount  => Warnings.Count();
        [JsonIgnore] public int InfoCount     => Infos.Count();

        [JsonIgnore]
        public string SummaryText => HasErrors
            ? $"⚠ يوجد {ErrorCount} خطأ يمنع التصدير"
            : HasWarnings
                ? $"يمكن التصدير — {WarningCount} تحذير"
                : "✓ جاهز للتصدير — لا توجد مشاكل";

        [JsonIgnore]
        public string SummaryColor => HasErrors ? "#EF4444" : HasWarnings ? "#F59E0B" : "#22C55E";
    }
}
