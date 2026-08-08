using System.Text;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// How a sequence value is turned into the string that gets printed.
    ///
    /// Official documents rarely want a bare integer: invoice and receipt books
    /// carry a series prefix and often a year suffix (e.g. <c>INV-000123/2026</c>),
    /// and the digit count is fixed by the book's design.
    /// </summary>
    public sealed record NumberFormatOptions(
        int PadDigits = 6,
        string Prefix = "",
        string Suffix = "",
        bool UseArabicDigits = false)
    {
        public static readonly NumberFormatOptions Default = new();
    }

    /// <summary>
    /// THE single place a printed number is formatted.
    ///
    /// There used to be two copies of this logic that disagreed: the composer padded
    /// to 4 digits while the print-command builder padded to 6, and only the composer
    /// applied Arabic-Indic digits. The preview therefore showed something different
    /// from what the press produced — on numbered documents that is a defect the
    /// customer discovers after the books are bound.
    /// </summary>
    public static class NumberFormatter
    {
        /// <summary>Formats the bare number (padding + prefix/suffix + digit style).</summary>
        public static string Format(long number, NumberFormatOptions? options = null)
        {
            var o = options ?? NumberFormatOptions.Default;

            int pad = o.PadDigits < 1 ? 1 : o.PadDigits;
            string digits = number.ToString().PadLeft(pad, '0');

            if (o.UseArabicDigits)
                digits = ToArabicIndic(digits);

            return $"{o.Prefix}{digits}{o.Suffix}";
        }

        /// <summary>
        /// Formats the number and appends the copy label (أصل / صورة) used on
        /// carbonless sets, keeping the "number / label" shape both paths produced.
        /// </summary>
        public static string FormatWithLabel(long number, string? label, NumberFormatOptions? options = null)
        {
            string text = Format(number, options);
            return string.IsNullOrEmpty(label) ? text : $"{text} / {label}";
        }

        /// <summary>Converts Western digits to Arabic-Indic (٠١٢٣…), leaving other characters alone.</summary>
        public static string ToArabicIndic(string western)
        {
            if (string.IsNullOrEmpty(western)) return western;

            var sb = new StringBuilder(western.Length);
            foreach (char c in western)
                sb.Append(c is >= '0' and <= '9' ? (char)('٠' + (c - '0')) : c);
            return sb.ToString();
        }
    }
}
