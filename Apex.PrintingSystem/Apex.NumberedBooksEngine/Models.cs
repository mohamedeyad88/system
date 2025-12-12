using System;
using System.Collections.Generic;
using System.IO;

namespace Apex.NumberedBooksEngine.Models
{
    public enum TextAlign { Left, Center, Right }
    
    /// <summary>
    /// Numbering mode determines how numbers are distributed across slots.
    /// </summary>
    public enum NumberingMode 
    { 
        /// <summary>Auto-detect based on slot layout analysis.</summary>
        Auto, 
        /// <summary>Linear top-to-bottom (Shershara pads).</summary>
        Linear,
        /// <summary>Linear mode alias for Shershara pads.</summary>
        Shershara = Linear,
        /// <summary>Imposed grid formula (Cutting sheets).</summary>
        Imposed,
        /// <summary>Cutting mode alias for imposed sheets.</summary>
        Cutting = Imposed,
        /// <summary>User-defined custom pattern.</summary>
        Custom
    }
    
    /// <summary>
    /// Copy type for multi-copy numbering (Original + Images).
    /// </summary>
    public enum CopyType 
    { 
        Original, 
        Copy1, 
        Copy2, 
        Copy3 
    }
    
    public enum OutputMode { SinglePdf, PerBook, PrintQueue }
    public enum LayoutSpec { A4, A5, TwoA5InA4, A3Plus, Custom }

    /// <summary>
    /// Style definition for a copy (color, opacity, label).
    /// </summary>
    public record CopyStyle(string Label, string ColorHex, float Opacity);

    public record SlotSpec(
        string Id,
        float X, float Y, float Width, float Height, // Normalized 0..1
        string FontFamily,
        float FontSize,
        string FontColorHex,
        TextAlign Align,
        float Rotation,
        CopyStyle[]? CopyStyles
    );

    public enum TemplateFormat { Image, Pdf }

    public record BookJobOptions(
        Stream? TemplateStream,
        string? TemplatePath,
        TemplateFormat TemplateFormat,
        LayoutSpec Layout,
        IReadOnlyList<SlotSpec> Slots,
        long StartNumber,
        long TotalNumbers,
        int PagesPerBook,
        int CopiesPerPage,
        NumberingMode Mode,
        bool LowResourceMode,
        int DegreeOfParallelism,
        int CheckpointEvery,
        string OutputMode,
        string OutputPath, // Directory or File path
        long Step = 1 // Default step
    );

    public record ProgressInfo(long PagesGenerated, long TotalPages, double Percent, long LastNumber);

    public record JobResult(
        string JobId,
        bool Success,
        string OutputPath,
        long TotalPagesGenerated,
        List<string> Errors
    );
}
