using Apex.Services.SmartVariables.Models;
using Apex.UI.ViewModels;

namespace Apex.UI.Tests;

/// <summary>
/// Headless tests for <see cref="SmartVariablesViewModel"/> core data logic: pasted-data
/// parsing (driven by the settable <c>RawPasteText</c>, no clipboard) and auto-mapping of
/// template fields to data columns. The ViewModel has a parameterless constructor.
/// </summary>
public class SmartVariablesViewModelTests
{
    private static SmartVariablesViewModel NewVm() => new();

    [Fact]
    public void LoadSampleData_PopulatesColumnsAndRows()
    {
        var vm = NewVm();
        vm.LoadSampleDataCommand.Execute(null);

        Assert.True(vm.HasPastedData);
        Assert.Equal(5, vm.ColumnCount);                 // الاسم/الصف/رقم الجلوس/الصورة/الكود
        Assert.Equal(3, vm.TotalRowCount);
        Assert.Contains("الاسم", vm.DataColumns);
    }

    [Fact]
    public void RawPasteText_ParsesTabSeparatedData()
    {
        var vm = NewVm();
        vm.RawPasteText = "Name\tCode\r\nAhmed\t001\r\nSara\t002";

        Assert.True(vm.HasPastedData);
        Assert.Equal(2, vm.ColumnCount);
        Assert.Equal(2, vm.TotalRowCount);
        Assert.Contains("Name", vm.DataColumns);
        Assert.Contains("Code", vm.DataColumns);
    }

    [Fact]
    public void ClearData_ResetsState()
    {
        var vm = NewVm();
        vm.LoadSampleDataCommand.Execute(null);
        Assert.True(vm.HasPastedData);

        vm.ClearDataCommand.Execute(null);

        Assert.False(vm.HasPastedData);
        Assert.Empty(vm.DataColumns);
        Assert.Equal(0, vm.TotalRowCount);
    }

    [Fact]
    public void AutoMap_BuildsOneMappingPerField()
    {
        var vm = NewVm();
        vm.LoadSampleDataCommand.Execute(null);          // 5 columns

        var fields = new List<SmartTemplateField>
        {
            new() { Label = "الاسم", VariableKey = "name", FieldType = SmartFieldType.TextVariable },
            new() { Label = "الكود", VariableKey = "code", FieldType = SmartFieldType.TextVariable },
        };
        vm.SetTemplateFields(fields);                    // triggers AutoMap internally

        // Characterize: auto-map produces a mapping row per template field.
        Assert.Equal(2, vm.MappingRows.Count);
    }

    [Fact]
    public void SetMapping_AppliesColumnToField()
    {
        var vm = NewVm();
        vm.LoadSampleDataCommand.Execute(null);

        var field = new SmartTemplateField { Label = "اسم", VariableKey = "fld", FieldType = SmartFieldType.TextVariable };
        vm.SetTemplateFields(new List<SmartTemplateField> { field });

        vm.SetMapping(field.Id, "الصف");                 // manual binding to a real column

        Assert.True(vm.MappedCount > 0);
    }
}
