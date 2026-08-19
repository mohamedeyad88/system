using System.Collections.Generic;
using System.Linq;
using Apex.Services.SmartVariables;
using Apex.Services.SmartVariables.Models;
using Apex.Services.Templates;
using Apex.UI.Services;

namespace Apex.UI.Tests.Rendering;

/// <summary>
/// Conditional rules where they take effect: the renderer every output path shares.
///
/// FieldRule was written and unit-tested but nothing called it, so a discount line
/// printed an empty labelled box on every piece that had no discount — or the job was
/// split in two and merged by hand. Evaluating in the render service means the live
/// preview, the PNG export and the press all make the same decision.
/// </summary>
public class ConditionalRulesInRenderPathTests
{
    private static TemplateSlotDefinition Slot(string id, params FieldRule[] rules) => new()
    {
        Id = id,
        Name = id,
        VariableName = id,
        DataType = SlotDataType.Text,
        X = 5, Y = 5, Width = 50, Height = 10,
        Rules = rules.ToList(),
    };

    private static TemplatePageDefinition Page(params TemplateSlotDefinition[] slots) => new()
    {
        WidthMm = 100,
        HeightMm = 60,
        Slots = slots.ToList(),
    };

    private static SmartDataSource Data(params (string key, string value)[] cells)
    {
        var row = new SmartDataRow { RowIndex = 0 };
        foreach (var (k, v) in cells) row.Values[k] = v;

        return new SmartDataSource
        {
            Columns = cells.Select(c => c.key).ToList(),
            Rows = new List<SmartDataRow> { row },
        };
    }

    private static IReadOnlyList<string> RenderedIds(
        TemplatePageDefinition page, SmartDataSource data)
    {
        var rendered = new TemplateRenderingService().Render(
            page, data, new List<VariableMapping>(),
            recordIndex: 0, assets: new Dictionary<string, byte[]>(), imageFolderPath: null);

        return rendered.Fields.Select(f => f.FieldId).ToList();
    }

    [Fact]
    public void AFieldWithNoRulesAlwaysPrints()
    {
        // Every template made before rules existed must be unaffected.
        var ids = RenderedIds(Page(Slot("name")), Data(("name", "Ali")));

        Assert.Contains("name", ids);
    }

    [Fact]
    public void AFieldIsOmittedWhenItsConditionFails()
    {
        // The case the feature exists for: no discount on this record, so no box.
        var page = Page(
            Slot("name"),
            Slot("discount", new FieldRule("discount", RuleOperator.IsNotEmpty)));

        var ids = RenderedIds(page, Data(("name", "Ali"), ("discount", "")));

        Assert.Contains("name", ids);
        Assert.DoesNotContain("discount", ids);
    }

    [Fact]
    public void TheSameFieldPrintsOnARecordThatSatisfiesTheRule()
    {
        var page = Page(Slot("discount", new FieldRule("discount", RuleOperator.IsNotEmpty)));

        var ids = RenderedIds(page, Data(("discount", "15%")));

        Assert.Contains("discount", ids);
    }

    [Fact]
    public void NumericConditionsCompareAsNumbers()
    {
        // "100" > "9" is false as text — on a threshold rule that hides the field on
        // exactly the records that should show it.
        var page = Page(Slot("vip", new FieldRule("total", RuleOperator.GreaterThan, "9")));

        Assert.Contains("vip", RenderedIds(page, Data(("total", "100"))));
        Assert.DoesNotContain("vip", RenderedIds(page, Data(("total", "5"))));
    }

    [Fact]
    public void HideRemovesTheFieldWhenTheConditionHolds()
    {
        var page = Page(Slot("banner",
            new FieldRule("status", RuleOperator.Equals, "cancelled", RuleAction.Hide)));

        Assert.DoesNotContain("banner", RenderedIds(page, Data(("status", "cancelled"))));
        Assert.Contains("banner", RenderedIds(page, Data(("status", "active"))));
    }

    [Fact]
    public void RulesAreNotAppliedBeforeAnyDataIsPasted()
    {
        // While designing, hiding fields on an empty data set would leave the author
        // staring at a blank page with nothing to select.
        var page = Page(Slot("discount", new FieldRule("discount", RuleOperator.IsNotEmpty)));

        var empty = new SmartDataSource();
        var ids = RenderedIds(page, empty);

        Assert.Contains("discount", ids);
    }

    [Fact]
    public void DecisionsAreMadePerRecord_NotOncePerJob()
    {
        // A variable-data run must be able to show the field on some records and not
        // others; a single decision reused for the whole job would defeat the point.
        var page = Page(Slot("discount", new FieldRule("discount", RuleOperator.IsNotEmpty)));

        var withRows = new SmartDataSource
        {
            Columns = new List<string> { "discount" },
            Rows = new List<SmartDataRow>
            {
                new() { RowIndex = 0, Values = { ["discount"] = "10%" } },
                new() { RowIndex = 1, Values = { ["discount"] = "" } },
            },
        };

        var svc = new TemplateRenderingService();
        var assets = new Dictionary<string, byte[]>();
        var maps = new List<VariableMapping>();

        var first = svc.Render(page, withRows, maps, 0, assets, null);
        var second = svc.Render(page, withRows, maps, 1, assets, null);

        Assert.Single(first.Fields);
        Assert.Empty(second.Fields);
    }
}
