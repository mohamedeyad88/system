using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Apex.Services.Printing.Arabic
{
    /// <summary>
    /// Metadata about a font and its Arabic-language capabilities.
    /// </summary>
    public class ArabicFontInfo
    {
        public string FamilyName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public bool IsAvailable { get; set; }
        public bool SupportsArabic { get; set; }
        public bool SupportsHarakat { get; set; }  // Tashkeel / diacritics
        public bool SupportsPresentationForms { get; set; } // FE70–FEFF block
        public int Priority { get; set; }   // Lower = higher priority
    }

    /// <summary>
    /// Discovers Arabic fonts on the system and provides an ordered fallback chain
    /// for use in GDI+, WPF, and direct print (PCL / PostScript) scenarios.
    /// </summary>
    public class ArabicFontFallbackChain
    {
        // ── Singleton ────────────────────────────────────────────────────────────
        private static readonly Lazy<ArabicFontFallbackChain> s_instance =
            new Lazy<ArabicFontFallbackChain>(() => new ArabicFontFallbackChain());

        public static ArabicFontFallbackChain Instance => s_instance.Value;

        // ── State ─────────────────────────────────────────────────────────────────
        private List<ArabicFontInfo> _chain = new List<ArabicFontInfo>();
        private readonly object _lock = new object();

        // ── Known font definitions ─────────────────────────────────────────────────

        /// <summary>
        /// Well-known Arabic font descriptors keyed by lowercase filename (without extension).
        /// Priority: lower = preferred.
        /// </summary>
        private static readonly Dictionary<string, KnownFontSpec> s_knownFonts
            = new Dictionary<string, KnownFontSpec>(StringComparer.OrdinalIgnoreCase)
            {
                // filename (no ext)          family name                P  Arabic Harakat PForm
                ["tradbdo"] = new("Traditional Arabic", 1, true, false, true),
                ["trado"] = new("Traditional Arabic", 1, true, false, true),
                ["arabtype"] = new("Arabic Typesetting", 2, true, false, true),
                ["aldhabi"] = new("Aldhabi", 3, true, true, true),
                ["alfirat"] = new("Al Furat", 4, true, true, true),
                ["sakkal"] = new("Sakkal Majalla", 5, true, true, true),
                ["majalla"] = new("Sakkal Majalla", 5, true, true, true),
                ["majallabold"] = new("Sakkal Majalla Bold", 5, true, true, true),
                ["segoeui"] = new("Segoe UI", 6, true, false, true),
                ["segoeuib"] = new("Segoe UI Bold", 6, true, false, true),
                ["tahoma"] = new("Tahoma", 7, true, false, true),
                ["tahomabd"] = new("Tahoma Bold", 7, true, false, true),
                ["calibri"] = new("Calibri", 8, true, false, true),
                ["calibrib"] = new("Calibri Bold", 8, true, false, true),
                ["arial"] = new("Arial", 9, true, false, true),
                ["arialbd"] = new("Arial Bold", 9, true, false, true),
                ["arialuni"] = new("Arial Unicode MS", 9, true, true, true),
                ["times"] = new("Times New Roman", 10, true, false, false),
                ["timesbd"] = new("Times New Roman Bold", 10, true, false, false),
            };

        private record KnownFontSpec(
            string FamilyName,
            int Priority,
            bool Arabic,
            bool Harakat,
            bool PresentationForms);

        // ── Construction ─────────────────────────────────────────────────────────

        private ArabicFontFallbackChain()
        {
            Refresh();
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the family name of the best available Arabic font.
        /// </summary>
        /// <param name="needsHarakat">
        ///   When <c>true</c>, prefer fonts that support diacritical marks (Tashkeel).
        /// </param>
        /// <param name="needsNaskh">
        ///   When <c>true</c>, prefer Naskh-style fonts (Traditional Arabic, Tahoma, etc.)
        ///   over decorative or calligraphic faces.
        /// </param>
        public string GetBestFont(bool needsHarakat = false, bool needsNaskh = true)
        {
            lock (_lock)
            {
                IEnumerable<ArabicFontInfo> candidates = _chain.Where(f => f.IsAvailable);

                if (needsHarakat)
                    candidates = candidates.Where(f => f.SupportsHarakat);

                var best = candidates.OrderBy(f => f.Priority).FirstOrDefault();
                return best?.FamilyName ?? "Arial";
            }
        }

        /// <summary>
        /// Returns the full ordered fallback chain, from highest to lowest priority.
        /// </summary>
        public IReadOnlyList<ArabicFontInfo> GetFallbackChain()
        {
            lock (_lock)
            {
                return _chain.AsReadOnly();
            }
        }

        /// <summary>
        /// Checks whether the named font contains a glyph for <paramref name="sampleChar"/>
        /// by probing the font via GDI on Windows.
        /// Returns <c>false</c> on non-Windows platforms or if the font is not found.
        /// </summary>
        public bool FontSupportsRange(string fontFamily, char sampleChar)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                using var font = new System.Drawing.Font(fontFamily, 12f);
                // If the system substituted a fallback, the family name won't match
                if (!string.Equals(font.FontFamily.Name, fontFamily,
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                // Use GDI GetGlyphIndices to test presence
                using var bmp = new System.Drawing.Bitmap(1, 1);
                using var g = System.Drawing.Graphics.FromImage(bmp);
                IntPtr hdc = g.GetHdc();
                IntPtr hFont = font.ToHfont();
                IntPtr prev = SelectObject(hdc, hFont);
                try
                {
                    ushort[] indices = new ushort[1];
                    GetGlyphIndicesW(hdc, sampleChar.ToString(), 1, indices,
                                     GGI_MARK_NONEXISTING_GLYPHS);
                    return indices[0] != 0xFFFF;
                }
                finally
                {
                    SelectObject(hdc, prev);
                    DeleteObject(hFont);
                    g.ReleaseHdc(hdc);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the preferred font family name for a specific printer.
        /// PostScript printers prefer fonts with Type 1 equivalents.
        /// PCL printers prefer fonts with HP soft-font equivalents.
        /// </summary>
        public string GetPrintFont(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                return GetBestFont();

            string lower = printerName.ToLowerInvariant();

            // PostScript printers
            if (lower.Contains("postscript") || lower.Contains("ps)") || lower.EndsWith(" ps")
             || lower.Contains("laserwriter") || lower.Contains("xerox")
             || lower.Contains("ghostscript") || lower.Contains("pdf"))
            {
                // "Nadeem" is a common PostScript Arabic font; fallback to Tahoma
                string nadeem = GetFontIfAvailable("Nadeem");
                if (!string.IsNullOrEmpty(nadeem)) return nadeem;

                string tahoma = GetFontIfAvailable("Tahoma");
                if (!string.IsNullOrEmpty(tahoma)) return tahoma;
            }

            // PCL printers (HP LaserJet, etc.)
            if (lower.Contains("pcl") || lower.Contains("hp laserjet")
             || lower.Contains("laserjet") || lower.Contains("hp color"))
            {
                // HP Arabic fonts shipped with some LaserJets
                string nadeem = GetFontIfAvailable("Nadeem");
                if (!string.IsNullOrEmpty(nadeem)) return nadeem;

                string traditional = GetFontIfAvailable("Traditional Arabic");
                if (!string.IsNullOrEmpty(traditional)) return traditional;
            }

            return GetBestFont();
        }

        /// <summary>
        /// Rescans the system font directory and refreshes the internal font chain.
        /// </summary>
        public void Refresh()
        {
            var discovered = DiscoverFonts();
            lock (_lock)
            {
                _chain = discovered
                    .OrderBy(f => f.Priority)
                    .ThenBy(f => f.FamilyName)
                    .ToList();
            }
        }

        /// <summary>
        /// Creates a <see cref="System.Drawing.Font"/> using the best available Arabic
        /// font for GDI+ rendering.
        /// </summary>
        public System.Drawing.Font CreateArabicFont(
            float size,
            System.Drawing.FontStyle style = System.Drawing.FontStyle.Regular)
        {
            // Walk the fallback chain until a font can be created successfully
            lock (_lock)
            {
                foreach (var info in _chain)
                {
                    if (!info.IsAvailable)
                        continue;
                    try
                    {
                        var font = new System.Drawing.Font(info.FamilyName, size, style,
                                       System.Drawing.GraphicsUnit.Point);
                        // Verify the system didn't silently substitute a completely
                        // different family
                        if (string.Equals(font.FontFamily.Name, info.FamilyName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return font;
                        }
                        font.Dispose();
                    }
                    catch
                    {
                        // Try next in chain
                    }
                }
            }

            // Absolute fallback — should virtually never happen on a normal Windows system
            return new System.Drawing.Font("Arial", size, style,
                       System.Drawing.GraphicsUnit.Point);
        }

        // ── Font discovery ────────────────────────────────────────────────────────

        private static List<ArabicFontInfo> DiscoverFonts()
        {
            var result = new List<ArabicFontInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string fontsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

            if (!Directory.Exists(fontsDir))
                return result;

            var fontFiles = Directory.EnumerateFiles(fontsDir, "*.ttf")
                .Concat(Directory.EnumerateFiles(fontsDir, "*.otf"));

            foreach (string filePath in fontFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string lowerName = fileName.ToLowerInvariant();

                // Check against known list first
                if (s_knownFonts.TryGetValue(lowerName, out var spec))
                {
                    string key = spec.FamilyName;
                    if (seen.Add(key))
                    {
                        result.Add(new ArabicFontInfo
                        {
                            FamilyName = spec.FamilyName,
                            FilePath = filePath,
                            IsAvailable = true,
                            SupportsArabic = spec.Arabic,
                            SupportsHarakat = spec.Harakat,
                            SupportsPresentationForms = spec.PresentationForms,
                            Priority = spec.Priority
                        });
                    }
                    continue;
                }

                // Catch-all: any font whose filename contains "arab"
                if (lowerName.Contains("arab"))
                {
                    string familyName = InferFamilyName(fileName);
                    if (seen.Add(familyName))
                    {
                        result.Add(new ArabicFontInfo
                        {
                            FamilyName = familyName,
                            FilePath = filePath,
                            IsAvailable = true,
                            SupportsArabic = true,
                            SupportsHarakat = false,
                            SupportsPresentationForms = true,
                            Priority = 20 // low priority for unknown fonts
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Converts a raw font filename into a human-readable family name by
        /// capitalising each word component and stripping common suffixes.
        /// </summary>
        private static string InferFamilyName(string fileName)
        {
            // Remove trailing Bold/Italic/Bd/It suffixes
            string stripped = fileName
                .Replace("Bd", " Bold", StringComparison.OrdinalIgnoreCase)
                .Replace("It", " Italic", StringComparison.OrdinalIgnoreCase)
                .Replace("-Bold", " Bold", StringComparison.OrdinalIgnoreCase)
                .Replace("-Italic", " Italic", StringComparison.OrdinalIgnoreCase)
                .Trim();

            // Insert spaces before uppercase letters (PascalCase → words)
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < stripped.Length; i++)
            {
                if (i > 0 && char.IsUpper(stripped[i]) && !char.IsUpper(stripped[i - 1]))
                    sb.Append(' ');
                sb.Append(stripped[i]);
            }
            return sb.ToString().Trim();
        }

        private string GetFontIfAvailable(string familyName)
        {
            lock (_lock)
            {
                var found = _chain.FirstOrDefault(f =>
                    f.IsAvailable &&
                    string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));
                return found?.FamilyName ?? string.Empty;
            }
        }

        // ── P/Invoke (Windows GDI) ────────────────────────────────────────────────

        private const uint GGI_MARK_NONEXISTING_GLYPHS = 0x0001;

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetGlyphIndicesW(
            IntPtr hdc, string lpstr, int c,
            [Out] ushort[] pgi, uint fl);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr ho);
    }
}
