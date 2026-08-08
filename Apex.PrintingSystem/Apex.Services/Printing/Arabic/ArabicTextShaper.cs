using System;
using System.Collections.Generic;
using System.Text;

namespace Apex.Services.Printing.Arabic
{
    /// <summary>
    /// Describes how an Arabic character joins with its neighbours.
    /// </summary>
    public enum ArabicJoiningType
    {
        NonJoining,   // Does not join on either side (e.g. space, Latin)
        RightJoining, // Joins only on the right (e.g. Alef, Dal, Waw)
        DualJoining,  // Joins on both sides (most Arabic letters)
        LeftJoining,  // Joins only on the left (rare; some extended block chars)
        Transparent   // Does not break joining context (diacritics / NSM)
    }

    /// <summary>
    /// Represents a single shaped glyph after contextual form resolution.
    /// </summary>
    public class ShapedGlyph
    {
        public char OriginalChar { get; set; }
        public char ShapedChar { get; set; }  // The contextual Unicode presentation form
        public string GlyphForm { get; set; } = ""; // "Isolated","Initial","Medial","Final"
        public bool IsArabic { get; set; }
    }

    /// <summary>
    /// Performs Unicode Arabic contextual shaping (Presentation Forms-B, U+FE70–U+FEFF).
    /// Handles joining type resolution, Lam-Alef ligatures, and diacritic transparency.
    /// </summary>
    public static class ArabicTextShaper
    {
        // ──────────────────────────────────────────────────────────────────────────
        // Joining type table
        // ──────────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<char, ArabicJoiningType> s_joiningTypes
            = BuildJoiningTypeTable();

        private static Dictionary<char, ArabicJoiningType> BuildJoiningTypeTable()
        {
            var t = new Dictionary<char, ArabicJoiningType>();

            // ── Right-Joining letters (join only on right; do not extend to the left) ──
            // Alef and its variants
            t['\u0627'] = ArabicJoiningType.RightJoining; // ا ALEF
            t['\u0622'] = ArabicJoiningType.RightJoining; // آ ALEF WITH MADDA ABOVE
            t['\u0623'] = ArabicJoiningType.RightJoining; // أ ALEF WITH HAMZA ABOVE
            t['\u0625'] = ArabicJoiningType.RightJoining; // إ ALEF WITH HAMZA BELOW
            t['\u0671'] = ArabicJoiningType.RightJoining; // ٱ ALEF WASLA
            t['\u0672'] = ArabicJoiningType.RightJoining; // ٲ
            t['\u0673'] = ArabicJoiningType.RightJoining; // ٳ
            t['\u0675'] = ArabicJoiningType.RightJoining; // ٵ
            // Dal and Thal
            t['\u062F'] = ArabicJoiningType.RightJoining; // د DAL
            t['\u0630'] = ArabicJoiningType.RightJoining; // ذ THAL
            // Reh and Zain
            t['\u0631'] = ArabicJoiningType.RightJoining; // ر REH
            t['\u0632'] = ArabicJoiningType.RightJoining; // ز ZAIN
            // Waw and variants
            t['\u0648'] = ArabicJoiningType.RightJoining; // و WAW
            t['\u0624'] = ArabicJoiningType.RightJoining; // ؤ WAW WITH HAMZA ABOVE
            t['\u06C4'] = ArabicJoiningType.RightJoining; // ۄ WAW WITH RING
            t['\u06C5'] = ArabicJoiningType.RightJoining; // ۅ KIRGHIZ OE
            t['\u06C6'] = ArabicJoiningType.RightJoining; // ۆ OE
            t['\u06C7'] = ArabicJoiningType.RightJoining; // ۇ U
            t['\u06C8'] = ArabicJoiningType.RightJoining; // ۈ YU
            t['\u06C9'] = ArabicJoiningType.RightJoining; // ۉ KIRGHIZ YU
            t['\u06CA'] = ArabicJoiningType.RightJoining; // ۊ
            t['\u06CB'] = ArabicJoiningType.RightJoining; // ۋ VE
            // Extended Reh family
            t['\u0693'] = ArabicJoiningType.RightJoining; // ړ
            t['\u0694'] = ArabicJoiningType.RightJoining; // ڔ
            t['\u0695'] = ArabicJoiningType.RightJoining; // ڕ
            t['\u0696'] = ArabicJoiningType.RightJoining; // ږ
            t['\u0697'] = ArabicJoiningType.RightJoining; // ڗ
            t['\u0698'] = ArabicJoiningType.RightJoining; // ژ JEH
            t['\u0699'] = ArabicJoiningType.RightJoining; // ڙ
            // Extended Dal family
            t['\u068C'] = ArabicJoiningType.RightJoining;
            t['\u068D'] = ArabicJoiningType.RightJoining;
            t['\u068E'] = ArabicJoiningType.RightJoining;
            t['\u068F'] = ArabicJoiningType.RightJoining;
            t['\u0690'] = ArabicJoiningType.RightJoining;
            t['\u0691'] = ArabicJoiningType.RightJoining; // ڑ RREH
            // Hamza standalone
            t['\u0621'] = ArabicJoiningType.NonJoining;   // ء HAMZA (isolated only)
            // Teh Marbuta
            t['\u0629'] = ArabicJoiningType.RightJoining; // ة TEH MARBUTA (final only)
            // Alef Maqsura
            t['\u0649'] = ArabicJoiningType.RightJoining; // ى ALEF MAQSURA — right-joining only
            // Special U+0750–U+077F right-joining subset
            t['\u0759'] = ArabicJoiningType.RightJoining;
            t['\u075A'] = ArabicJoiningType.RightJoining;

            // ── Dual-Joining letters ──
            // Standard Arabic consonants
            t['\u0628'] = ArabicJoiningType.DualJoining; // ب BA
            t['\u062A'] = ArabicJoiningType.DualJoining; // ت TA
            t['\u062B'] = ArabicJoiningType.DualJoining; // ث THA
            t['\u062C'] = ArabicJoiningType.DualJoining; // ج JIM
            t['\u062D'] = ArabicJoiningType.DualJoining; // ح HA
            t['\u062E'] = ArabicJoiningType.DualJoining; // خ KHA
            t['\u0633'] = ArabicJoiningType.DualJoining; // س SIN
            t['\u0634'] = ArabicJoiningType.DualJoining; // ش SHIN
            t['\u0635'] = ArabicJoiningType.DualJoining; // ص SAD
            t['\u0636'] = ArabicJoiningType.DualJoining; // ض DAD
            t['\u0637'] = ArabicJoiningType.DualJoining; // ط TAH
            t['\u0638'] = ArabicJoiningType.DualJoining; // ظ ZAH
            t['\u0639'] = ArabicJoiningType.DualJoining; // ع AIN
            t['\u063A'] = ArabicJoiningType.DualJoining; // غ GHAIN
            t['\u0641'] = ArabicJoiningType.DualJoining; // ف FA
            t['\u0642'] = ArabicJoiningType.DualJoining; // ق QAF
            t['\u0643'] = ArabicJoiningType.DualJoining; // ك KAF
            t['\u0644'] = ArabicJoiningType.DualJoining; // ل LAM
            t['\u0645'] = ArabicJoiningType.DualJoining; // م MIM
            t['\u0646'] = ArabicJoiningType.DualJoining; // ن NUN
            t['\u0647'] = ArabicJoiningType.DualJoining; // ه HEH
            t['\u064A'] = ArabicJoiningType.DualJoining; // ي YEH
            t['\u0626'] = ArabicJoiningType.DualJoining; // ئ YEH WITH HAMZA ABOVE
            // Extended Arabic consonants (U+0600–U+06FF)
            t['\u067E'] = ArabicJoiningType.DualJoining; // پ PEH (Farsi)
            t['\u0686'] = ArabicJoiningType.DualJoining; // چ TCHEH (Farsi)
            t['\u0688'] = ArabicJoiningType.DualJoining; // ڈ DDAL (Urdu)
            t['\u06A9'] = ArabicJoiningType.DualJoining; // ک FARSI KAF
            t['\u06AF'] = ArabicJoiningType.DualJoining; // گ GAF (Farsi)
            t['\u06BA'] = ArabicJoiningType.DualJoining; // ں NOON GHUNNA (Urdu)
            t['\u06BE'] = ArabicJoiningType.DualJoining; // ھ HEH DOACHASHMEE
            t['\u06C1'] = ArabicJoiningType.DualJoining; // ہ HEH GOAL
            t['\u06CC'] = ArabicJoiningType.DualJoining; // ی FARSI YEH
            t['\u06D2'] = ArabicJoiningType.RightJoining; // ے YEH BARREE (right-joining)
            // U+0750–U+077F extended block (dual-joining majority)
            for (char c = '\u0750'; c <= '\u077F'; c++)
            {
                if (!t.ContainsKey(c))
                    t[c] = ArabicJoiningType.DualJoining;
            }

            // ── Transparent (diacritics) ──
            // Harakat U+064B–U+0652
            for (char c = '\u064B'; c <= '\u0652'; c++)
                t[c] = ArabicJoiningType.Transparent;
            t['\u0670'] = ArabicJoiningType.Transparent; // SUPERSCRIPT ALEF
            t['\u0653'] = ArabicJoiningType.Transparent; // MADDAH ABOVE
            t['\u0654'] = ArabicJoiningType.Transparent; // HAMZA ABOVE
            t['\u0655'] = ArabicJoiningType.Transparent; // HAMZA BELOW
            t['\u0656'] = ArabicJoiningType.Transparent;
            t['\u0657'] = ArabicJoiningType.Transparent;
            t['\u0658'] = ArabicJoiningType.Transparent;
            t['\u0659'] = ArabicJoiningType.Transparent;
            t['\u065A'] = ArabicJoiningType.Transparent;
            t['\u065B'] = ArabicJoiningType.Transparent;
            t['\u065C'] = ArabicJoiningType.Transparent;
            t['\u065D'] = ArabicJoiningType.Transparent;
            t['\u065E'] = ArabicJoiningType.Transparent;
            t['\u065F'] = ArabicJoiningType.Transparent;

            return t;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Contextual form tables  (Presentation Forms-B, U+FE70–U+FEFF)
        // Each entry: [Isolated, Initial, Medial, Final]
        // Where a form doesn't exist (e.g. right-joining has no Initial/Medial),
        // the Isolated form is used as a fallback.
        // ──────────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<char, char[]> s_forms
            = BuildContextualForms();

        private static Dictionary<char, char[]> BuildContextualForms()
        {
            // Index: 0=Isolated, 1=Initial, 2=Medial, 3=Final
            var f = new Dictionary<char, char[]>();

            f['\u0621'] = new[] { '\uFE80', '\uFE80', '\uFE80', '\uFE80' }; // ء HAMZA
            f['\u0622'] = new[] { '\uFE81', '\uFE81', '\uFE81', '\uFE82' }; // آ ALEF WITH MADDA
            f['\u0623'] = new[] { '\uFE83', '\uFE83', '\uFE83', '\uFE84' }; // أ ALEF WITH HAMZA ABOVE
            f['\u0624'] = new[] { '\uFE85', '\uFE85', '\uFE85', '\uFE86' }; // ؤ WAW WITH HAMZA
            f['\u0625'] = new[] { '\uFE87', '\uFE87', '\uFE87', '\uFE88' }; // إ ALEF WITH HAMZA BELOW
            f['\u0626'] = new[] { '\uFE89', '\uFE8B', '\uFE8C', '\uFE8A' }; // ئ YEH WITH HAMZA
            f['\u0627'] = new[] { '\uFE8D', '\uFE8D', '\uFE8D', '\uFE8E' }; // ا ALEF
            f['\u0628'] = new[] { '\uFE8F', '\uFE91', '\uFE92', '\uFE90' }; // ب BA
            f['\u0629'] = new[] { '\uFE93', '\uFE93', '\uFE93', '\uFE94' }; // ة TEH MARBUTA
            f['\u062A'] = new[] { '\uFE95', '\uFE97', '\uFE98', '\uFE96' }; // ت TA
            f['\u062B'] = new[] { '\uFE99', '\uFE9B', '\uFE9C', '\uFE9A' }; // ث THA
            f['\u062C'] = new[] { '\uFE9D', '\uFE9F', '\uFEA0', '\uFE9E' }; // ج JIM
            f['\u062D'] = new[] { '\uFEA1', '\uFEA3', '\uFEA4', '\uFEA2' }; // ح HA
            f['\u062E'] = new[] { '\uFEA5', '\uFEA7', '\uFEA8', '\uFEA6' }; // خ KHA
            f['\u062F'] = new[] { '\uFEA9', '\uFEA9', '\uFEA9', '\uFEAA' }; // د DAL
            f['\u0630'] = new[] { '\uFEAB', '\uFEAB', '\uFEAB', '\uFEAC' }; // ذ THAL
            f['\u0631'] = new[] { '\uFEAD', '\uFEAD', '\uFEAD', '\uFEAE' }; // ر REH
            f['\u0632'] = new[] { '\uFEAF', '\uFEAF', '\uFEAF', '\uFEB0' }; // ز ZAIN
            f['\u0633'] = new[] { '\uFEB1', '\uFEB3', '\uFEB4', '\uFEB2' }; // س SIN
            f['\u0634'] = new[] { '\uFEB5', '\uFEB7', '\uFEB8', '\uFEB6' }; // ش SHIN
            f['\u0635'] = new[] { '\uFEB9', '\uFEBB', '\uFEBC', '\uFEBA' }; // ص SAD
            f['\u0636'] = new[] { '\uFEBD', '\uFEBF', '\uFEC0', '\uFEBE' }; // ض DAD
            f['\u0637'] = new[] { '\uFEC1', '\uFEC3', '\uFEC4', '\uFEC2' }; // ط TAH
            f['\u0638'] = new[] { '\uFEC5', '\uFEC7', '\uFEC8', '\uFEC6' }; // ظ ZAH
            f['\u0639'] = new[] { '\uFEC9', '\uFECB', '\uFECC', '\uFECA' }; // ع AIN
            f['\u063A'] = new[] { '\uFECD', '\uFECF', '\uFED0', '\uFECE' }; // غ GHAIN
            f['\u0641'] = new[] { '\uFED1', '\uFED3', '\uFED4', '\uFED2' }; // ف FA
            f['\u0642'] = new[] { '\uFED5', '\uFED7', '\uFED8', '\uFED6' }; // ق QAF
            f['\u0643'] = new[] { '\uFED9', '\uFEDB', '\uFEDC', '\uFEDA' }; // ك KAF
            f['\u0644'] = new[] { '\uFEDD', '\uFEDF', '\uFEE0', '\uFEDE' }; // ل LAM
            f['\u0645'] = new[] { '\uFEE1', '\uFEE3', '\uFEE4', '\uFEE2' }; // م MIM
            f['\u0646'] = new[] { '\uFEE5', '\uFEE7', '\uFEE8', '\uFEE6' }; // ن NUN
            f['\u0647'] = new[] { '\uFEE9', '\uFEEB', '\uFEEC', '\uFEEA' }; // ه HEH
            f['\u0648'] = new[] { '\uFEED', '\uFEED', '\uFEED', '\uFEEE' }; // و WAW
            f['\u0649'] = new[] { '\uFEEF', '\uFEEF', '\uFEEF', '\uFEF0' }; // ى ALEF MAQSURA
            f['\u064A'] = new[] { '\uFEF1', '\uFEF3', '\uFEF4', '\uFEF2' }; // ي YEH

            return f;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Lam-Alef ligature table
        // Key: Alef variant  Value: [isolated-ligature, final-ligature]
        // ──────────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<char, (char Isolated, char Final)> s_lamAlef
            = new Dictionary<char, (char, char)>
            {
                ['\u0622'] = ('\uFEF5', '\uFEF6'), // Lam + Alef with Madda Above
                ['\u0623'] = ('\uFEF7', '\uFEF8'), // Lam + Alef with Hamza Above
                ['\u0625'] = ('\uFEF9', '\uFEFA'), // Lam + Alef with Hamza Below  (U+FEF9/A)
                ['\u0627'] = ('\uFEFB', '\uFEFC'), // Lam + Plain Alef
            };

        // ──────────────────────────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Shapes an Arabic string by replacing each character with its correct
        /// contextual Unicode presentation form.
        /// </summary>
        public static string Shape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var glyphs = ShapeDetailed(text);
            var sb = new StringBuilder(glyphs.Count);
            foreach (var g in glyphs)
                sb.Append(g.ShapedChar);
            return sb.ToString();
        }

        /// <summary>
        /// Returns a per-glyph shaping breakdown including original/shaped characters
        /// and the resolved contextual form name.
        /// </summary>
        public static List<ShapedGlyph> ShapeDetailed(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<ShapedGlyph>();

            // ── Pass 1: Build a flat list of (index, char) skipping nothing,
            //            but tag each position with its joining type.
            int len = text.Length;
            var joinTypes = new ArabicJoiningType[len];
            for (int i = 0; i < len; i++)
                joinTypes[i] = GetJoiningType(text[i]);

            // ── Pass 2: For each position compute form, handle Lam+Alef ligatures.
            var result = new List<ShapedGlyph>(len);
            bool[] consumed = new bool[len]; // marks positions absorbed into ligatures

            for (int i = 0; i < len; i++)
            {
                if (consumed[i])
                    continue;

                char c = text[i];

                if (!IsArabicChar(c) && joinTypes[i] != ArabicJoiningType.Transparent)
                {
                    result.Add(new ShapedGlyph
                    {
                        OriginalChar = c,
                        ShapedChar = c,
                        GlyphForm = "",
                        IsArabic = false
                    });
                    continue;
                }

                // Transparent (diacritics) pass through unchanged
                if (joinTypes[i] == ArabicJoiningType.Transparent)
                {
                    result.Add(new ShapedGlyph
                    {
                        OriginalChar = c,
                        ShapedChar = c,
                        GlyphForm = "Diacritic",
                        IsArabic = true
                    });
                    continue;
                }

                // Determine effective previous and next non-transparent joining type
                ArabicJoiningType prevType = GetEffectivePrevType(text, joinTypes, i);
                bool canConnectRight = CanConnectRight(prevType); // connects to right of this char

                // ── Check Lam+Alef ligature ──
                if (c == '\u0644') // LAM
                {
                    int nextIdx = FindNextNonTransparent(text, joinTypes, i);
                    if (nextIdx != -1 && s_lamAlef.ContainsKey(text[nextIdx]))
                    {
                        char alef = text[nextIdx];
                        var (iso, fin) = s_lamAlef[alef];
                        char ligature = canConnectRight ? fin : iso;
                        string form = canConnectRight ? "Final" : "Isolated";

                        result.Add(new ShapedGlyph
                        {
                            OriginalChar = c,
                            ShapedChar = ligature,
                            GlyphForm = form,
                            IsArabic = true
                        });
                        // Mark intervening diacritics as pass-through then skip alef
                        for (int k = i + 1; k < nextIdx; k++)
                        {
                            consumed[k] = true;
                            result.Add(new ShapedGlyph
                            {
                                OriginalChar = text[k],
                                ShapedChar = text[k],
                                GlyphForm = "Diacritic",
                                IsArabic = true
                            });
                        }
                        consumed[nextIdx] = true;
                        continue;
                    }
                }

                // Determine effective next non-transparent joining type
                ArabicJoiningType nextType = GetEffectiveNextType(text, joinTypes, i);
                bool canConnectLeft = CanConnectLeft(nextType); // connects to left of this char

                string formName = ResolveFormName(joinTypes[i], canConnectRight, canConnectLeft);
                char shaped = ApplyForm(c, formName);

                result.Add(new ShapedGlyph
                {
                    OriginalChar = c,
                    ShapedChar = shaped,
                    GlyphForm = formName,
                    IsArabic = true
                });
            }

            return result;
        }

        /// <summary>
        /// Returns true if <paramref name="c"/> falls in the Arabic Unicode blocks.
        /// </summary>
        public static bool IsArabicChar(char c)
        {
            return (c >= '\u0600' && c <= '\u06FF')
                || (c >= '\u0750' && c <= '\u077F')
                || (c >= '\u08A0' && c <= '\u08FF')
                || (c >= '\uFB50' && c <= '\uFDFF') // Arabic Presentation Forms-A
                || (c >= '\uFE70' && c <= '\uFEFF'); // Arabic Presentation Forms-B
        }

        /// <summary>
        /// Returns true if <paramref name="c"/> is an Arabic diacritic (Harakat).
        /// U+064B–U+0652 (Fathah, Kasra, Damma, …) plus U+0670 (Superscript Alef).
        /// </summary>
        public static bool IsArabicDiacritic(char c)
        {
            return (c >= '\u064B' && c <= '\u065F') || c == '\u0670';
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Internal helpers
        // ──────────────────────────────────────────────────────────────────────────

        private static ArabicJoiningType GetJoiningType(char c)
        {
            if (s_joiningTypes.TryGetValue(c, out var jt))
                return jt;

            // Arabic block default: dual-joining (most letters)
            if ((c >= '\u0600' && c <= '\u06FF') || (c >= '\u0750' && c <= '\u077F'))
                return ArabicJoiningType.DualJoining;

            return ArabicJoiningType.NonJoining;
        }

        // Scan backwards from position i (exclusive), skipping Transparent chars,
        // and return the joining type of the first non-transparent character.
        private static ArabicJoiningType GetEffectivePrevType(
            string text, ArabicJoiningType[] types, int i)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                if (types[k] != ArabicJoiningType.Transparent)
                    return types[k];
            }
            return ArabicJoiningType.NonJoining;
        }

        // Scan forwards from position i (exclusive), skipping Transparent chars,
        // and return the joining type of the first non-transparent character.
        private static ArabicJoiningType GetEffectiveNextType(
            string text, ArabicJoiningType[] types, int i)
        {
            for (int k = i + 1; k < text.Length; k++)
            {
                if (types[k] != ArabicJoiningType.Transparent)
                    return types[k];
            }
            return ArabicJoiningType.NonJoining;
        }

        // Returns the index of the first non-transparent character strictly after i,
        // or -1 if none found.
        private static int FindNextNonTransparent(
            string text, ArabicJoiningType[] types, int i)
        {
            for (int k = i + 1; k < text.Length; k++)
            {
                if (types[k] != ArabicJoiningType.Transparent)
                    return k;
            }
            return -1;
        }

        // A character can connect to its right if its left neighbour is
        // DualJoining or LeftJoining (in logical text, the previous char is to the right visually).
        private static bool CanConnectRight(ArabicJoiningType prevType)
        {
            return prevType == ArabicJoiningType.DualJoining
                || prevType == ArabicJoiningType.LeftJoining;
        }

        // A character can connect to its left if its right neighbour is
        // DualJoining or RightJoining.
        private static bool CanConnectLeft(ArabicJoiningType nextType)
        {
            return nextType == ArabicJoiningType.DualJoining
                || nextType == ArabicJoiningType.RightJoining;
        }

        private static string ResolveFormName(
            ArabicJoiningType selfType, bool connectRight, bool connectLeft)
        {
            if (selfType == ArabicJoiningType.NonJoining || selfType == ArabicJoiningType.Transparent)
                return "Isolated";

            if (selfType == ArabicJoiningType.RightJoining)
            {
                // Right-joining: can only accept a connection from the right (prev char).
                // connectLeft is irrelevant (they won't connect to the left).
                return connectRight ? "Final" : "Isolated";
            }

            // DualJoining (or LeftJoining)
            if (connectRight && connectLeft) return "Medial";
            if (!connectRight && connectLeft) return "Initial";
            if (connectRight && !connectLeft) return "Final";
            return "Isolated";
        }

        private static char ApplyForm(char c, string form)
        {
            if (!s_forms.TryGetValue(c, out var forms))
                return c; // No presentation form mapping; return as-is

            return form switch
            {
                "Initial" => forms[1],
                "Medial" => forms[2],
                "Final" => forms[3],
                _ => forms[0]  // Isolated (also used as fallback)
            };
        }
    }
}
