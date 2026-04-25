using System;
using System.Collections.Generic;
using System.Text;

namespace Apex.Services.Printing.Arabic
{
    /// <summary>
    /// Represents a position in shaped Arabic text where a Kashida (U+0640)
    /// may be inserted for justification.
    /// </summary>
    public class KashidaInsertionPoint
    {
        /// <summary>Index in the original string after which to insert the kashida.</summary>
        public int  Position   { get; set; }
        /// <summary>The character immediately before the insertion point.</summary>
        public char BeforeChar { get; set; }
        /// <summary>The character immediately after the insertion point.</summary>
        public char AfterChar  { get; set; }
        /// <summary>
        /// Priority 1 = highest (adjacent to Shadda/Harakat),
        /// Priority 5 = lowest (generic medial connection).
        /// </summary>
        public int  Priority   { get; set; }
        /// <summary>True when the insertion is visually valid at this position.</summary>
        public bool CanStretch { get; set; }
    }

    /// <summary>
    /// Implements Arabic Kashida (U+0640) justification.
    /// Kashida is the Unicode tatweel character used to stretch connecting strokes
    /// between Arabic letters, providing a visually natural way to justify lines.
    /// </summary>
    public static class KashidaJustifier
    {
        // Unicode Kashida (Tatweel) character
        private const char Kashida = '\u0640';

        // ── Shadda and Harakat range ────────────────────────────────────────────
        private const char Shadda = '\u0651';

        // ── Presentation Forms-B ranges ─────────────────────────────────────────
        // Initial forms: connect on the left (start of word)
        // Medial forms:  connect on both sides
        // Final forms:   connect on the right (end of word run)
        // Isolated forms: no connections

        // Medial and Initial form ranges within U+FE70–U+FEFF
        // Each letter's forms follow the pattern:
        //   Isolated(+0), Final(+1), Initial(+2), Medial(+3)  — for many letters
        // We explicitly list the known initial/medial chars for accuracy.

        private static readonly HashSet<char> s_initialForms = BuildInitialForms();
        private static readonly HashSet<char> s_medialForms  = BuildMedialForms();
        private static readonly HashSet<char> s_finalForms   = BuildFinalForms();

        // Letters that have high-priority stretching (Ba, Ta, Tha, Nun, Yeh family)
        // represented by their base Unicode code points
        private static readonly HashSet<char> s_highPriorityBases = new HashSet<char>
        {
            '\u0628', // ب BA
            '\u062A', // ت TA
            '\u062B', // ث THA
            '\u0646', // ن NUN
            '\u064A', // ي YEH
            '\u0626', // ئ YEH WITH HAMZA
            '\u06CC', // ی FARSI YEH
        };

        // ──────────────────────────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds all valid Kashida insertion points in <paramref name="shapedText"/>.
        /// The text should have already been through <see cref="ArabicTextShaper.Shape"/>.
        /// </summary>
        public static List<KashidaInsertionPoint> FindInsertionPoints(string shapedText)
        {
            if (string.IsNullOrEmpty(shapedText))
                return new List<KashidaInsertionPoint>();

            var points = new List<KashidaInsertionPoint>();

            for (int i = 0; i < shapedText.Length - 1; i++)
            {
                char before = shapedText[i];
                char after  = shapedText[i + 1];

                int  priority   = GetJustificationPriority(before, after);
                bool canStretch = AcceptsKashidaRight(before) && AcceptsKashidaLeft(after);

                if (!canStretch)
                    continue;

                // Additional validation: kashida should not be inserted between
                // a letter and its diacritic
                if (ArabicTextShaper.IsArabicDiacritic(after))
                    continue;

                points.Add(new KashidaInsertionPoint
                {
                    Position   = i,
                    BeforeChar = before,
                    AfterChar  = after,
                    Priority   = priority,
                    CanStretch = true
                });
            }

            // Sort by priority (ascending = highest priority first), then by position
            points.Sort((a, b) =>
            {
                int cmp = a.Priority.CompareTo(b.Priority);
                return cmp != 0 ? cmp : a.Position.CompareTo(b.Position);
            });

            return points;
        }

        /// <summary>
        /// Inserts Kashida characters into <paramref name="shapedText"/> to justify
        /// a line to <paramref name="targetCharWidth"/> characters.
        /// </summary>
        /// <param name="shapedText">Arabic text already shaped via ArabicTextShaper.</param>
        /// <param name="targetCharWidth">Desired line width in character units.</param>
        /// <param name="currentCharWidth">Actual current line width in character units.</param>
        /// <returns>
        /// The text with Kashida characters inserted at the highest-priority positions.
        /// If no valid insertion points exist or no stretching is needed, returns the
        /// original text unchanged.
        /// </returns>
        public static string JustifyLine(
            string shapedText, int targetCharWidth, int currentCharWidth)
        {
            if (string.IsNullOrEmpty(shapedText))
                return shapedText;

            int needed = targetCharWidth - currentCharWidth;
            if (needed <= 0)
                return shapedText;

            var insertionPoints = FindInsertionPoints(shapedText);
            if (insertionPoints.Count == 0)
                return shapedText;

            // Distribute kashidas across insertion points, cycling through them
            // from highest priority to lowest until the quota is filled.
            // Build a map: position → number of kashidas to insert.
            var insertCounts = new Dictionary<int, int>();
            int distributed  = 0;
            int cycleIndex   = 0;

            // Limit to avoid degenerate cases where a single position gets all kashidas
            int maxPerPosition = Math.Max(1, (needed / insertionPoints.Count) + 1);

            while (distributed < needed)
            {
                var point = insertionPoints[cycleIndex % insertionPoints.Count];
                int pos   = point.Position;

                insertCounts.TryGetValue(pos, out int existing);
                if (existing < maxPerPosition || insertionPoints.Count == 1)
                {
                    insertCounts[pos] = existing + 1;
                    distributed++;
                }

                cycleIndex++;
                // Safety: if we've cycled through all points multiple times with no progress, break
                if (cycleIndex > needed * insertionPoints.Count + needed)
                    break;
            }

            // Build the output string, inserting kashidas after each flagged position.
            // Positions are in terms of the *original* shaped text, so we iterate
            // forward and adjust for the growing offset.
            var sb     = new StringBuilder(shapedText.Length + distributed);
            int offset = 0;

            for (int i = 0; i < shapedText.Length; i++)
            {
                sb.Append(shapedText[i]);
                if (insertCounts.TryGetValue(i, out int count))
                {
                    for (int k = 0; k < count; k++)
                        sb.Append(Kashida);
                    offset += count;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="c"/> has an open connecting
        /// stroke on its right side (i.e., it is an Initial or Medial presentation form).
        /// </summary>
        public static bool AcceptsKashidaRight(char c)
        {
            // Tatweel itself always accepts on both sides
            if (c == Kashida) return true;

            // Presentation forms
            if (s_initialForms.Contains(c) || s_medialForms.Contains(c))
                return true;

            // Base Arabic letters (dual-joining) also accept kashida
            if (c >= '\u0620' && c <= '\u064A')
            {
                if (ArabicTextShaper.IsArabicChar(c) && !ArabicTextShaper.IsArabicDiacritic(c))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="c"/> accepts a kashida
        /// connection on its left side (i.e., it is a Medial or Final presentation form).
        /// </summary>
        public static bool AcceptsKashidaLeft(char c)
        {
            if (c == Kashida) return true;

            if (s_medialForms.Contains(c) || s_finalForms.Contains(c))
                return true;

            // Base Arabic dual-joining letters
            if (c >= '\u0620' && c <= '\u064A')
            {
                if (ArabicTextShaper.IsArabicChar(c) && !ArabicTextShaper.IsArabicDiacritic(c))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the justification priority for the connection between
        /// <paramref name="leftChar"/> and <paramref name="rightChar"/>.
        /// Lower values indicate higher priority for kashida insertion.
        /// Returns <see cref="int.MaxValue"/> when the pair is not a valid kashida site.
        /// </summary>
        public static int GetJustificationPriority(char leftChar, char rightChar)
        {
            // Priority 1: adjacent to Shadda or other harakat
            if (leftChar == Shadda || rightChar == Shadda)
                return 1;
            if (ArabicTextShaper.IsArabicDiacritic(leftChar)
             || ArabicTextShaper.IsArabicDiacritic(rightChar))
                return 1;

            // Must be a valid kashida site
            if (!AcceptsKashidaRight(leftChar) || !AcceptsKashidaLeft(rightChar))
                return int.MaxValue;

            // Priority 2: after initial/medial forms of Ba, Ta, Tha, Nun, Yeh family
            char leftBase = GetBaseChar(leftChar);
            if (s_highPriorityBases.Contains(leftBase)
             && (s_initialForms.Contains(leftChar) || s_medialForms.Contains(leftChar)))
                return 2;

            // Priority 3: after medial forms of other dual-joining connecting letters
            if (s_medialForms.Contains(leftChar))
                return 3;

            // Priority 4: before final forms (the receiving stroke of a final letter)
            if (s_finalForms.Contains(rightChar))
                return 4;

            // Priority 5: any medial connection (fallback for initial-like connections)
            if (s_initialForms.Contains(leftChar))
                return 5;

            return int.MaxValue;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Form set builders
        // ──────────────────────────────────────────────────────────────────────────

        private static HashSet<char> BuildInitialForms()
        {
            // Initial presentation forms (connect to the left only)
            return new HashSet<char>
            {
                '\uFE8B', // ئ Initial
                '\uFE91', // ب Initial
                '\uFE97', // ت Initial
                '\uFE9B', // ث Initial
                '\uFE9F', // ج Initial
                '\uFEA3', // ح Initial
                '\uFEA7', // خ Initial
                '\uFEB3', // س Initial
                '\uFEB7', // ش Initial
                '\uFEBB', // ص Initial
                '\uFEBF', // ض Initial
                '\uFEC3', // ط Initial
                '\uFEC7', // ظ Initial
                '\uFECB', // ع Initial
                '\uFECF', // غ Initial
                '\uFED3', // ف Initial
                '\uFED7', // ق Initial
                '\uFEDB', // ك Initial
                '\uFEDF', // ل Initial
                '\uFEE3', // م Initial
                '\uFEE7', // ن Initial
                '\uFEEB', // ه Initial
                '\uFEF3', // ي Initial
            };
        }

        private static HashSet<char> BuildMedialForms()
        {
            // Medial presentation forms (connect on both sides)
            return new HashSet<char>
            {
                '\uFE8C', // ئ Medial
                '\uFE92', // ب Medial
                '\uFE98', // ت Medial
                '\uFE9C', // ث Medial
                '\uFEA0', // ج Medial
                '\uFEA4', // ح Medial
                '\uFEA8', // خ Medial
                '\uFEB4', // س Medial
                '\uFEB8', // ش Medial
                '\uFEBC', // ص Medial
                '\uFEC0', // ض Medial
                '\uFEC4', // ط Medial
                '\uFEC8', // ظ Medial
                '\uFECC', // ع Medial
                '\uFED0', // غ Medial
                '\uFED4', // ف Medial
                '\uFED8', // ق Medial
                '\uFEDC', // ك Medial
                '\uFEE0', // ل Medial
                '\uFEE4', // م Medial
                '\uFEE8', // ن Medial
                '\uFEEC', // ه Medial
                '\uFEF4', // ي Medial
            };
        }

        private static HashSet<char> BuildFinalForms()
        {
            // Final presentation forms (connect to the right only)
            return new HashSet<char>
            {
                '\uFE80', // ء (isolated only, but treat as final-capable)
                '\uFE82', // آ Final
                '\uFE84', // أ Final
                '\uFE86', // ؤ Final
                '\uFE88', // إ Final
                '\uFE8A', // ئ Final
                '\uFE8E', // ا Final
                '\uFE90', // ب Final
                '\uFE94', // ة Final
                '\uFE96', // ت Final
                '\uFE9A', // ث Final
                '\uFE9E', // ج Final
                '\uFEA2', // ح Final
                '\uFEA6', // خ Final
                '\uFEAA', // د Final
                '\uFEAC', // ذ Final
                '\uFEAE', // ر Final
                '\uFEB0', // ز Final
                '\uFEB2', // س Final
                '\uFEB6', // ش Final
                '\uFEBA', // ص Final
                '\uFEBE', // ض Final
                '\uFEC2', // ط Final
                '\uFEC6', // ظ Final
                '\uFECA', // ع Final
                '\uFECE', // غ Final
                '\uFED2', // ف Final
                '\uFED6', // ق Final
                '\uFEDA', // ك Final
                '\uFEDE', // ل Final
                '\uFEE2', // م Final
                '\uFEE6', // ن Final
                '\uFEEA', // ه Final
                '\uFEEE', // و Final
                '\uFEF0', // ى Final
                '\uFEF2', // ي Final
            };
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Presentation Form → Base character mapping
        // ──────────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<char, char> s_presentationToBase
            = BuildPresentationToBase();

        private static Dictionary<char, char> BuildPresentationToBase()
        {
            var map = new Dictionary<char, char>();
            // Covers the letters listed in the form sets above
            char[] bases = {
                '\u0626','\u0628','\u062A','\u062B','\u062C','\u062D','\u062E',
                '\u0633','\u0634','\u0635','\u0636','\u0637','\u0638','\u0639',
                '\u063A','\u0641','\u0642','\u0643','\u0644','\u0645','\u0646',
                '\u0647','\u064A'
            };
            // Map each presentation form back to its base
            // The ArabicTextShaper s_forms dictionary encodes the mapping in the other direction.
            // We rebuild the reverse here.
            var formsField = typeof(ArabicTextShaper)
                .GetField("s_forms",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

            if (formsField?.GetValue(null) is Dictionary<char, char[]> formsDict)
            {
                foreach (var kvp in formsDict)
                {
                    char baseChar  = kvp.Key;
                    char[] forms   = kvp.Value;
                    // forms[0]=Isolated, [1]=Initial, [2]=Medial, [3]=Final
                    foreach (char f in forms)
                    {
                        if (f != baseChar && !map.ContainsKey(f))
                            map[f] = baseChar;
                    }
                }
            }
            else
            {
                // Fallback: hardcode the most common ones
                // Ba family
                map['\uFE8F'] = '\u0628'; map['\uFE91'] = '\u0628';
                map['\uFE92'] = '\u0628'; map['\uFE90'] = '\u0628';
                // Ta
                map['\uFE95'] = '\u062A'; map['\uFE97'] = '\u062A';
                map['\uFE98'] = '\u062A'; map['\uFE96'] = '\u062A';
                // Tha
                map['\uFE99'] = '\u062B'; map['\uFE9B'] = '\u062B';
                map['\uFE9C'] = '\u062B'; map['\uFE9A'] = '\u062B';
                // Nun
                map['\uFEE5'] = '\u0646'; map['\uFEE7'] = '\u0646';
                map['\uFEE8'] = '\u0646'; map['\uFEE6'] = '\u0646';
                // Yeh
                map['\uFEF1'] = '\u064A'; map['\uFEF3'] = '\u064A';
                map['\uFEF4'] = '\u064A'; map['\uFEF2'] = '\u064A';
            }

            return map;
        }

        private static char GetBaseChar(char presentationForm)
        {
            return s_presentationToBase.TryGetValue(presentationForm, out char b) ? b : presentationForm;
        }
    }
}
