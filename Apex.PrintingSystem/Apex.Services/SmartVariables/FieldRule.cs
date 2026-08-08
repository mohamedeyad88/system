using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Apex.Services.SmartVariables
{
    /// <summary>How a field's value is compared against the rule's operand.</summary>
    public enum RuleOperator
    {
        IsEmpty,
        IsNotEmpty,
        Equals,
        NotEquals,
        Contains,
        GreaterThan,
        LessThan,
    }

    /// <summary>What happens to the field when the condition holds.</summary>
    public enum RuleAction
    {
        /// <summary>Field prints only when the condition holds.</summary>
        Show,

        /// <summary>Field is omitted when the condition holds.</summary>
        Hide,
    }

    /// <summary>
    /// "Print this field only when the data says so."
    ///
    /// Variable-data work is full of fields that must not appear on every record: a
    /// discount line when there is no discount, a "second guest" line on a single
    /// booking, an expiry date on a document that does not expire. Without rules the
    /// operator either prints an empty labelled box on thousands of pieces, or splits
    /// the job into separate runs and merges them by hand.
    /// </summary>
    public sealed record FieldRule(
        string VariableKey,
        RuleOperator Operator,
        string? Operand = null,
        RuleAction Action = RuleAction.Show)
    {
        /// <summary>Does the condition hold for this record?</summary>
        public bool Matches(IReadOnlyDictionary<string, string?> row)
        {
            row.TryGetValue(VariableKey, out var raw);
            string value = raw ?? "";

            return Operator switch
            {
                RuleOperator.IsEmpty => string.IsNullOrWhiteSpace(value),
                RuleOperator.IsNotEmpty => !string.IsNullOrWhiteSpace(value),
                RuleOperator.Equals => Same(value, Operand),
                RuleOperator.NotEquals => !Same(value, Operand),
                RuleOperator.Contains => (Operand ?? "").Length > 0
                    && value.Contains(Operand!, StringComparison.CurrentCultureIgnoreCase),
                RuleOperator.GreaterThan => Compare(value, Operand) > 0,
                RuleOperator.LessThan => Compare(value, Operand) < 0,
                _ => false,
            };
        }

        private static bool Same(string a, string? b) =>
            string.Equals(a.Trim(), (b ?? "").Trim(), StringComparison.CurrentCultureIgnoreCase);

        /// <summary>
        /// Numeric comparison when both sides are numbers, text otherwise. Comparing
        /// "10" against "9" as text would say 10 is smaller — which on a discount rule
        /// prints the line on exactly the wrong records.
        /// </summary>
        private static int Compare(string a, string? b)
        {
            string other = b ?? "";

            if (double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(other, NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
                return x.CompareTo(y);

            return string.Compare(a.Trim(), other.Trim(), StringComparison.CurrentCultureIgnoreCase);
        }
    }

    /// <summary>Applies a field's rules to one record.</summary>
    public static class FieldRuleEvaluator
    {
        /// <summary>
        /// Should the field print for this record?
        ///
        /// With no rules the field always prints, so adding the feature changes nothing
        /// for existing templates. With several rules ALL must be satisfied — a field
        /// gated on two conditions has to mean both, not either, or it appears on
        /// records it was explicitly meant to skip.
        /// </summary>
        public static bool ShouldPrint(
            IReadOnlyList<FieldRule>? rules, IReadOnlyDictionary<string, string?> row)
        {
            if (rules == null || rules.Count == 0) return true;
            if (row == null) return true;

            foreach (var rule in rules)
            {
                bool matched = rule.Matches(row);

                // Show: must match to print. Hide: must NOT match to print.
                bool allows = rule.Action == RuleAction.Show ? matched : !matched;
                if (!allows) return false;
            }
            return true;
        }

        /// <summary>
        /// Rules whose variable does not exist in the data. They silently evaluate
        /// against an empty value, so a typo in a key turns into "hide everything" or
        /// "show everything" across the whole run — worth refusing before printing.
        /// </summary>
        public static IReadOnlyList<FieldRule> FindRulesWithUnknownVariable(
            IEnumerable<FieldRule> rules, IEnumerable<string> availableKeys)
        {
            var known = new HashSet<string>(availableKeys, StringComparer.OrdinalIgnoreCase);
            return rules.Where(r => !known.Contains(r.VariableKey)).ToList();
        }
    }
}
