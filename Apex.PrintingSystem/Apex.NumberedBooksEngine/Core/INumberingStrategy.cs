using Apex.NumberedBooksEngine.Models;
using System.Collections.Generic;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Defines the strategy for generating page numbers based on job options.
    /// </summary>
    public interface INumberingStrategy
    {
        /// <summary>
        /// Generates numbers for each page/sheet in the job.
        /// </summary>
        /// <param name="options">Job configuration.</param>
        /// <returns>An enumerable of number arrays, where each array represents the numbers for all slots on a single page/sheet.</returns>
        IEnumerable<long[]> GeneratePageNumbers(BookJobOptions options);
    }
}
