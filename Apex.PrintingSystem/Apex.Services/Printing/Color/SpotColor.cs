using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Services.Printing.Color
{
    /// <summary>
    /// A named ink that is NOT built from cyan, magenta, yellow and black.
    ///
    /// A brand colour, a metallic, a varnish or a die-cut line is mixed as its own ink
    /// and gets its own plate. Two things follow that process colour never has to deal
    /// with: the name must survive to the plate room exactly as the customer specified
    /// it, and the CMYK values carried alongside are a PREVIEW only — printing the
    /// approximation instead of pulling the plate is the classic way a brand colour
    /// comes back wrong from press.
    /// </summary>
    /// <param name="Name">
    /// The ink name as the customer and the plate room know it, e.g. "PANTONE 185 C".
    /// </param>
    /// <param name="AlternateC">Preview cyan (0–1).</param>
    /// <param name="AlternateM">Preview magenta (0–1).</param>
    /// <param name="AlternateY">Preview yellow (0–1).</param>
    /// <param name="AlternateK">Preview black (0–1).</param>
    /// <param name="IsTechnical">
    /// True for inks that must never be printed as colour — die lines, crease lines,
    /// varnish. They belong on their own plate and are stripped before the press run.
    /// </param>
    public sealed record SpotColor(
        string Name,
        double AlternateC,
        double AlternateM,
        double AlternateY,
        double AlternateK,
        bool IsTechnical = false)
    {
        /// <summary>The preview colour at a given tint (0–1), for screen only.</summary>
        public (byte C, byte M, byte Y, byte K) PreviewAt(double tint)
        {
            double t = Math.Clamp(tint, 0, 1);
            return (
                To255(AlternateC * t), To255(AlternateM * t),
                To255(AlternateY * t), To255(AlternateK * t));
        }

        private static byte To255(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    }

    /// <summary>
    /// Whether an object knocks a hole in what is beneath it, or prints on top of it.
    /// </summary>
    public enum OverprintMode
    {
        /// <summary>
        /// The default. What is underneath is removed so the inks do not mix — but any
        /// press misregistration then shows as a white line around the object.
        /// </summary>
        Knockout,

        /// <summary>
        /// Printed over the top. For small black text this is what prevents a white
        /// halo on a coloured background; for light colours over dark it produces a
        /// muddy mix instead of the intended colour.
        /// </summary>
        Overprint,
    }

    /// <summary>What is wrong, and why it matters on press.</summary>
    public sealed record InkIssue(string Severity, string Message);

    /// <summary>
    /// Checks the ink decisions that cost money on press.
    ///
    /// Every rule here corresponds to a job that has to be reprinted: a plate the shop
    /// did not know it was paying for, a white halo around every line of small text, a
    /// die line printed as a visible red stroke on the finished piece.
    /// </summary>
    public static class InkPreflight
    {
        /// <summary>Small text where a knockout will show every registration error.</summary>
        public const double SmallTextPoints = 12.0;

        /// <summary>
        /// Black text small enough that knockout is a risk. Solid black over a colour
        /// should overprint: the ink is opaque enough to cover, and a knockout leaves a
        /// white outline the moment the press drifts by a hair.
        /// </summary>
        public static InkIssue? CheckBlackTextOverprint(
            double fontSizePoints, bool isSolidBlack, OverprintMode mode, bool onColouredBackground)
        {
            if (!isSolidBlack || !onColouredBackground) return null;
            if (fontSizePoints > SmallTextPoints) return null;
            if (mode == OverprintMode.Overprint) return null;

            return new InkIssue("warning",
                $"نص أسود بحجم {fontSizePoints:0.#} نقطة فوق خلفية ملوّنة بوضع Knockout — " +
                "أي انزياح في التسجيل سيُظهر هالة بيضاء حول الحروف. الأنسب Overprint.");
        }

        /// <summary>
        /// A light colour set to overprint. It will mix with what is underneath instead
        /// of covering it, which almost never produces the colour on the proof.
        /// </summary>
        public static InkIssue? CheckLightOverprint(
            (byte C, byte M, byte Y, byte K) ink, OverprintMode mode, bool onColouredBackground)
        {
            if (mode != OverprintMode.Overprint || !onColouredBackground) return null;

            // "Light" = little ink of any kind. Such a tint cannot cover anything.
            int total = ink.C + ink.M + ink.Y + ink.K;
            if (total > 200) return null;

            return new InkIssue("warning",
                "لون فاتح بوضع Overprint فوق خلفية ملوّنة — سيختلط بما تحته " +
                "بدل أن يغطّيه، والنتيجة لن تطابق البروفة.");
        }

        /// <summary>
        /// Technical inks (die, crease, varnish) that were left in the colour plates.
        /// They must go on their own plate or they print as visible lines on the piece.
        /// </summary>
        public static IReadOnlyList<InkIssue> CheckTechnicalInks(IEnumerable<SpotColor> used)
        {
            return used.Where(s => s.IsTechnical)
                       .Select(s => new InkIssue("info",
                           $"«{s.Name}» حبر تقني (قص/تجليخ/ورنيش) — يجب فصله على لوح مستقل " +
                           "ولا يُطبع ضمن الألوان."))
                       .ToList();
        }

        /// <summary>
        /// Counts the plates a job will need. A shop quoting four-colour work and
        /// finding two extra spot plates at the last minute either eats the cost or
        /// delays the job.
        /// </summary>
        public static int CountPlates(IEnumerable<SpotColor> spots, bool usesProcess)
        {
            int spotPlates = spots
                .Where(s => !s.IsTechnical)
                .Select(s => s.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return spotPlates + (usesProcess ? 4 : 0);
        }

        /// <summary>
        /// Spot inks named on objects but not defined in the job. They would silently
        /// fall back to process colour — the customer's brand colour printed as an
        /// approximation, which is exactly the failure spot inks exist to prevent.
        /// </summary>
        public static IReadOnlyList<string> FindUndefinedSpots(
            IEnumerable<string> namesUsed, IEnumerable<SpotColor> defined)
        {
            var known = new HashSet<string>(
                defined.Select(s => s.Name.Trim()), StringComparer.OrdinalIgnoreCase);

            return namesUsed
                .Select(n => (n ?? "").Trim())
                .Where(n => n.Length > 0 && !known.Contains(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
