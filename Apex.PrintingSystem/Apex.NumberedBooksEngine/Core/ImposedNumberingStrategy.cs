using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Imposed numbering: For printing with cutting.
    /// Formula: value = startNumber + sheetIndex + (slotIndex * totalSheets)
    /// 
    /// Example with 4 slots, 100 total numbers (25 sheets):
    /// Sheet 0: [1, 26, 51, 76]
    /// Sheet 1: [2, 27, 52, 77]
    /// ...
    /// After cutting and stacking, you get sequential pads.
    /// </summary>
    public class ImposedNumberingStrategy : INumberingStrategy
    {
        public IEnumerable<long[]> GeneratePageNumbers(BookJobOptions options)
        {
            int slotCount = options.Slots.Count;
            if (slotCount == 0) yield break;

            long totalNumbers = options.TotalNumbers;
            long startNumber = options.StartNumber;

            // Calculate total sheets needed
            // Each sheet produces 'slotCount' numbers after cutting
            long totalSheets = (long)Math.Ceiling((double)totalNumbers / slotCount);

            for (long sheetIndex = 0; sheetIndex < totalSheets; sheetIndex++)
            {
                var pageNumbers = new long[slotCount];

                for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
                {
                    // Imposition formula
                    long value = startNumber + sheetIndex + (slotIndex * totalSheets);
                    
                    // Only include if within the requested range
                    if (value < startNumber + totalNumbers)
                    {
                        pageNumbers[slotIndex] = value;
                    }
                    else
                    {
                        pageNumbers[slotIndex] = -1; // Empty slot marker
                    }
                }

                yield return pageNumbers;
            }
        }
    }
}
