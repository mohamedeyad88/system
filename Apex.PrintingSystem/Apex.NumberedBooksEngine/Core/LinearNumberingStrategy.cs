using Apex.NumberedBooksEngine.Models;
using System.Collections.Generic;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Linear numbering: Slots on each page are numbered sequentially.
    /// Page 1: [1, 2, 3, ...slotCount]
    /// Page 2: [slotCount+1, slotCount+2, ...]
    /// Used for pads without cutting.
    /// </summary>
    public class LinearNumberingStrategy : INumberingStrategy
    {
        public IEnumerable<long[]> GeneratePageNumbers(BookJobOptions options)
        {
            int slotCount = options.Slots.Count;
            if (slotCount == 0) yield break;

            long totalNumbers = options.TotalNumbers;
            long currentNumber = options.StartNumber;
            long numbersGenerated = 0;

            while (numbersGenerated < totalNumbers)
            {
                var pageNumbers = new long[slotCount];

                for (int i = 0; i < slotCount; i++)
                {
                    if (numbersGenerated < totalNumbers)
                    {
                        pageNumbers[i] = currentNumber++;
                        numbersGenerated++;
                    }
                    else
                    {
                        pageNumbers[i] = -1; // Empty slot marker
                    }
                }

                yield return pageNumbers;
            }
        }
    }
}
