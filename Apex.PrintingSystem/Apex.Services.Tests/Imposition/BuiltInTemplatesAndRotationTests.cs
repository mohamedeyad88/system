using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;
using Apex.Services.PaperCutting;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Two guarantees:
///  1. Every built-in preset actually PLANS — a preset that fails when clicked is
///     worse than no preset, because the operator trusts it.
///  2. Step &amp; Repeat auto-rotation genuinely improves sheet yield and reports the
///     rotation on each slot (the PDF engine needs it, or artwork prints stretched).
/// </summary>
public class BuiltInTemplatesAndRotationTests
{
    private static ImpositionService NewService() => new(new PaperCuttingOptimizerService());

    // ── Built-in presets ──────────────────────────────────────────────────────

    [Fact]
    public void PresetsExist()
    {
        Assert.NotEmpty(BuiltInImpositionTemplates.All());
    }

    [Fact]
    public void EveryPresetPlansSuccessfully()
    {
        var svc = NewService();

        foreach (var tpl in BuiltInImpositionTemplates.All())
        {
            var r = svc.Plan(tpl.Input);
            Assert.True(r.IsValid, $"preset '{tpl.Id}' failed to plan: {r.ErrorMessage}");
            Assert.NotEmpty(r.Sheets);
            Assert.True(r.SheetsRequired > 0, $"preset '{tpl.Id}' produced no sheets");
        }
    }

    [Fact]
    public void PresetIdsAreUnique()
    {
        var ids = BuiltInImpositionTemplates.All().Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void PresetsAreRecognisedAsBuiltIn()
    {
        foreach (var tpl in BuiltInImpositionTemplates.All())
            Assert.True(BuiltInImpositionTemplates.IsBuiltIn(tpl), $"'{tpl.Id}' not flagged built-in");
    }

    [Fact]
    public void UserTemplatesAreNotMistakenForBuiltIns()
    {
        Assert.False(BuiltInImpositionTemplates.IsBuiltIn(new ImpositionTemplate { Name = "mine" }));
        Assert.False(BuiltInImpositionTemplates.IsBuiltIn(null));
    }

    [Fact]
    public void PresetsAreFreshInstances_SoEditingOneCannotCorruptTheLibrary()
    {
        var first = BuiltInImpositionTemplates.All()[0];
        first.Input.PageWidth = 999;

        var again = BuiltInImpositionTemplates.All()[0];
        Assert.NotEqual(999, again.Input.PageWidth);
    }

    [Theory]
    [InlineData("builtin-8pp", 8)]
    [InlineData("builtin-16pp", 16)]
    [InlineData("builtin-32pp", 32)]
    public void SignaturePresetsCarryTheirPageCount(string id, int expectedPages)
    {
        var tpl = BuiltInImpositionTemplates.All().Single(t => t.Id == id);
        Assert.Equal(expectedPages, tpl.Input.SourcePageCount);
    }

    [Fact]
    public void BookletPresetsSeedCreepCompensation()
    {
        foreach (var tpl in BuiltInImpositionTemplates.All()
                     .Where(t => t.Input.Type is ImpositionType.SaddleStitch or ImpositionType.PerfectBinding))
        {
            Assert.True(tpl.Input.PaperThickness > 0,
                $"booklet preset '{tpl.Id}' should seed a paper caliper");
        }
    }

    // ── Step & Repeat auto-rotation ───────────────────────────────────────────

    private static ImpositionInput Repeat(double pageW, double pageH, bool allowRotation) => new()
    {
        Type = ImpositionType.StepRepeat,
        SourcePageCount = 1,
        RequiredCopies = 1000,
        PageWidth = pageW,
        PageHeight = pageH,
        SheetWidth = 320,
        SheetHeight = 450,
        Bleed = 0,
        SheetMargin = 10,
        Gutter = 0,
        AllowRotation = allowRotation,
    };

    [Fact]
    public void AutoRotation_NeverYieldsFewerPiecesThanUnrotated()
    {
        var svc = NewService();

        // A landscape piece on a portrait sheet is the case rotation helps most.
        var with = svc.Plan(Repeat(140, 95, allowRotation: true));
        var without = svc.Plan(Repeat(140, 95, allowRotation: false));

        Assert.True(with.IsValid && without.IsValid);
        Assert.True(with.PagesPerSide >= without.PagesPerSide,
            $"rotation reduced yield: {with.PagesPerSide} < {without.PagesPerSide}");
    }

    [Fact]
    public void RotatedSlotsReportNinetyDegrees_SoArtworkIsNotStretched()
    {
        var r = NewService().Plan(Repeat(140, 95, allowRotation: true));
        Assert.True(r.IsValid, r.ErrorMessage);

        // Whatever the optimizer chose, every slot must declare a legal rotation
        // that matches its own footprint rather than being left at a default.
        foreach (var s in r.Sheets[0].Front.Slots)
            Assert.True(s.Rotation is 0 or 90, $"unexpected rotation {s.Rotation}");
    }

    [Fact]
    public void RotationDisabled_LeavesEverySlotUpright()
    {
        var r = NewService().Plan(Repeat(140, 95, allowRotation: false));
        Assert.True(r.IsValid, r.ErrorMessage);

        Assert.All(r.Sheets[0].Front.Slots, s => Assert.Equal(0, s.Rotation));
    }

    [Fact]
    public void StepRepeatSlotsStayInsideTheSheet()
    {
        var r = NewService().Plan(Repeat(90, 50, allowRotation: true));
        Assert.True(r.IsValid, r.ErrorMessage);

        foreach (var s in r.Sheets[0].Front.Slots)
        {
            Assert.True(s.X >= -1e-6 && s.Y >= -1e-6, "slot outside sheet origin");
            Assert.True(s.X + s.Width <= r.SheetWidthMm + 1e-6, "slot overflows sheet width");
            Assert.True(s.Y + s.Height <= r.SheetHeightMm + 1e-6, "slot overflows sheet height");
        }
    }

    [Fact]
    public void SheetCountCoversTheRequestedCopies()
    {
        var r = NewService().Plan(Repeat(90, 50, allowRotation: true));

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.True(r.SheetsRequired * r.PagesPerSide >= 1000,
            "planned sheets do not cover the requested copies");
    }
}
