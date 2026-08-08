namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Print scaling mode for controlling how content is scaled during printing.
    /// </summary>
    public enum PrintScaleMode
    {
        /// <summary>
        /// Print at actual size (100% scale). No auto-scaling. Preserves original document dimensions.
        /// </summary>
        ActualSize = 0,

        /// <summary>
        /// Fit content to page. Calculates scale factor based on paper size while maintaining aspect ratio.
        /// </summary>
        FitToPage = 1
    }
}

