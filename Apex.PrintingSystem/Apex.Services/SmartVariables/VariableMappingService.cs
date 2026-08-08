using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.SmartVariables
{
    public interface IVariableMappingService
    {
        List<VariableMapping> BuildMappings(
            IEnumerable<SmartTemplateField> fields,
            IEnumerable<string> columns);

        MappingConfidence ScoreMatch(string fieldLabel, string fieldId, string columnName);
    }

    public class VariableMappingService : IVariableMappingService
    {
        // ── Arabic ↔ English synonym dictionary ───────────────────────────────
        // Each entry: canonical key → list of synonyms (Arabic and English)
        // Matching is case-insensitive and ignores spaces/underscores.

        private static readonly List<(string[] Keys, string[] Synonyms)> SynonymTable = new()
        {
            // Identity
            (new[]{"id","رقم","كود"},
             new[]{"id","رقم","رقم السجل","كود","code","serial","تسلسل","رقم تسلسلي","no","رقم الطالب","student id"}),

            // Name
            (new[]{"name","اسم","الاسم"},
             new[]{"name","اسم","الاسم","الاسم الكامل","full name","fullname","اسم الطالب","student name","اسم العميل","customer name","اسم الموظف","employee name"}),

            // First / Last name
            (new[]{"firstname","الاسم الأول","الاسم الاول"},
             new[]{"firstname","first name","الاسم الأول","الاسم الاول","first","اسم"}),

            (new[]{"lastname","اسم العائلة","اللقب"},
             new[]{"lastname","last name","family name","اسم العائلة","اسم الأسرة","اللقب","surname"}),

            // Email
            (new[]{"email","بريد","ايميل","إيميل"},
             new[]{"email","e-mail","بريد","بريد إلكتروني","بريد الكتروني","ايميل","إيميل","mail"}),

            // Phone
            (new[]{"phone","هاتف","جوال","موبايل"},
             new[]{"phone","mobile","cell","هاتف","جوال","موبايل","رقم الجوال","رقم الهاتف","telephone","tel"}),

            // Date of birth
            (new[]{"birthdate","تاريخ الميلاد","dob"},
             new[]{"birthdate","birth date","date of birth","dob","تاريخ الميلاد","تاريخ الولادة","born"}),

            // Nationality
            (new[]{"nationality","الجنسية","جنسية"},
             new[]{"nationality","الجنسية","جنسية","nation","country"}),

            // Gender
            (new[]{"gender","الجنس","نوع"},
             new[]{"gender","sex","الجنس","جنس","ذكر/أنثى"}),

            // Job / Title
            (new[]{"job","وظيفة","المسمى الوظيفي"},
             new[]{"job","title","position","role","وظيفة","المسمى الوظيفي","مسمى وظيفي","المنصب"}),

            // Department
            (new[]{"department","قسم","إدارة"},
             new[]{"department","dept","قسم","إدارة","الإدارة","الوحدة","section"}),

            // Company / Organization
            (new[]{"company","شركة","جهة"},
             new[]{"company","organization","org","employer","شركة","جهة العمل","المؤسسة","الجهة"}),

            // Image / Photo
            (new[]{"image","photo","صورة"},
             new[]{"image","photo","picture","img","صورة","الصورة","صورة شخصية","avatar"}),

            // QR / Barcode data
            (new[]{"qr","barcode","باركود"},
             new[]{"qr","qrcode","barcode","باركود","كود qr","رمز"}),

            // Amount / Price
            (new[]{"amount","price","مبلغ","سعر"},
             new[]{"amount","price","total","مبلغ","سعر","القيمة","التكلفة","value"}),

            // Date (generic)
            (new[]{"date","تاريخ"},
             new[]{"date","تاريخ","تاريخ الإصدار","تاريخ الإنشاء","issue date","created date"}),

            // Expiry date
            (new[]{"expiry","تاريخ الانتهاء","انتهاء"},
             new[]{"expiry","expiration","expire","expiry date","تاريخ الانتهاء","صلاحية","تاريخ الصلاحية"}),

            // Address
            (new[]{"address","عنوان"},
             new[]{"address","عنوان","العنوان","city","مدينة","location","الموقع"}),

            // Notes / Description
            (new[]{"notes","ملاحظات","وصف"},
             new[]{"notes","note","description","remarks","ملاحظات","ملاحظة","وصف","تفاصيل"}),

            // Certificate / Award
            (new[]{"certificate","شهادة"},
             new[]{"certificate","cert","award","شهادة","الشهادة","الجائزة"}),

            // Serial
            (new[]{"serial","تسلسل","رقم"},
             new[]{"serial","serial number","no","number","تسلسل","رقم تسلسلي","التسلسل","#"}),
        };

        // ── Public API ─────────────────────────────────────────────────────────

        public List<VariableMapping> BuildMappings(
            IEnumerable<SmartTemplateField> fields,
            IEnumerable<string> columns)
        {
            var columnList = columns.ToList();
            var mappings = new List<VariableMapping>();

            // Track columns already "taken" (High-confidence match wins)
            var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // First pass: find High-confidence matches
            var candidates = new List<(VariableMapping mapping, MappingConfidence conf, string col)>();

            foreach (var field in fields)
            {
                if (!field.IsVariable) continue;

                var mapping = new VariableMapping
                {
                    FieldId = field.Id,
                    FieldLabel = field.Label,
                    FieldType = field.FieldType,
                    IsRequired = field.IsRequired,
                };

                // Try every column and pick the best score.
                //
                // MappingConfidence is declared BEST-FIRST (High = 0 … None = 3), so a
                // better match is a SMALLER value. The comparisons here used to read
                // the other way round — starting from None (3) and skipping anything
                // "less than" it, which is every real match. Auto-map therefore never
                // bound a single field, not even on an exactly identical column name.
                string? bestCol = null;
                var bestConf = MappingConfidence.None;

                foreach (string col in columnList)
                {
                    var conf = ScoreMatch(field.Label, field.VariableKey, col);
                    if (conf == MappingConfidence.None) continue;

                    if (IsBetter(conf, bestConf) || bestCol == null)
                    {
                        bestCol = col;
                        bestConf = conf;
                    }
                    else if (conf == bestConf &&
                             NormalizeKey(col) == NormalizeKey(field.Label))
                    {
                        // Equal score: prefer the column whose name IS the field's name.
                        bestCol = col;
                        bestConf = conf;
                    }
                }

                mapping.ColumnName = bestCol;
                mapping.Confidence = bestConf;

                mappings.Add(mapping);
                candidates.Add((mapping, bestConf, bestCol ?? ""));
            }

            // Second pass: resolve conflicts (two fields → same column)
            // Higher-confidence mapping wins; lower gets reset to None
            var byColumn = candidates
                .Where(c => !string.IsNullOrEmpty(c.col))
                .GroupBy(c => c.col, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in byColumn)
            {
                // Best first — and "best" is the SMALLEST enum value.
                var sorted = group.OrderBy(c => c.conf).ToList();
                // Keep the best, clear the rest
                for (int i = 1; i < sorted.Count; i++)
                {
                    sorted[i].mapping.ColumnName = null;
                    sorted[i].mapping.Confidence = MappingConfidence.None;
                }
            }

            return mappings;
        }

        /// <summary>
        /// Returns a MappingConfidence score for one (field, column) pair.
        /// High   = exact match after normalization
        /// Medium = synonym match
        /// Low    = partial / substring match
        /// None   = no match
        /// </summary>
        public MappingConfidence ScoreMatch(string fieldLabel, string fieldId, string columnName)
        {
            string normField = NormalizeKey(fieldLabel);
            string normId = NormalizeKey(fieldId);
            string normColumn = NormalizeKey(columnName);

            if (string.IsNullOrEmpty(normColumn))
                return MappingConfidence.None;

            // Exact match on label or variableKey
            if (normField == normColumn || normId == normColumn)
                return MappingConfidence.High;

            // Synonym table lookup
            foreach (var (keys, synonyms) in SynonymTable)
            {
                bool fieldInGroup = keys.Any(k => NormalizeKey(k) == normField || NormalizeKey(k) == normId)
                                  || synonyms.Any(s => NormalizeKey(s) == normField || NormalizeKey(s) == normId);
                bool columnInGroup = keys.Any(k => NormalizeKey(k) == normColumn)
                                  || synonyms.Any(s => NormalizeKey(s) == normColumn);

                if (fieldInGroup && columnInGroup)
                    return MappingConfidence.Medium;
            }

            // Partial match: column contains field label or vice versa
            if (normColumn.Contains(normField) || normField.Contains(normColumn))
                return MappingConfidence.Low;

            if (normColumn.Contains(normId) || normId.Contains(normColumn))
                return MappingConfidence.Low;

            return MappingConfidence.None;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// True when <paramref name="candidate"/> is a better match than
        /// <paramref name="current"/>.
        ///
        /// A named helper rather than a bare comparison, because the enum reads
        /// best-to-worst while <c>&lt;</c> reads smallest-to-largest — the exact
        /// confusion that stopped auto-map from ever binding anything.
        /// </summary>
        public static bool IsBetter(MappingConfidence candidate, MappingConfidence current) =>
            candidate < current;

        /// <summary>
        /// Normalizes a string for fuzzy comparison:
        /// lowercase, remove spaces/underscores/hyphens, normalize alef forms.
        /// </summary>
        private static string NormalizeKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value
                .ToLowerInvariant()
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "")
                .Replace("أ", "ا")
                .Replace("إ", "ا")
                .Replace("آ", "ا")
                .Replace("ة", "ه")
                .Replace("ى", "ي");
        }
    }
}
