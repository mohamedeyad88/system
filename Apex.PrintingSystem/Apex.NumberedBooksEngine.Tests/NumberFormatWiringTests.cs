using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;

namespace Apex.NumberedBooksEngine.Tests;

/// <summary>
/// The job's number format has to reach whichever component actually draws the
/// number. There are two of them — the composer (preview and PDF) and the print
/// command builder (streaming print) — and the streaming job was configuring only
/// the first. Every job therefore printed NumberFormatOptions.Default: six Western
/// digits, no prefix, no suffix, whatever the operator had chosen. On a numbered
/// book that is found after the paper is cut.
/// </summary>
public class NumberFormatWiringTests
{
    private static readonly IReadOnlyList<SlotSpec> OneSlot = new[]
    {
        new SlotSpec(
            Id: "n1",
            X: 0.1f, Y: 0.1f, Width: 0.3f, Height: 0.05f,
            FontFamily: "Arial", FontSize: 12, FontColorHex: "#000000",
            Align: TextAlign.Left, Rotation: 0, CopyStyles: null)
    };

    private static string DrawnBy(PrintCommandBuilder builder)
    {
        var cmd = builder.BuildGdiCommandWithCopyStyle(
            templateId: "t", pageNumbers: new long[] { 1 },
            slots: OneSlot, dpi: 300, copyType: CopyType.Original);

        return cmd.Slots[0].Text;
    }

    [Fact]
    public void ThePrintBuilderHonoursArabicIndicDigits()
    {
        var builder = new PrintCommandBuilder
        {
            NumberFormat = new NumberFormatOptions(PadDigits: 6, UseArabicDigits: true)
        };

        Assert.Equal("٠٠٠٠٠١", DrawnBy(builder));
    }

    [Fact]
    public void ThePrintBuilderHonoursPrefixSuffixAndPadding()
    {
        var builder = new PrintCommandBuilder
        {
            NumberFormat = new NumberFormatOptions(PadDigits: 4, Prefix: "INV-", Suffix: "/2026")
        };

        Assert.Equal("INV-0001/2026", DrawnBy(builder));
    }

    /// <summary>
    /// The two producers must agree, or the preview shows one thing and the press
    /// produces another.
    /// </summary>
    [Theory]
    [InlineData(6, false, "", "")]
    [InlineData(6, true, "", "")]
    [InlineData(3, false, "A-", "/م")]
    [InlineData(5, true, "س", "")]
    public void BothProducersFormatTheSameNumberIdentically(
        int pad, bool arabic, string prefix, string suffix)
    {
        var format = new NumberFormatOptions(pad, prefix, suffix, arabic);

        var builder = new PrintCommandBuilder { NumberFormat = format };
        var viaComposer = NumberFormatter.Format(1, format);

        Assert.Equal(viaComposer, DrawnBy(builder));
    }

    /// <summary>
    /// The regression itself: the job used to configure the composer and leave the
    /// streaming printer on its defaults, so the operator's choice never reached the
    /// paper. This fails against the old orchestrator.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheJobGivesBothProducersTheSameFormat(bool arabic)
    {
        var orchestrator = new JobOrchestrator();
        var chosen = new NumberFormatOptions(PadDigits: 4, Prefix: "INV-", Suffix: "/2026");

        orchestrator.ApplyNumberFormat(chosen, useArabicDigits: arabic);

        Assert.Equal(orchestrator.ComposerNumberFormat, orchestrator.PrintBuilderNumberFormat);
        Assert.Equal(arabic, orchestrator.PrintBuilderNumberFormat.UseArabicDigits);
        Assert.Equal("INV-", orchestrator.PrintBuilderNumberFormat.Prefix);
        Assert.Equal(4, orchestrator.PrintBuilderNumberFormat.PadDigits);
    }

    /// <summary>
    /// With nothing chosen the two still have to match, rather than one silently
    /// falling back to Default while the other carries the job's digit style.
    /// </summary>
    [Fact]
    public void BothProducersAgreeEvenWhenNoFormatWasChosen()
    {
        var orchestrator = new JobOrchestrator();

        orchestrator.ApplyNumberFormat(null, useArabicDigits: true);

        Assert.Equal(orchestrator.ComposerNumberFormat, orchestrator.PrintBuilderNumberFormat);
        Assert.True(orchestrator.PrintBuilderNumberFormat.UseArabicDigits);
    }
}
