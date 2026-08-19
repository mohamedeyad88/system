using Apex.NumberedBooksEngine.Core;

namespace Apex.NumberedBooksEngine.Tests;

/// <summary>
/// The number drawn on the design has to be the number that reaches the paper.
///
/// The designer used to render slots with ToString("D4") — four Western digits, no
/// prefix, no suffix — while the press used the job's real format. An operator sizing
/// and placing a slot against "0001" could find "INV-000123/2026" printed into it,
/// and the mismatch only shows up once the sheets are cut.
/// </summary>
public class PreviewMatchesPrintTests
{
    public static TheoryData<int, string, string, bool, string> Cases => new()
    {
        //  pad  prefix  suffix   arabic   expected for the number 1
        {  4,   "",     "",      false,   "0001" },
        {  6,   "",     "",      false,   "000001" },
        {  6,   "",     "",      true,    "٠٠٠٠٠١" },
        {  6,   "INV-", "/2026", false,   "INV-000001/2026" },
        {  3,   "أ-",   "/م",    true,    "أ-٠٠١/م" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void OneFormatterServesPreviewAndPress(
        int pad, string prefix, string suffix, bool arabic, string expected)
    {
        var format = new NumberFormatOptions(pad, prefix, suffix, arabic);

        // What the designer paints into a slot.
        var onScreen = NumberFormatter.Format(1, format);

        // What the streaming printer emits, once the orchestrator has handed the
        // format to the command builder (see NumberFormatWiringTests).
        var builder = new PrintCommandBuilder { NumberFormat = format };
        var onPaper = builder.BuildGdiCommandWithCopyStyle(
            templateId: "t",
            pageNumbers: new long[] { 1 },
            slots: new[]
            {
                new Models.SlotSpec(
                    Id: "n1", X: .1f, Y: .1f, Width: .3f, Height: .05f,
                    FontFamily: "Arial", FontSize: 12, FontColorHex: "#000000",
                    Align: Models.TextAlign.Left, Rotation: 0, CopyStyles: null)
            },
            dpi: 300,
            copyType: Models.CopyType.Original).Slots[0].Text;

        Assert.Equal(expected, onScreen);
        Assert.Equal(onScreen, onPaper);
    }

    /// <summary>
    /// A four-digit preview is only correct when four digits were asked for — the old
    /// hard-coded "D4" happened to look right at that one setting, which is why the
    /// mismatch went unnoticed.
    /// </summary>
    [Fact]
    public void TheOldHardCodedFourDigitPreviewDisagreesWithTheDefaultJob()
    {
        var defaultJob = new NumberFormatOptions();   // PadDigits = 6
        Assert.NotEqual(1L.ToString("D4"), NumberFormatter.Format(1, defaultJob));
    }
}
