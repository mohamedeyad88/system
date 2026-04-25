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
            // Each page produces 'slotsPerPage' numbers after cutting
            long totalPages = (long)Math.Ceiling((double)options.TotalNumbers / slotsPerPage);
            long startNumber = options.StartNumber;
            long endNumber = startNumber + options.TotalNumbers;

            // #region agent log
            System.Diagnostics.Debug.WriteLine($"[CuttingStrategy] GeneratePageNumbers:");
            System.Diagnostics.Debug.WriteLine($"  StartNumber: {startNumber}");
            System.Diagnostics.Debug.WriteLine($"  TotalNumbers: {options.TotalNumbers}");
            System.Diagnostics.Debug.WriteLine($"  EndNumber: {endNumber}");
            System.Diagnostics.Debug.WriteLine($"  SlotsPerPage: {slotsPerPage}");
            System.Diagnostics.Debug.WriteLine($"  TotalPages: {totalPages}");
            // #endregion

            for (long pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                var pageNumbers = new long[slotsPerPage];

                for (int slotIndex = 0; slotIndex < slotsPerPage; slotIndex++)
                {
                    // Imposition formula: value = start + pageIndex + slotIndex * totalPages
                    long value = startNumber + pageIndex + (slotIndex * totalPages);
                    
                    // Check if value is within the requested range
                    // Value must be >= startNumber and < endNumber
                    if (value >= startNumber && value < endNumber)
                    {
                        pageNumbers[slotIndex] = value;
                    }
                    else
                    {
                        pageNumbers[slotIndex] = -1; // Empty slot
                    }
                }

                // #region agent log
                System.Diagnostics.Debug.WriteLine($"[CuttingStrategy] Page {pageIndex}: [{string.Join(", ", pageNumbers)}]");
                // #endregion

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
