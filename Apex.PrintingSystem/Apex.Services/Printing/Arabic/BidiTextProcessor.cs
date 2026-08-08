using System;
using System.Collections.Generic;
using System.Text;

namespace Apex.Services.Printing.Arabic
{
    /// <summary>
    /// Unicode Bidirectional character types per UAX#9.
    /// </summary>
    public enum BiDiCharType
    {
        L,   // Strong Left-to-Right (Latin, Greek, Cyrillic, …)
        R,   // Strong Right-to-Left (Hebrew, …)
        AL,  // Arabic Letter — treated as RTL strong
        EN,  // European Number (0-9, extended digits)
        AN,  // Arabic Number (U+0660–U+0669 Eastern Arabic-Indic)
        WS,  // Whitespace
        ON,  // Other Neutral (punctuation, symbols)
        NSM, // Non-Spacing Mark (combining diacritics)
        B,   // Paragraph Separator (U+000A, U+000D, U+2029)
        S,   // Segment Separator (U+0009)
        BN   // Boundary Neutral (formatting, control chars)
    }

    /// <summary>
    /// A contiguous run of text sharing the same directionality.
    /// </summary>
    public class BidiRun
    {
        public string Text { get; set; } = "";
        public bool IsRtl { get; set; }
        public int Level { get; set; }
        public int StartIndex { get; set; }
        public int Length { get; set; }
    }

    /// <summary>
    /// Simplified Unicode Bidirectional Algorithm (UAX#9) implementation.
    /// Handles Arabic, Hebrew, Latin, and mixed-direction text for printing.
    /// </summary>
    public static class BidiTextProcessor
    {
        // ──────────────────────────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans <paramref name="text"/> for the first strong directional character
        /// (L, R, or AL) and returns <c>true</c> when the paragraph direction is RTL.
        /// Defaults to LTR when no strong character is found.
        /// </summary>
        public static bool IsRtlParagraph(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (char c in text)
            {
                BiDiCharType t = GetCharType(c);
                if (t == BiDiCharType.L) return false;
                if (t == BiDiCharType.R || t == BiDiCharType.AL) return true;
            }
            return false; // no strong character → LTR default
        }

        /// <summary>
        /// Splits <paramref name="text"/> into directional runs.
        /// Each run is either fully RTL or fully LTR.
        /// Neutral characters (whitespace, punctuation) are absorbed into the
        /// preceding strong run; if no preceding run exists they follow the
        /// paragraph base direction.
        /// </summary>
        public static List<BidiRun> GetBidiRuns(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<BidiRun>();

            bool paragraphRtl = IsRtlParagraph(text);
            int baseLevel = paragraphRtl ? 1 : 0;

            // Assign embedding levels per character (simplified two-level model)
            int[] levels = AssignLevels(text, baseLevel);

            // Group contiguous equal-level positions into runs
            var runs = new List<BidiRun>();
            int start = 0;
            while (start < text.Length)
            {
                int end = start + 1;
                while (end < text.Length && levels[end] == levels[start])
                    end++;

                bool isRtl = (levels[start] & 1) == 1;
                runs.Add(new BidiRun
                {
                    Text = text.Substring(start, end - start),
                    IsRtl = isRtl,
                    Level = levels[start],
                    StartIndex = start,
                    Length = end - start
                });
                start = end;
            }

            // Merge adjacent runs of the same direction for cleanliness
            return MergeAdjacentRuns(runs);
        }

        /// <summary>
        /// Reorders <paramref name="text"/> for left-to-right visual rendering
        /// according to the Unicode Bidi algorithm:
        /// <list type="bullet">
        ///   <item>RTL runs have their characters reversed.</item>
        ///   <item>When the paragraph direction is RTL the run order is reversed.</item>
        /// </list>
        /// </summary>
        public static string ReorderForDisplay(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            bool paragraphRtl = IsRtlParagraph(text);
            var runs = GetBidiRuns(text);

            // Reverse RTL run content
            for (int i = 0; i < runs.Count; i++)
            {
                if (runs[i].IsRtl)
                    runs[i] = new BidiRun
                    {
                        Text = ReverseString(runs[i].Text),
                        IsRtl = true,
                        Level = runs[i].Level,
                        StartIndex = runs[i].StartIndex,
                        Length = runs[i].Length
                    };
            }

            // Reverse run order for RTL paragraphs
            if (paragraphRtl)
                runs.Reverse();

            var sb = new StringBuilder(text.Length);
            foreach (var run in runs)
                sb.Append(run.Text);
            return sb.ToString();
        }

        /// <summary>
        /// Returns the BiDi character type of <paramref name="c"/> per UAX#9.
        /// </summary>
        public static BiDiCharType GetCharType(char c)
        {
            // ── Paragraph / Segment separators ──────────────────────────────────
            if (c == '\u000A' || c == '\u000D' || c == '\u2029') return BiDiCharType.B;
            if (c == '\u0009' || c == '\u000B') return BiDiCharType.S;

            // ── Boundary neutrals (formatting / BOM) ────────────────────────────
            if (c == '\u200B' || c == '\u200C' || c == '\u200D'
             || c == '\uFEFF' || c == '\u00AD')
                return BiDiCharType.BN;
            if (c < '\u0009') return BiDiCharType.BN;
            if (c == '\u000C' || c == '\u000E' || c == '\u000F') return BiDiCharType.BN;
            if (c >= '\u001C' && c <= '\u001F') return BiDiCharType.BN; // FS,GS,RS,US

            // ── Whitespace ───────────────────────────────────────────────────────
            if (c == ' ' || c == '\u00A0' || c == '\u1680'
             || (c >= '\u2000' && c <= '\u200A')
             || c == '\u202F' || c == '\u205F' || c == '\u3000')
                return BiDiCharType.WS;

            // ── Strong Left-to-Right ─────────────────────────────────────────────
            // Basic Latin letters
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                return BiDiCharType.L;
            // Latin-1 Supplement letters and beyond (through Extended Latin)
            if (c >= '\u00C0' && c <= '\u02AF') return BiDiCharType.L;
            // Greek, Coptic
            if (c >= '\u0370' && c <= '\u03FF') return BiDiCharType.L;
            // Cyrillic
            if (c >= '\u0400' && c <= '\u04FF') return BiDiCharType.L;
            // Devanagari and other Indic scripts (treated as LTR for our purposes)
            if (c >= '\u0900' && c <= '\u097F') return BiDiCharType.L;
            // CJK Unified Ideographs
            if (c >= '\u4E00' && c <= '\u9FFF') return BiDiCharType.L;
            // Hangul
            if (c >= '\uAC00' && c <= '\uD7AF') return BiDiCharType.L;
            // Latin Extended Additional / Enclosed CJK etc. — treat as L
            if (c >= '\u1E00' && c <= '\u1EFF') return BiDiCharType.L;
            // General Punctuation that is typically LTR (fallback)

            // ── Strong Right-to-Left (Hebrew) ────────────────────────────────────
            if (c >= '\u0590' && c <= '\u05FF') return BiDiCharType.R;
            if (c >= '\u07C0' && c <= '\u07EA') return BiDiCharType.R; // NKo
            if (c >= '\uFB1D' && c <= '\uFB4F') return BiDiCharType.R; // Hebrew Presentation Forms

            // ── Arabic Letters (AL) ──────────────────────────────────────────────
            if (c >= '\u0600' && c <= '\u060F') return BiDiCharType.AL; // Arabic (incl. signs)
            if (c >= '\u0610' && c <= '\u061F') return BiDiCharType.AL; // Arabic extended signs
            if (c >= '\u0620' && c <= '\u063F') return BiDiCharType.AL; // Core Arabic block
            if (c >= '\u0640' && c <= '\u065F') return BiDiCharType.AL; // Tatweel + harakat
            if (c >= '\u0660' && c <= '\u0669') return BiDiCharType.AN; // Arabic-Indic digits → AN
            if (c >= '\u066A' && c <= '\u06FF') return BiDiCharType.AL; // rest of Arabic block
            if (c >= '\u0750' && c <= '\u077F') return BiDiCharType.AL; // Arabic Supplement
            if (c >= '\u08A0' && c <= '\u08FF') return BiDiCharType.AL; // Arabic Extended-A
            if (c >= '\uFB50' && c <= '\uFDFF') return BiDiCharType.AL; // Arabic Presentation Forms-A
            if (c >= '\uFE70' && c <= '\uFEFF') return BiDiCharType.AL; // Arabic Presentation Forms-B

            // ── Arabic-Indic / Extended digits ──────────────────────────────────
            if (c >= '\u06F0' && c <= '\u06F9') return BiDiCharType.AN; // Extended Arabic-Indic
            if (c >= '\u0030' && c <= '\u0039') return BiDiCharType.EN; // ASCII digits

            // ── European Number ─────────────────────────────────────────────────
            if (c >= '\u00B2' && c <= '\u00B3') return BiDiCharType.EN; // superscripts
            if (c == '\u00B9') return BiDiCharType.EN;

            // ── Non-Spacing Marks ───────────────────────────────────────────────
            if (c >= '\u064B' && c <= '\u0670') return BiDiCharType.NSM; // Arabic harakat
            if (c >= '\u0300' && c <= '\u036F') return BiDiCharType.NSM; // Combining Diacritical Marks
            if (c >= '\u1DC0' && c <= '\u1DFF') return BiDiCharType.NSM;
            if (c >= '\u20D0' && c <= '\u20FF') return BiDiCharType.NSM;
            if (c >= '\uFE20' && c <= '\uFE2F') return BiDiCharType.NSM;

            // ── Other Neutral (default for unclassified punctuation/symbols) ────
            return BiDiCharType.ON;
        }

        /// <summary>
        /// Wraps <paramref name="text"/> at word boundaries so that no line
        /// exceeds <paramref name="maxCharsPerLine"/> characters.
        /// Word order within each line is preserved (respects RTL context).
        /// </summary>
        public static List<string> WrapText(string text, int maxCharsPerLine)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();

            if (maxCharsPerLine <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCharsPerLine),
                    "maxCharsPerLine must be greater than zero.");

            var lines = new List<string>();

            // Split on Unicode whitespace while keeping the whitespace token
            var words = SplitKeepingWhitespace(text);

            var currentLine = new StringBuilder();
            foreach (var word in words)
            {
                // Newline tokens → flush
                if (word == "\n" || word == "\r\n" || word == "\r")
                {
                    lines.Add(currentLine.ToString().TrimEnd(' '));
                    currentLine.Clear();
                    continue;
                }

                // Whitespace-only token between words
                if (string.IsNullOrWhiteSpace(word))
                {
                    if (currentLine.Length > 0)
                        currentLine.Append(' ');
                    continue;
                }

                int prospective = currentLine.Length + word.Length;
                if (currentLine.Length > 0) prospective++; // space separator

                if (prospective <= maxCharsPerLine)
                {
                    if (currentLine.Length > 0)
                        currentLine.Append(' ');
                    currentLine.Append(word);
                }
                else
                {
                    // Flush the current line
                    if (currentLine.Length > 0)
                    {
                        lines.Add(currentLine.ToString().TrimEnd(' '));
                        currentLine.Clear();
                    }

                    // Word itself exceeds limit → hard-break it
                    if (word.Length > maxCharsPerLine)
                    {
                        int offset = 0;
                        while (offset < word.Length)
                        {
                            int take = Math.Min(maxCharsPerLine, word.Length - offset);
                            lines.Add(word.Substring(offset, take));
                            offset += take;
                        }
                    }
                    else
                    {
                        currentLine.Append(word);
                    }
                }
            }

            if (currentLine.Length > 0)
                lines.Add(currentLine.ToString().TrimEnd(' '));

            return lines;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Internal helpers
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Assigns a BiDi embedding level (0 = LTR, 1 = RTL, 2 = LTR-override-inside-RTL)
        /// to each character.  This implements a simplified two-pass X/N/I/L rule subset.
        /// </summary>
        private static int[] AssignLevels(string text, int baseLevel)
        {
            int len = text.Length;
            int[] levels = new int[len];

            // Determine per-character "strong" direction, ignoring neutrals temporarily
            // First pass: assign strong chars their natural level
            for (int i = 0; i < len; i++)
            {
                BiDiCharType t = GetCharType(text[i]);
                levels[i] = t switch
                {
                    BiDiCharType.AL or BiDiCharType.R => 1,
                    BiDiCharType.L => 0,
                    BiDiCharType.AN => 1,
                    BiDiCharType.EN => 0, // EN treated as LTR at this stage
                    BiDiCharType.NSM => -1, // will inherit
                    _ => -1  // neutral — will inherit
                };
            }

            // Second pass: resolve neutrals by inheritance from neighbours
            // (simplified N1/N2: neutral inherits the nearest strong level)
            // Forward propagation from last known strong level
            int lastLevel = baseLevel;
            for (int i = 0; i < len; i++)
            {
                if (levels[i] == -1)
                    levels[i] = lastLevel; // tentative
                else
                    lastLevel = levels[i];
            }

            // Backward propagation for neutrals at the start
            lastLevel = baseLevel;
            for (int i = len - 1; i >= 0; i--)
            {
                BiDiCharType t = GetCharType(text[i]);
                bool isNeutral = t is BiDiCharType.WS or BiDiCharType.ON
                                   or BiDiCharType.BN or BiDiCharType.NSM
                                   or BiDiCharType.S;
                if (!isNeutral)
                {
                    lastLevel = levels[i];
                }
                else
                {
                    // If neutral spans only neutrals until the end, reset to base
                    // (already assigned base during forward pass — keep it)
                }
            }

            // Resolve EN in RTL context → treat as AN (RTL)
            for (int i = 0; i < len; i++)
            {
                if (GetCharType(text[i]) == BiDiCharType.EN)
                {
                    // Look for nearest preceding strong character
                    bool inRtlContext = false;
                    for (int k = i - 1; k >= 0; k--)
                    {
                        BiDiCharType tk = GetCharType(text[k]);
                        if (tk == BiDiCharType.AL) { inRtlContext = true; break; }
                        if (tk == BiDiCharType.L) { inRtlContext = false; break; }
                    }
                    if (inRtlContext)
                        levels[i] = 1;
                }
            }

            return levels;
        }

        private static List<BidiRun> MergeAdjacentRuns(List<BidiRun> runs)
        {
            if (runs.Count <= 1)
                return runs;

            var merged = new List<BidiRun> { runs[0] };
            for (int i = 1; i < runs.Count; i++)
            {
                var prev = merged[merged.Count - 1];
                var curr = runs[i];
                if (prev.IsRtl == curr.IsRtl && prev.Level == curr.Level)
                {
                    merged[merged.Count - 1] = new BidiRun
                    {
                        Text = prev.Text + curr.Text,
                        IsRtl = prev.IsRtl,
                        Level = prev.Level,
                        StartIndex = prev.StartIndex,
                        Length = prev.Length + curr.Length
                    };
                }
                else
                {
                    merged.Add(curr);
                }
            }
            return merged;
        }

        private static string ReverseString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            char[] arr = s.ToCharArray();
            Array.Reverse(arr);
            return new string(arr);
        }

        /// <summary>
        /// Splits text into tokens separated by whitespace.
        /// Whitespace itself is kept as separate tokens (including newlines).
        /// </summary>
        private static List<string> SplitKeepingWhitespace(string text)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inSpace = false;

            foreach (char c in text)
            {
                bool isNewline = c == '\n' || c == '\r';
                bool isSpace = c == ' ' || c == '\t' || c == '\u00A0';

                if (isNewline)
                {
                    if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                    tokens.Add(c.ToString());
                    inSpace = false;
                }
                else if (isSpace)
                {
                    if (!inSpace && sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }
                    tokens.Add(" ");
                    inSpace = true;
                }
                else
                {
                    if (inSpace) inSpace = false;
                    sb.Append(c);
                }
            }
            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            return tokens;
        }
    }
}
