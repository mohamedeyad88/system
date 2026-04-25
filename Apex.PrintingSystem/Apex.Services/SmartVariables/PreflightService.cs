using System.Collections.Generic;
using System.Linq;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.SmartVariables
{
    public interface IPreflightService
    {
        PreflightReport Run(
            SmartDataSource       source,
            List<VariableMapping> mappings,
            List<ImageAsset>      imageLibrary,
            ExportSettings        exportSettings);
    }

    public class PreflightService : IPreflightService
    {
        public PreflightReport Run(
            SmartDataSource       source,
            List<VariableMapping> mappings,
            List<ImageAsset>      imageLibrary,
            ExportSettings        exportSettings)
        {
            var report = new PreflightReport();

            // ── 1. No data at all ──────────────────────────────────────────────
            if (!source.HasData)
            {
                report.Issues.Add(new ValidationIssue
                {
                    Level    = IssueLevel.Error,
                    Category = IssueCategory.NoData,
                    Message  = "لا توجد بيانات. الصق جدول البيانات من Excel أو Google Sheets أولاً.",
                });
                return report; // nothing else to check
            }

            // ── 2. Unmapped required fields ────────────────────────────────────
            foreach (var mapping in mappings.Where(m => m.HasError))
            {
                report.Issues.Add(new ValidationIssue
                {
                    Level      = IssueLevel.Error,
                    Category   = IssueCategory.MissingRequiredField,
                    FieldLabel = mapping.FieldLabel,
                    Message    = $"الحقل المطلوب '{mapping.FieldLabel}' غير مربوط بأي عمود.",
                    Suggestion = "اذهب إلى تبويب ربط المتغيرات واربط هذا الحقل بعمود البيانات.",
                });
            }

            // ── 3. Unmapped optional fields ────────────────────────────────────
            foreach (var mapping in mappings.Where(m => !m.IsMapped && !m.IsRequired))
            {
                report.Issues.Add(new ValidationIssue
                {
                    Level      = IssueLevel.Info,
                    Category   = IssueCategory.UnmappedField,
                    FieldLabel = mapping.FieldLabel,
                    Message    = $"الحقل الاختياري '{mapping.FieldLabel}' غير مربوط — سيظهر فارغاً أو بالقيمة الافتراضية.",
                });
            }

            // ── 4. Per-row issues ──────────────────────────────────────────────
            foreach (var row in source.Rows)
            {
                foreach (string err in row.Errors)
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Level    = IssueLevel.Error,
                        Category = IssueCategory.MissingRequiredField,
                        RowIndex = row.RowIndex,
                        Message  = err,
                    });
                }

                foreach (string warn in row.Warnings)
                {
                    var category = ClassifyWarning(warn);
                    report.Issues.Add(new ValidationIssue
                    {
                        Level    = IssueLevel.Warning,
                        Category = category,
                        RowIndex = row.RowIndex,
                        Message  = warn,
                    });
                }

                // Missing image → error
                if (row.ImageStatus == ImageStatus.Missing)
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Level    = IssueLevel.Error,
                        Category = IssueCategory.MissingImage,
                        RowIndex = row.RowIndex,
                        Message  = $"السجل {row.RowIndex + 1}: الصورة مطلوبة ولم يتم العثور عليها.",
                        Suggestion = "تحقق من اسم الملف أو اضبط مجلد الصور.",
                    });
                }

                if (row.ImageStatus == ImageStatus.MultipleMatches)
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Level    = IssueLevel.Warning,
                        Category = IssueCategory.MissingImage,
                        RowIndex = row.RowIndex,
                        Message  = $"السجل {row.RowIndex + 1}: تم العثور على عدة صور بنفس الاسم، تم استخدام أول تطابق.",
                    });
                }
            }

            // ── 5. Export settings checks ──────────────────────────────────────

            // Output folder required for file-based exports
            if (exportSettings.Format != ExportFormat.DirectPrint
                && string.IsNullOrWhiteSpace(exportSettings.OutputFolder))
            {
                report.Issues.Add(new ValidationIssue
                {
                    Level    = IssueLevel.Error,
                    Category = IssueCategory.General,
                    Message  = "لم يتم تحديد مجلد الحفظ.",
                    Suggestion = "اختر مجلداً لحفظ الملفات في تبويب التصدير.",
                });
            }

            // Printer name required for DirectPrint
            if (exportSettings.Format == ExportFormat.DirectPrint
                && string.IsNullOrWhiteSpace(exportSettings.PrinterName))
            {
                report.Issues.Add(new ValidationIssue
                {
                    Level    = IssueLevel.Error,
                    Category = IssueCategory.General,
                    Message  = "لم يتم اختيار طابعة.",
                    Suggestion = "اختر طابعة من قائمة الطابعات المتاحة.",
                });
            }

            // DPI sanity
            if (exportSettings.DpiResolution < 72)
            {
                report.Issues.Add(new ValidationIssue
                {
                    Level    = IssueLevel.Warning,
                    Category = IssueCategory.ImageQuality,
                    Message  = $"دقة التصدير منخفضة جداً ({exportSettings.DpiResolution} DPI). يُنصح بـ 150 DPI كحد أدنى.",
                });
            }

            // ── 6. Unused images ───────────────────────────────────────────────
            var unusedImages = imageLibrary.Where(a => !a.IsUsed && a.Status == AssetStatus.Available).ToList();
            if (unusedImages.Count > 0 && unusedImages.Count <= 10)
            {
                foreach (var img in unusedImages)
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Level    = IssueLevel.Info,
                        Category = IssueCategory.UnusedImage,
                        Message  = $"الصورة '{img.FileName}' موجودة في المكتبة لكنها غير مستخدمة.",
                    });
                }
            }
            else if (unusedImages.Count > 10)
            {
                report.Issues.Add(new ValidationIssue
                {
                    Level    = IssueLevel.Info,
                    Category = IssueCategory.UnusedImage,
                    Message  = $"يوجد {unusedImages.Count} صورة في المكتبة غير مستخدمة.",
                });
            }

            return report;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static IssueCategory ClassifyWarning(string message)
        {
            if (message.Contains("مكرر") || message.Contains("تطابق متعدد"))
                return IssueCategory.DuplicateValue;
            if (message.Contains("صورة") || message.Contains("image"))
                return IssueCategory.MissingImage;
            if (message.Contains("طويل") || message.Contains("حرف"))
                return IssueCategory.TextTooLong;
            if (message.Contains("تاريخ") || message.Contains("رقم") || message.Contains("تنسيق"))
                return IssueCategory.InvalidFormat;
            if (message.Contains("DPI") || message.Contains("دقة"))
                return IssueCategory.ImageQuality;
            if (message.Contains("فارغ"))
                return IssueCategory.EmptyOptionalField;
            return IssueCategory.General;
        }
    }
}
