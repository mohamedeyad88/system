namespace Apex.Services.Printing
{
    /// <summary>
    /// How a batch of files is spread over the selected printers.
    /// </summary>
    public enum PrintDistributionMode
    {
        /// <summary>
        /// Deal the files round-robin across the printers so a long queue finishes
        /// sooner. Each file is printed ONCE.
        /// </summary>
        LoadBalance,

        /// <summary>
        /// Send EVERY file to EVERY printer — the same documents come out at each
        /// station (branch copies, or a spare set). Each file is printed N times.
        /// </summary>
        Duplicate
    }
}
