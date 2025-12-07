using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Extended copy style definition with all display properties.
    /// </summary>
    public record CopyStyleDefinition(
        CopyType Type,
        string Label,           // "أصل" or "صورة 1"
        string ColorHex,
        float Opacity,
        float FontSizeMultiplier,
        bool ShowLabel,
        string? FontWeightOverride
    )
    {
        /// <summary>
        /// Creates default styles for Original + 3 copies.
        /// </summary>
        public static IReadOnlyList<CopyStyleDefinition> CreateDefaults()
        {
            return new[]
            {
                new CopyStyleDefinition(CopyType.Original, "أصل", "#000000", 1.0f, 1.0f, true, null),
                new CopyStyleDefinition(CopyType.Copy1, "صورة 1", "#FF0000", 0.9f, 1.0f, true, null),
                new CopyStyleDefinition(CopyType.Copy2, "صورة 2", "#0000FF", 0.85f, 1.0f, true, null),
                new CopyStyleDefinition(CopyType.Copy3, "صورة 3", "#808080", 0.8f, 1.0f, true, null)
            };
        }

        /// <summary>
        /// Creates a simple style with no label.
        /// </summary>
        public static CopyStyleDefinition Simple(CopyType type, string colorHex, float opacity = 1.0f)
        {
            return new CopyStyleDefinition(type, "", colorHex, opacity, 1.0f, false, null);
        }
    }

    /// <summary>
    /// Options for multi-copy numbering generation.
    /// </summary>
    public record MultiCopyOptions(
        bool Enabled,
        IReadOnlyList<CopyStyleDefinition> CopyStyles,
        bool PrintCopiesTogether  // If true, all copies of a number print before next number
    )
    {
        public static MultiCopyOptions Disabled => new(false, Array.Empty<CopyStyleDefinition>(), false);
        
        public static MultiCopyOptions Default => new(
            true, 
            CopyStyleDefinition.CreateDefaults(), 
            true
        );
    }

    /// <summary>
    /// Result of number generation including copy information.
    /// </summary>
    public record NumberedPage(
        long[] Numbers,
        CopyType CopyType,
        CopyStyleDefinition Style
    );
}
