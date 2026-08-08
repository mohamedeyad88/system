using System.Collections.Generic;
using System.Linq;
using Apex.Services.SmartVariables;
using Apex.Services.SmartVariables.Models;

namespace Apex.UI.Tests.SmartVariables;

/// <summary>
/// A column the operator picks by hand has to stay picked.
///
/// Auto-map rebuilt the whole mapping list, and it is re-run automatically whenever
/// the template fields are synced — which happens on every step change AND every tab
/// switch. So a field bound by hand on the canvas was silently reverted the moment the
/// user navigated anywhere. From the operator's side the binding simply "did not work",
/// with nothing on screen to say why.
///
/// These tests pin the merge rule that auto-map must follow.
/// </summary>
public class ManualMappingSurvivesTests
{
    private static readonly VariableMappingService Service = new();

    private static SmartTemplateField Field(string id, string label) => new()
    {
        Id = id,
        Label = label,
        VariableKey = label,
        FieldType = SmartFieldType.TextVariable,
    };

    /// <summary>
    /// The merge the ViewModel performs: rebuild by name-matching, then restore any
    /// column the operator had chosen, provided it still exists in the data.
    /// </summary>
    private static List<VariableMapping> AutoMapPreservingManual(
        IReadOnlyList<SmartTemplateField> fields,
        IReadOnlyList<string> columns,
        List<VariableMapping> existing)
    {
        var mappings = Service.BuildMappings(fields, columns);

        foreach (var m in mappings)
        {
            var manual = existing.FirstOrDefault(
                e => e.FieldId == m.FieldId && !string.IsNullOrEmpty(e.ColumnName));
            if (manual == null) continue;

            if (columns.Contains(manual.ColumnName!, System.StringComparer.OrdinalIgnoreCase))
            {
                m.ColumnName = manual.ColumnName;
                m.Confidence = manual.Confidence == MappingConfidence.None
                    ? MappingConfidence.High
                    : manual.Confidence;
            }
        }
        return mappings;
    }

    [Fact]
    public void AHandPickedColumnSurvivesAutoMap()
    {
        // The reported bug, reduced: a field whose name does not resemble any column
        // is bound by hand, then the user moves to another step.
        var fields = new List<SmartTemplateField> { Field("f1", "الحقل الأول") };
        var columns = new List<string> { "الاسم", "الصف", "الدرجة" };

        var manual = new List<VariableMapping>
        {
            new() { FieldId = "f1", ColumnName = "الدرجة", Confidence = MappingConfidence.High },
        };

        var result = AutoMapPreservingManual(fields, columns, manual);

        Assert.Equal("الدرجة", result.Single(m => m.FieldId == "f1").ColumnName);
    }

    [Fact]
    public void AHandPickedColumnBeatsWhatAutoMapWouldHaveGuessed()
    {
        // Auto-map would match "الاسم" to the field called "الاسم". The operator
        // deliberately chose a different column; their choice wins.
        var fields = new List<SmartTemplateField> { Field("f1", "الاسم") };
        var columns = new List<string> { "الاسم", "اسم الأب" };

        var guessed = Service.BuildMappings(fields, columns).Single();
        Assert.Equal("الاسم", guessed.ColumnName);   // confirms auto-map's preference

        var manual = new List<VariableMapping>
        {
            new() { FieldId = "f1", ColumnName = "اسم الأب", Confidence = MappingConfidence.High },
        };

        var result = AutoMapPreservingManual(fields, columns, manual);

        Assert.Equal("اسم الأب", result.Single().ColumnName);
    }

    [Fact]
    public void RepeatedNavigationDoesNotErodeTheBinding()
    {
        // Every tab switch re-runs the sync. Surviving once is not enough.
        var fields = new List<SmartTemplateField> { Field("f1", "حقل") };
        var columns = new List<string> { "الاسم", "الدرجة" };

        var mappings = new List<VariableMapping>
        {
            new() { FieldId = "f1", ColumnName = "الدرجة", Confidence = MappingConfidence.High },
        };

        for (int i = 0; i < 10; i++)
            mappings = AutoMapPreservingManual(fields, columns, mappings);

        Assert.Equal("الدرجة", mappings.Single().ColumnName);
    }

    [Fact]
    public void UnmappedFieldsAreStillFilledInAutomatically()
    {
        // Preserving manual choices must not disable auto-map for everything else.
        var fields = new List<SmartTemplateField> { Field("f1", "حقل"), Field("f2", "الاسم") };
        var columns = new List<string> { "الاسم", "الدرجة" };

        var manual = new List<VariableMapping>
        {
            new() { FieldId = "f1", ColumnName = "الدرجة", Confidence = MappingConfidence.High },
        };

        var result = AutoMapPreservingManual(fields, columns, manual);

        Assert.Equal("الدرجة", result.Single(m => m.FieldId == "f1").ColumnName);
        Assert.Equal("الاسم", result.Single(m => m.FieldId == "f2").ColumnName);
    }

    [Fact]
    public void AManualColumnThatNoLongerExistsIsDropped()
    {
        // The operator pasted different data. Keeping a pointer to a column that is
        // gone would render blank on every record with no explanation.
        var fields = new List<SmartTemplateField> { Field("f1", "حقل") };
        var newColumns = new List<string> { "الاسم", "الصف" };

        var manual = new List<VariableMapping>
        {
            new() { FieldId = "f1", ColumnName = "عمود قديم", Confidence = MappingConfidence.High },
        };

        var result = AutoMapPreservingManual(fields, newColumns, manual);

        Assert.NotEqual("عمود قديم", result.Single().ColumnName);
    }

    [Fact]
    public void ClearingAMappingIsRespected_NotUndoneByAutoMap()
    {
        // Unbinding is also a deliberate act, but it must not fight auto-map forever:
        // an empty ColumnName is treated as "not manually set", so auto-map may fill
        // it again. Clear mappings is the way to start over.
        var fields = new List<SmartTemplateField> { Field("f1", "الاسم") };
        var columns = new List<string> { "الاسم" };

        var cleared = new List<VariableMapping>
        {
            new() { FieldId = "f1", ColumnName = null, Confidence = MappingConfidence.None },
        };

        var result = AutoMapPreservingManual(fields, columns, cleared);

        Assert.Equal("الاسم", result.Single().ColumnName);
    }
}
