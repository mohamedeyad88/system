using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Cutting (Imposed Sheets) numbering strategy.
    /// Uses imposition formula: value = start + pageIndex + slotIndex * totalPages
    /// Used for sheets that will be cut into smaller pieces.
    /// 
    /// Example for 4 slots, 2500 pages, starting at 1:
    /// Page 0: [1, 2501, 5001, 7501]
    /// Page 1: [2, 2502, 5002, 7502]
    /// ...
    /// Page 2499: [2500, 5000, 7500, 10000]
    /// </summary>
    public class CuttingStrategy : INumberingStrategy
    {
        public IEnumerable<long[]> GeneratePageNumbers(BookJobOptions options)
        {
            int slotsPerPage = options.Slots.Count;
            if (slotsPerPage == 0) yield break;

            // Calculate total pages needed
            long totalPages = (long)Math.Ceiling((double)options.TotalNumbers / slotsPerPage);
            long startNumber = options.StartNumber;

            for (long pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                var pageNumbers = new long[slotsPerPage];

                for (int slotIndex = 0; slotIndex < slotsPerPage; slotIndex++)
                {
                    // Imposition formula: value = start + pageIndex + slotIndex * totalPages
                    long value = startNumber + pageIndex + (slotIndex * totalPages);
                    
                    // Check if value exceeds total range
                    if (value < startNumber + options.TotalNumbers)
                    {
                        pageNumbers[slotIndex] = value;
                    }
                    else
                    {
                        pageNumbers[slotIndex] = -1; // Empty slot
                    }
                }

                yield return pageNumbers;
            }
        }

        /// <summary>
        /// Generates numbers with multi-copy support.
        /// </summary>
        public IEnumerable<(long[] Numbers, CopyType CopyType)> GenerateMultiCopyPageNumbers(
            BookJobOptions options,
            IReadOnlyList<CopyType> copyTypes)
        {
            foreach (var pageNumbers in GeneratePageNumbers(options))
            {
                foreach (var copyType in copyTypes)
                {
                    yield return (pageNumbers, copyType);
                }
            }
        }
    }
}
