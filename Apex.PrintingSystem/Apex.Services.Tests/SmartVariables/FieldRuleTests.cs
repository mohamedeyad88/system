using System.Collections.Generic;
using Apex.Services.SmartVariables;

namespace Apex.Services.Tests.SmartVariables;

/// <summary>
/// Printing a field only when the record calls for it.
///
/// Without rules, a discount line prints an empty labelled box on every piece that has
/// no discount, or the job gets split into separate runs and merged by hand. Both are
/// how variable-data work goes wrong at scale.
/// </summary>
public class FieldRuleTests
{
    private static Dictionary<string, string?> Row(params (string k, string? v)[] pairs)
    {
        var d = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    // ── No rules ─────────────────────────────────────────────────────────────

    [Fact]
    public void AFieldWithNoRulesAlwaysPrints()
    {
        // Adding the feature must not change a single existing template.
        Assert.True(FieldRuleEvaluator.ShouldPrint(null, Row(("name", "Ali"))));
        Assert.True(FieldRuleEvaluator.ShouldPrint(new List<FieldRule>(), Row(("name", "Ali"))));
    }

    // ── Presence ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("50", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void ShowOnlyWhenTheValueIsPresent(string? discount, bool expected)
    {
        var rules = new List<FieldRule> { new("discount", RuleOperator.IsNotEmpty) };

        Assert.Equal(expected, FieldRuleEvaluator.ShouldPrint(rules, Row(("discount", discount))));
    }

    [Fact]
    public void AMissingColumnCountsAsEmpty()
    {
        var rules = new List<FieldRule> { new("discount", RuleOperator.IsNotEmpty) };

        Assert.False(FieldRuleEvaluator.ShouldPrint(rules, Row(("name", "Ali"))));
    }

    // ── Comparison ───────────────────────────────────────────────────────────

    [Fact]
    public void NumbersAreComparedAsNumbers_NotAsText()
    {
        // "10" > "9" is false as text. On a discount rule that prints the line on
        // exactly the wrong records.
        var rules = new List<FieldRule> { new("total", RuleOperator.GreaterThan, "9") };

        Assert.True(FieldRuleEvaluator.ShouldPrint(rules, Row(("total", "10"))));
        Assert.False(FieldRuleEvaluator.ShouldPrint(rules, Row(("total", "8"))));
    }

    [Fact]
    public void NonNumericValuesFallBackToTextComparison()
    {
        var rules = new List<FieldRule> { new("grade", RuleOperator.GreaterThan, "B") };

        Assert.True(FieldRuleEvaluator.ShouldPrint(rules, Row(("grade", "C"))));
        Assert.False(FieldRuleEvaluator.ShouldPrint(rules, Row(("grade", "A"))));
    }

    [Fact]
    public void EqualityIgnoresCaseAndSurroundingSpace()
    {
        // Spreadsheet data is never clean; a trailing space must not silently change
        // what prints.
        var rules = new List<FieldRule> { new("type", RuleOperator.Equals, "vip") };

        Assert.True(FieldRuleEvaluator.ShouldPrint(rules, Row(("type", " VIP "))));
    }

    [Fact]
    public void ContainsMatchesPartOfTheValue()
    {
        var rules = new List<FieldRule> { new("notes", RuleOperator.Contains, "urgent") };

        Assert.True(FieldRuleEvaluator.ShouldPrint(rules, Row(("notes", "Please treat as URGENT"))));
        Assert.False(FieldRuleEvaluator.ShouldPrint(rules, Row(("notes", "routine"))));
    }

    // ── Hide ─────────────────────────────────────────────────────────────────

    [Fact]
    public void HideRemovesTheFieldWhenTheConditionHolds()
    {
        var rules = new List<FieldRule> { new("status", RuleOperator.Equals, "cancelled", RuleAction.Hide) };

        Assert.False(FieldRuleEvaluator.ShouldPrint(rules, Row(("status", "cancelled"))));
        Assert.True(FieldRuleEvaluator.ShouldPrint(rules, Row(("status", "active"))));
    }

    // ── Several rules ────────────────────────────────────────────────────────

    [Fact]
    public void SeveralRulesMustAllBeSatisfied()
    {
        // A field gated on two conditions has to mean BOTH. "Either" would print it on
        // records it was explicitly meant to skip.
        var rules = new List<FieldRule>
        {
            new("discount", RuleOperator.IsNotEmpty),
            new("total", RuleOperator.GreaterThan, "100"),
        };

        Assert.True(FieldRuleEvaluator.ShouldPrint(rules, Row(("discount", "10"), ("total", "500"))));
        Assert.False(FieldRuleEvaluator.ShouldPrint(rules, Row(("discount", "10"), ("total", "50"))));
        Assert.False(FieldRuleEvaluator.ShouldPrint(rules, Row(("discount", ""), ("total", "500"))));
    }

    // ── Safety ───────────────────────────────────────────────────────────────

    [Fact]
    public void ARuleOnAVariableThatDoesNotExistIsReported()
    {
        // A typo in a key evaluates against an empty value, quietly turning into
        // "hide everything" or "show everything" across the whole run.
        var rules = new List<FieldRule>
        {
            new("discount", RuleOperator.IsNotEmpty),
            new("discunt", RuleOperator.IsNotEmpty),   // typo
        };

        var bad = FieldRuleEvaluator.FindRulesWithUnknownVariable(
            rules, new[] { "name", "discount", "total" });

        Assert.Single(bad);
        Assert.Equal("discunt", bad[0].VariableKey);
    }
}
