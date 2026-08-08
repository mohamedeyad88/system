using Apex.Core.Localization;
using Apex.Core.Models.Imposition;
using System.Collections.Generic;

namespace Apex.Services.Imposition
{
    /// <summary>
    /// Standard, ready-to-run imposition presets so an operator does not have to
    /// re-derive the common signature layouts (8pp / 16pp / 32pp and the everyday
    /// N-Up and card jobs) by hand on every job.
    ///
    /// These are READ-ONLY: they are produced fresh on every call and are never
    /// written to the template folder, so they cannot be corrupted or deleted.
    /// Saving one after editing simply creates a normal user template.
    /// </summary>
    public static class BuiltInImpositionTemplates
    {
        /// <summary>Marks an Id as belonging to a built-in preset.</summary>
        public const string IdPrefix = "builtin-";

        public static bool IsBuiltIn(ImpositionTemplate? t) =>
            t?.Id?.StartsWith(IdPrefix, System.StringComparison.Ordinal) == true;

        /// <summary>All standard presets, in the order they should be offered.</summary>
        public static IReadOnlyList<ImpositionTemplate> All() => new List<ImpositionTemplate>
        {
            // ── Folded signatures ──────────────────────────────────────────────
            // 8pp: two A5 leaves per SRA3 sheet → 4 sheets of 4 pages.
            Signature("8pp", 8, ImpositionType.SaddleStitch, leaves: 2),
            // 16pp: the workhorse booklet signature.
            Signature("16pp", 16, ImpositionType.SaddleStitch, leaves: 4),
            // 32pp: perfect-bound book, 8 leaves per signature.
            Signature("32pp", 32, ImpositionType.PerfectBinding, leaves: 8),

            // ── Everyday N-Up on oversize stock ───────────────────────────────
            // Sizes are deliberately planned on SRA3 (320×450): trimmed A-series
            // sheets leave no room for bleed once the pages butt together, so shops
            // impose on oversize stock and trim down.
            NUp("A4onSRA3", NUpLayout.TwoUp, pageW: 210, pageH: 297, sheetW: 320, sheetH: 450),
            NUp("A5onSRA3", NUpLayout.FourUp, pageW: 148, pageH: 210, sheetW: 320, sheetH: 450),
            NUp("A6onSRA3", NUpLayout.EightUp, pageW: 105, pageH: 148, sheetW: 320, sheetH: 450),

            // ── Cards on SRA3, single plate ───────────────────────────────────
            Cards("Cards", pageW: 90, pageH: 50),
            WorkTurn("CardsWT", pageW: 90, pageH: 50),
        };

        // ── Builders ──────────────────────────────────────────────────────────

        private static ImpositionTemplate Make(string key, ImpositionInput input) => new()
        {
            Id = IdPrefix + key,
            Name = AppLocalizer.L($"ImpTpl_{key}"),
            Input = input,
            Marks = new PrintMarksOptions { CropMarks = true, JobInfo = true },
        };

        private static ImpositionTemplate Signature(
            string key, int pages, ImpositionType type, int leaves) =>
            Make(key, new ImpositionInput
            {
                Type = type,
                SourcePageCount = pages,
                LeavesPerSignature = leaves,
                Binding = type == ImpositionType.PerfectBinding
                    ? BindingType.PerfectBinding
                    : BindingType.SaddleStitch,
                PageWidth = 148,
                PageHeight = 210,
                SheetWidth = 320,
                SheetHeight = 450,
                Bleed = 3,
                // Pages butt together and share their bleed, so the gutter is 0 and
                // the margin is only what the press needs to hold the sheet.
                SheetMargin = 5,
                Gutter = 0,
                // A booklet is only as good as its creep compensation; seed a sane
                // caliper for 100 gsm stock, which the operator can adjust.
                PaperThickness = 0.1,
                Alignment = SheetAlignment.Centre,
            });

        private static ImpositionTemplate NUp(
            string key, NUpLayout n, double pageW, double pageH, double sheetW, double sheetH) =>
            Make(key, new ImpositionInput
            {
                Type = ImpositionType.NUp,
                NUp = n,
                SourcePageCount = (int)n,
                PageWidth = pageW,
                PageHeight = pageH,
                SheetWidth = sheetW,
                SheetHeight = sheetH,
                Bleed = 2,
                SheetMargin = 5,
                Gutter = 0,
                AllowRotation = true,
                Alignment = SheetAlignment.Centre,
            });

        private static ImpositionTemplate Cards(string key, double pageW, double pageH) =>
            Make(key, new ImpositionInput
            {
                Type = ImpositionType.StepRepeat,
                SourcePageCount = 1,
                RequiredCopies = 1000,
                PageWidth = pageW,
                PageHeight = pageH,
                SheetWidth = 320,
                SheetHeight = 450,
                Bleed = 2,
                SheetMargin = 10,
                Gutter = 3,
                AllowRotation = true,
            });

        private static ImpositionTemplate WorkTurn(string key, double pageW, double pageH) =>
            Make(key, new ImpositionInput
            {
                Type = ImpositionType.WorkAndTurn,
                SourcePageCount = 2,          // front + back of the card
                RequiredCopies = 1000,
                PageWidth = pageW,
                PageHeight = pageH,
                SheetWidth = 320,
                SheetHeight = 450,
                Bleed = 2,
                SheetMargin = 10,
                Gutter = 3,
                AllowRotation = true,
            });
    }
}
