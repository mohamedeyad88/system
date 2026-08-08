using Apex.Core.Models.PaperCutting;

namespace Apex.Core.Models.Imposition
{
    /// <summary>
    /// All inputs for the imposition planner. Length values are in the unit given by
    /// <see cref="Unit"/>. This is the logic-layer contract (Phase 1) — no PDF I/O.
    /// </summary>
    public class ImpositionInput
    {
        // ── Source job ─────────────────────────────────────────────────────
        /// <summary>Number of pages in the source document.</summary>
        public int SourcePageCount { get; set; }

        /// <summary>Finished (trimmed) page width of one source page.</summary>
        public double PageWidth { get; set; }

        /// <summary>Finished (trimmed) page height of one source page.</summary>
        public double PageHeight { get; set; }

        // ── Press sheet ────────────────────────────────────────────────────
        public double SheetWidth { get; set; }
        public double SheetHeight { get; set; }

        // ── Scheme ─────────────────────────────────────────────────────────
        public ImpositionType Type { get; set; } = ImpositionType.NUp;

        /// <summary>Used when <see cref="Type"/> is <see cref="ImpositionType.NUp"/>.</summary>
        public NUpLayout NUp { get; set; } = NUpLayout.TwoUp;

        /// <summary>Binding method (drives signature size for booklet schemes).</summary>
        public BindingType Binding { get; set; } = BindingType.SaddleStitch;

        /// <summary>
        /// Leaves (folded sheets) per signature for Perfect Binding.
        /// 1 leaf = 4 pages. Ignored for Saddle Stitch (one nested signature).
        /// </summary>
        public int LeavesPerSignature { get; set; } = 4;

        /// <summary>
        /// Copies required for Cut &amp; Stack / Step &amp; Repeat planning.
        /// For booklet/N-Up document schemes this is the number of finished copies.
        /// </summary>
        public int RequiredCopies { get; set; } = 1;

        // ── Bleed / margins / gutters ──────────────────────────────────────
        /// <summary>Bleed added around each placed page (uniform default).</summary>
        public double Bleed { get; set; } = 3;

        /// <summary>Left/right bleed override; null uses <see cref="Bleed"/>.</summary>
        public double? BleedHorizontal { get; set; }

        /// <summary>Top/bottom bleed override; null uses <see cref="Bleed"/>.</summary>
        public double? BleedVertical { get; set; }

        /// <summary>Outer margin from the sheet edge to the first page (uniform default).</summary>
        public double SheetMargin { get; set; } = 10;

        // Per-edge margin overrides; null falls back to SheetMargin. These exist for
        // the GRIPPER edge: the press jaws hold one edge of the sheet, so that edge
        // needs a larger un-printable margin than the other three.
        public double? MarginLeft { get; set; }
        public double? MarginTop { get; set; }
        public double? MarginRight { get; set; }
        public double? MarginBottom { get; set; }

        /// <summary>Gap between adjacent placed pages (uniform default).</summary>
        public double Gutter { get; set; } = 5;

        /// <summary>Gap between COLUMNS; null uses <see cref="Gutter"/>.</summary>
        public double? GutterHorizontal { get; set; }

        /// <summary>Gap between ROWS; null uses <see cref="Gutter"/>.</summary>
        public double? GutterVertical { get; set; }

        /// <summary>Allow 90° rotation of pages to improve fit (N-Up / Step &amp; Repeat).</summary>
        public bool AllowRotation { get; set; } = true;

        /// <summary>
        /// Caliper (thickness) of ONE sheet of the chosen paper, used to compensate
        /// creep (shingling) in folded signatures. Nested sheets push the inner pages
        /// toward the fore-edge, so without compensation the inner pages lose more
        /// margin when the book is trimmed. Set to 0 to disable compensation.
        /// </summary>
        public double PaperThickness { get; set; }

        // ── Units ──────────────────────────────────────────────────────────
        public MeasurementUnit Unit { get; set; } = MeasurementUnit.Millimeter;

        public SheetOrientation Orientation { get; set; } = SheetOrientation.Portrait;

        /// <summary>Where the grid sits on the sheet when it doesn't fill the printable area.</summary>
        public SheetAlignment Alignment { get; set; } = SheetAlignment.Centre;

        /// <summary>
        /// When true and the page (after bleed) is larger than the sheet slot, the pages are
        /// scaled down proportionally to fit the selected sheet (after margins/bleed) instead
        /// of failing. Used for N-Up.
        /// </summary>
        public bool ScaleToFit { get; set; }

        /// <summary>
        /// For duplex (booklet) schemes: mirror the BACK-side slots left↔right so the back
        /// registers against the same physical sheet edge as the front when printing 2-sided.
        /// </summary>
        public bool MirrorBacksHorizontally { get; set; }

        /// <summary>
        /// When true, an incomplete last sheet re-aligns its own pages (re-centred for its
        /// actual count) instead of leaving them in the full-grid positions. N-Up only.
        /// </summary>
        public bool AlignSheetsIndependently { get; set; }
    }
}
