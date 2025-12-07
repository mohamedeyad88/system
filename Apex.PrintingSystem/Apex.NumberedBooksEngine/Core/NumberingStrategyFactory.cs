using Apex.NumberedBooksEngine.Models;
using System;
using System.Linq;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Factory to create the appropriate numbering strategy based on job options or auto-detection.
    /// </summary>
    public static class NumberingStrategyFactory
    {
        public static INumberingStrategy Create(BookJobOptions options)
        {
            return options.Mode switch
            {
                NumberingMode.Linear or NumberingMode.Shershara => new ShersharaStrategy(),
                NumberingMode.Imposed or NumberingMode.Cutting => new CuttingStrategy(),
                NumberingMode.Auto => AutoDetect(options),
                NumberingMode.Custom => new LinearNumberingStrategy(), // Future: Implement CustomPatternStrategy
                _ => new LinearNumberingStrategy()
            };
        }

        /// <summary>
        /// Auto-detects the best strategy based on slot configuration using LayoutAutoDetector.
        /// </summary>
        private static INumberingStrategy AutoDetect(BookJobOptions options)
        {
            var detectedMode = LayoutAutoDetector.DetectMode(options.Slots);
            
            return detectedMode switch
            {
                NumberingMode.Linear or NumberingMode.Shershara => new ShersharaStrategy(),
                NumberingMode.Imposed or NumberingMode.Cutting => new CuttingStrategy(),
                _ => new LinearNumberingStrategy()
            };
        }
    }
}
