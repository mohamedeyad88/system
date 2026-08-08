using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Shershara (Perforated Pads) numbering strategy.
    /// Numbers increase linearly top-to-bottom within each page.
    /// Used for pad printing where sheets are perforated.
    /// 
    /// Example for 4 slots, starting at 1:
    /// Page 1: [1, 2, 3, 4]
    /// Page 2: [5, 6, 7, 8]
    /// </summary>
    public class ShersharaStrategy : INumberingStrategy
    {
        public IEnumerable<long[]> GeneratePageNumbers(BookJobOptions options)
        {
            int slotsPerPage = options.Slots.Count;
            if (slotsPerPage == 0) yield break;

            long currentNumber = options.StartNumber;
            long endNumber = options.StartNumber + options.TotalNumbers;

            while (currentNumber < endNumber)
            {
                var pageNumbers = new long[slotsPerPage];

                for (int slotIndex = 0; slotIndex < slotsPerPage; slotIndex++)
                {
                    if (currentNumber < endNumber)
                    {
                        pageNumbers[slotIndex] = currentNumber;
                        currentNumber++;
                    }
                    else
                    {
                        // Pad with -1 for empty slots
                        pageNumbers[slotIndex] = -1;
                    }
                }

                yield return pageNumbers;
            }
        }

        /// <summary>
        /// Generates numbers with multi-copy support.
        /// Each number is repeated for each copy type.
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
