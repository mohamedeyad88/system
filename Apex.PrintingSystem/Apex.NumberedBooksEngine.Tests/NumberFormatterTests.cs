using Apex.NumberedBooksEngine.Core;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests;

/// <summary>
/// The printed-number format.
///
/// This logic used to exist twice with different padding — the composer produced
/// 4 digits while the print-command builder produced 6, and only one of them
/// applied Arabic-Indic digits. On numbered documents that means the preview and
/// the bound book disagree, which the customer only discovers after the run.
/// </summary>
public class NumberFormatterTests
{
    [Theory]
    [InlineData(1, 6, "000001")]
    [InlineData(123, 6, "000123")]
    [InlineData(1, 4, "0001")]
    [InlineData(999999, 6, "999999")]
    public void PadsToTheRequestedDigits(long number, int pad, string expected)
    {
        Assert.Equal(expected, NumberFormatter.Format(number, new NumberFormatOptions(PadDigits: pad)));
    }

    [Fact]
    public void NumberLongerThanThePadding_IsNotTruncated()
    {
        // Losing a digit would print the WRONG number — never acceptable.
        Assert.Equal("1234567", NumberFormatter.Format(1234567, new NumberFormatOptions(PadDigits: 4)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void InvalidPadding_FallsBackToAtLeastOneDigit(int pad)
    {
        Assert.Equal("7", NumberFormatter.Format(7, new NumberFormatOptions(PadDigits: pad)));
    }

    [Fact]
    public void AppliesPrefixAndSuffix()
    {
        var opts = new NumberFormatOptions(PadDigits: 6, Prefix: "INV-", Suffix: "/2026");
        Assert.Equal("INV-000123/2026", NumberFormatter.Format(123, opts));
    }

    [Fact]
    public void ArabicIndicDigits_AreConverted()
    {
        var opts = new NumberFormatOptions(PadDigits: 4, UseArabicDigits: true);
        Assert.Equal("٠١٢٣", NumberFormatter.Format(123, opts));
    }

    [Fact]
    public void ArabicIndic_LeavesPrefixAndSuffixLettersAlone()
    {
        var opts = new NumberFormatOptions(PadDigits: 3, Prefix: "أ-", Suffix: "/م", UseArabicDigits: true);
        Assert.Equal("أ-٠٠١/م", NumberFormatter.Format(1, opts));
    }

    [Fact]
    public void DefaultIsSixDigitsWestern()
    {
        Assert.Equal("000042", NumberFormatter.Format(42));
    }

    [Fact]
    public void CopyLabel_IsAppendedAfterTheNumber()
    {
        Assert.Equal("000005 / أصل",
            NumberFormatter.FormatWithLabel(5, "أصل", new NumberFormatOptions(PadDigits: 6)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoCopyLabel_LeavesTheNumberBare(string? label)
    {
        Assert.Equal("000005", NumberFormatter.FormatWithLabel(5, label, new NumberFormatOptions(PadDigits: 6)));
    }

    [Fact]
    public void ToArabicIndic_ConvertsOnlyDigits()
    {
        Assert.Equal("أ-٠٠٩/ب", NumberFormatter.ToArabicIndic("أ-009/ب"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ToArabicIndic_HandlesEmptyInput(string? input)
    {
        Assert.Equal(input, NumberFormatter.ToArabicIndic(input!));
    }

    [Fact]
    public void NullOptions_UseTheDefault()
    {
        Assert.Equal(NumberFormatter.Format(9, NumberFormatOptions.Default),
                     NumberFormatter.Format(9, null));
    }
}
