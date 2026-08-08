namespace Apex.Core.Models.PaperCutting
{
    /// <summary>
    /// Controls how the optimizer selects among the available cutting patterns.
    /// </summary>
    public enum PaperOptimizationMode
    {
        /// <summary>Maximise the number of pieces cut from one raw sheet.</summary>
        MaximumPieces,

        /// <summary>Minimise paper waste area.</summary>
        MinimumWaste,

        /// <summary>Best overall sheet utilisation (area used %).</summary>
        BestUtilization,

        /// <summary>Prefer the simplest cut sequence (fewest cuts).</summary>
        SimpleCutFirst
    }
}
