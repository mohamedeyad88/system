using PdfSharpCore.Fonts;
using System.IO;

namespace Apex.Services.Imposition
{
    /// <summary>
    /// Registers a system font resolver for PdfSharpCore once, process-wide. Without a
    /// resolver, any PdfSharpCore text draw (imposition job-info/crop-mark labels, sample
    /// documents) silently fails. Idempotent and thread-safe.
    /// </summary>
    public static class PdfFonts
    {
        private static bool _set;
        private static readonly object Gate = new();

        public static void EnsureResolver()
        {
            if (_set || GlobalFontSettings.FontResolver != null) return;
            lock (Gate)
            {
                if (_set || GlobalFontSettings.FontResolver != null) return;
                GlobalFontSettings.FontResolver = new SystemArialResolver();
                _set = true;
            }
        }

        private sealed class SystemArialResolver : IFontResolver
        {
            public string DefaultFontName => "Arial";

            private static readonly string[] Candidates =
            {
                @"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\segoeui.ttf", @"C:\Windows\Fonts\tahoma.ttf"
            };

            public byte[] GetFont(string faceName)
            {
                foreach (var p in Candidates)
                    if (File.Exists(p)) return File.ReadAllBytes(p);
                throw new FileNotFoundException("لم يُعثر على خط نظام لعرض النص.");
            }

            public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
                => new FontResolverInfo("Arial");
        }
    }
}
