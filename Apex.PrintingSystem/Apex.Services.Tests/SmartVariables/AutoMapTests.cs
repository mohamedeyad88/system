using System.Collections.Generic;
using System.Linq;
using Apex.Services.SmartVariables;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.Tests.SmartVariables;

/// <summary>
/// Automatic column matching.
///
/// MappingConfidence is declared best-first — High = 0, None = 3 — while the selection
/// loop compared as though larger meant better. It started from None (3) and skipped
/// anything "smaller", which is every real match. Auto-map therefore bound NOTHING,
/// ever, not even a column whose name was character-for-character the field's name.
/// Combined with manual bindings being overwritten on every navigation, that is the
/// whole of "the binding does not work" in the template designer.
/// </summary>
public class AutoMapTests
{
    private static readonly VariableMappingService Service = new();

    private static SmartTemplateField Field(string label, string? key = null) => new()
    {
        Id = label,
        Label = label,
        VariableKey = key ?? label,
        FieldType = SmartFieldType.TextVariable,
    };

    [Fact]
    public void AnIdenticalColumnNameIsMatched()
    {
        // The most basic case there is, and it used to return nothing.
        var result = Service.BuildMappings(
            new[] { Field("الاسم") },
            new[] { "الاسم", "الصف" });

        var m = result.Single();
        Assert.Equal("الاسم", m.ColumnName);
        Assert.Equal(MappingConfidence.High, m.Confidence);
    }

    [Fact]
    public void MatchingIgnoresSpacingAndArabicLetterVariants()
    {
        // Spreadsheet headers are never typed consistently.
        var result = Service.BuildMappings(
            new[] { Field("اسم الطالب") },
            new[] { "أسم  الطالبة" });

        Assert.Equal("أسم  الطالبة", result.Single().ColumnName);
    }

    [Fact]
    public void AnExactMatchWinsOverAPartialOne()
    {
        var result = Service.BuildMappings(
            new[] { Field("الاسم") },
            new[] { "اسم الأب", "الاسم" });

        Assert.Equal("الاسم", result.Single().ColumnName);
        Assert.Equal(MappingConfidence.High, result.Single().Confidence);
    }

    [Fact]
    public void AFieldWithNoResemblingColumnStaysUnmapped()
    {
        // Silence is correct here — inventing a binding would print the wrong column
        // on every record.
        var result = Service.BuildMappings(
            new[] { Field("رقم الهاتف المحمول") },
            new[] { "س", "ص" });

        Assert.Null(result.Single().ColumnName);
        Assert.Equal(MappingConfidence.None, result.Single().Confidence);
    }

    [Fact]
    public void TwoFieldsNeverShareOneColumn()
    {
        // Two fields bound to the same column means one of them is certainly wrong;
        // the weaker match must give way rather than both printing the same value.
        var result = Service.BuildMappings(
            new[] { Field("الاسم"), Field("اسم") },
            new[] { "الاسم" });

        Assert.Single(result.Where(m => m.ColumnName == "الاسم"));
    }

    [Fact]
    public void TheSameVariableInTwoSlotsSharesTheColumn()
    {
        // A repeated placeholder — the customer name in both the header and the footer —
        // is two SLOTS carrying the SAME variable key. Both must resolve to the column;
        // blanking one was the reported "the variable works in one place but not the
        // other" bug. This is distinct from TwoFieldsNeverShareOneColumn, where the two
        // fields are DIFFERENT variables.
        var slot1 = new SmartTemplateField { Id = "slot1", Label = "الاسم", VariableKey = "الاسم", FieldType = SmartFieldType.TextVariable };
        var slot2 = new SmartTemplateField { Id = "slot2", Label = "الاسم", VariableKey = "الاسم", FieldType = SmartFieldType.TextVariable };

        var result = Service.BuildMappings(new[] { slot1, slot2 }, new[] { "الاسم" });

        Assert.Equal(2, result.Count(m => m.ColumnName == "الاسم"));
    }

    [Fact]
    public void EveryFieldGetsARowEvenWhenUnmatched()
    {
        // The mapping grid has to show unmapped fields, otherwise the operator cannot
        // see what still needs attention.
        var result = Service.BuildMappings(
            new[] { Field("الاسم"), Field("لا يوجد له عمود") },
            new[] { "الاسم" });

        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(MappingConfidence.High, MappingConfidence.Medium, true)]
    [InlineData(MappingConfidence.Medium, MappingConfidence.Low, true)]
    [InlineData(MappingConfidence.Low, MappingConfidence.None, true)]
    [InlineData(MappingConfidence.Medium, MappingConfidence.High, false)]
    [InlineData(MappingConfidence.None, MappingConfidence.Low, false)]
    public void BetterMeansCloserToHigh(MappingConfidence candidate, MappingConfidence current, bool expected)
    {
        // Pins the direction of the comparison that caused the bug.
        Assert.Equal(expected, VariableMappingService.IsBetter(candidate, current));
    }
}
