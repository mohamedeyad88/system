namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// One independent counter in a job.
    ///
    /// A single sheet often carries more than one number, and they are not the same
    /// number formatted twice: a receipt book runs its receipt number (REC-000123) and
    /// an audit control number (2026-00456) side by side, each with its own range,
    /// step and format. Before this, every slot drew from one counter, so the second
    /// number could only be produced by running the job twice and collating by hand.
    /// </summary>
    /// <param name="Id">
    /// Matches <see cref="Apex.NumberedBooksEngine.Models.SlotSpec.SeriesId"/>. The
    /// first series in the list is the primary one, used by slots that name no series.
    /// </param>
    /// <param name="StartNumber">First value this series issues.</param>
    /// <param name="TotalNumbers">How many values this series issues.</param>
    /// <param name="Step">Increment between values.</param>
    /// <param name="Format">
    /// Padding, prefix, suffix and digit style for this series. Null uses the default —
    /// which is why two series can print identical digits with different prefixes.
    /// </param>
    public sealed record NumberSeries(
        string Id,
        long StartNumber,
        long TotalNumbers,
        long Step = 1,
        NumberFormatOptions? Format = null)
    {
        /// <summary>The value at a zero-based position in this series.</summary>
        public long ValueAt(long index) => StartNumber + index * Step;

        /// <summary>Renders a value using this series' own format.</summary>
        public string FormatValue(long value) =>
            NumberFormatter.Format(value, Format ?? NumberFormatOptions.Default);
    }
}
