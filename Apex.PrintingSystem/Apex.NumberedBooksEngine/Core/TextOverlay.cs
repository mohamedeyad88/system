using Apex.NumberedBooksEngine.Models;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// One number to be drawn as live text on a printed page, rather than baked into a
    /// raster of the whole sheet.
    ///
    /// <para>Composing a full-page image per sheet is what made long runs unusable: a
    /// 300&#160;dpi A4 page is 2480&#215;3508, so every sheet allocated a ~35&#160;MB surface,
    /// re-drew the template into it, PNG-encoded it, decoded it again and handed the
    /// spooler a full-page bitmap. A 100,000-number book in two copies is 200,000 of those.
    /// The number also ended up as 300&#160;dpi pixels on a 600 or 1200&#160;dpi press, which
    /// is why operators saw the digits come out jagged next to crisp artwork.</para>
    ///
    /// <para>With an overlay the template bitmap is prepared once for the whole job and the
    /// number is drawn with the printer's own text engine at the device's real resolution:
    /// no per-page surface, no per-page encode, and type as sharp as the press can make it.</para>
    /// </summary>
    /// <param name="Text">The number exactly as it should read, already formatted.</param>
    /// <param name="X">Left edge as a fraction of sheet width.</param>
    /// <param name="Y">Top edge as a fraction of sheet height.</param>
    /// <param name="Width">Box width as a fraction of sheet width; drives alignment.</param>
    /// <param name="Height">Box height as a fraction of sheet height.</param>
    /// <param name="FontSize">Em size in 96-dpi design units — the same contract as
    /// <see cref="SlotSpec.FontSize"/>, scaled up by the template's resolution at draw time.</param>
    public record TextOverlay(
        string Text,
        float X,
        float Y,
        float Width,
        float Height,
        string FontFamily,
        float FontSize,
        string ColorHex,
        float Opacity,
        TextAlign Align,
        float Rotation);
}
