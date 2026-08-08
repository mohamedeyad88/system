using Apex.Core.Localization;
using Apex.Core.Models.Imposition;
using Apex.Core.Models.PaperCutting;
using Apex.Services.PaperCutting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Apex.Services.Imposition
{
    /// <summary>
    /// Phase 1 imposition planner: computes how source pages are arranged onto press
    /// sheets for each scheme (N-Up, Saddle Stitch, Perfect Binding, Cut &amp; Stack,
    /// Step &amp; Repeat). Pure geometry/ordering logic — no PDF I/O (that is Phase 2).
    ///
    /// Reuses <see cref="PaperCuttingOptimizerService"/> for the "fill the sheet for
    /// maximum pieces" math behind Cut &amp; Stack and Step &amp; Repeat.
    /// </summary>
    public class ImpositionService
    {
        private readonly PaperCuttingOptimizerService _paperOptimizer;

        public ImpositionService(PaperCuttingOptimizerService paperOptimizer)
        {
            _paperOptimizer = paperOptimizer ?? throw new ArgumentNullException(nameof(paperOptimizer));
        }

        // ── Unit conversion ────────────────────────────────────────────────────
        private static double ToMm(double value, MeasurementUnit unit) => unit switch
        {
            MeasurementUnit.Centimeter => value * 10.0,
            MeasurementUnit.Inch => value * 25.4,
            _ => value
        };

        // ── Public entry point ──────────────────────────────────────────────────

        public ImpositionResult Plan(ImpositionInput input)
        {
            var error = Validate(input);
            if (!string.IsNullOrEmpty(error))
                return new ImpositionResult { IsValid = false, ErrorMessage = error };

            var result = input.Type switch
            {
                ImpositionType.NUp => PlanNUp(input),
                ImpositionType.SaddleStitch => PlanSaddleStitch(input),
                ImpositionType.PerfectBinding => PlanPerfectBinding(input),
                ImpositionType.CutStack => PlanCutStack(input),
                ImpositionType.StepRepeat => PlanStepRepeat(input),
                ImpositionType.WorkAndTurn => PlanWorkAndTurn(input, tumble: false),
                ImpositionType.WorkAndTumble => PlanWorkAndTurn(input, tumble: true),
                _ => new ImpositionResult { IsValid = false, ErrorMessage = AppLocalizer.L("ImpS_UnsupportedType") }
            };

            // Work-and-turn/tumble backs are already positioned by the sheet flip, so
            // mirroring them again would misregister the backup.
            bool backsAreFlipPositions = input.Type is ImpositionType.WorkAndTurn
                                                    or ImpositionType.WorkAndTumble;
            if (input.MirrorBacksHorizontally && result.IsValid && result.IsDuplex && !backsAreFlipPositions)
                MirrorBacks(result);

            return result;
        }

        /// <summary>Mirrors every back-side slot left↔right for correct 2-sided registration.</summary>
        private static void MirrorBacks(ImpositionResult result)
        {
            foreach (var sheet in result.Sheets)
            {
                if (sheet.Back == null) continue;
                foreach (var slot in sheet.Back.Slots)
                    slot.X = result.SheetWidthMm - slot.X - slot.Width;
            }
        }

        // ── Validation ───────────────────────────────────────────────────────────

        private static string Validate(ImpositionInput i)
        {
            if (i.PageWidth <= 0) return AppLocalizer.L("ImpS_PageWidthPositive");
            if (i.PageHeight <= 0) return AppLocalizer.L("ImpS_PageHeightPositive");
            if (i.SheetWidth <= 0) return AppLocalizer.L("ImpS_SheetWidthPositive");
            if (i.SheetHeight <= 0) return AppLocalizer.L("ImpS_SheetHeightPositive");
            if (i.Bleed < 0) return AppLocalizer.L("ImpS_BleedNonNeg");
            if (i.SheetMargin < 0) return AppLocalizer.L("ImpS_MarginNonNeg");
            if (i.Gutter < 0) return AppLocalizer.L("ImpS_GutterNonNeg");

            bool needsPages = i.Type is ImpositionType.NUp or ImpositionType.SaddleStitch
                                       or ImpositionType.PerfectBinding or ImpositionType.CutStack
                                       or ImpositionType.WorkAndTurn or ImpositionType.WorkAndTumble;
            if (needsPages && i.SourcePageCount <= 0)
                return AppLocalizer.L("ImpS_SourcePagesPositive");
            if (i.Type is ImpositionType.CutStack or ImpositionType.StepRepeat
                       or ImpositionType.WorkAndTurn or ImpositionType.WorkAndTumble
                && i.RequiredCopies <= 0)
                return AppLocalizer.L("ImpS_CopiesPositive");
            return string.Empty;
        }

        // ── N-Up ──────────────────────────────────────────────────────────────────

        private ImpositionResult PlanNUp(ImpositionInput input)
        {
            int n = (int)input.NUp;
            double sheetW = ToMm(input.SheetWidth, input.Unit);
            double sheetH = ToMm(input.SheetHeight, input.Unit);
            var (placedW, placedH) = ResolvePlacedSize(input);
            var margin = ResolveMargins(input);
            var gutter = ResolveGutters(input);

            var (hf, vf) = AlignFactors(input.Alignment);
            var grid = ChooseGrid(n, sheetW, sheetH, placedW, placedH, margin, gutter, input.AllowRotation, hf, vf);
            if (grid == null && input.ScaleToFit)
                grid = ChooseGridFitted(n, sheetW, sheetH, placedW, placedH, margin, gutter, input.AllowRotation, hf, vf);
            if (grid == null)
                return new ImpositionResult
                {
                    IsValid = false,
                    ErrorMessage = AppLocalizer.L("ImpS_PageBiggerThanSheet")
                };

            int perSheet = grid.Cols * grid.Rows;
            int sheetsRequired = (int)Math.Ceiling((double)input.SourcePageCount / perSheet);

            var result = NewResult(input, sheetW, sheetH, grid);
            result.PagesPerSide = perSheet;
            result.SheetsRequired = sheetsRequired;
            result.IsDuplex = false;

            int pageNo = 1;
            for (int s = 0; s < sheetsRequired; s++)
            {
                int pagesOnSheet = Math.Min(perSheet, input.SourcePageCount - s * perSheet);

                // Re-align a partial last sheet to centre its own pages (when requested).
                var g = grid;
                if (input.AlignSheetsIndependently && pagesOnSheet > 0 && pagesOnSheet < perSheet)
                {
                    int rowsUsed = (int)Math.Ceiling((double)pagesOnSheet / grid.Cols);
                    int colsUsed = rowsUsed > 1 ? grid.Cols : pagesOnSheet;
                    g = ReAlignGrid(grid, colsUsed, rowsUsed, sheetW, sheetH, margin, gutter, hf, vf);
                }

                var sheet = new SheetLayout { SheetIndex = s + 1 };
                for (int idx = 0; idx < perSheet && pageNo <= input.SourcePageCount; idx++)
                {
                    int c = idx % grid.Cols;
                    int r = idx / grid.Cols;
                    sheet.Front.Slots.Add(MakeSlot(pageNo++, c, r, g));
                }
                result.Sheets.Add(sheet);
            }

            result.UtilizationPercent = Utilization(perSheet, grid.PlacedW, grid.PlacedH, sheetW, sheetH);
            bool scaled = grid.PlacedW < placedW - 1e-6 || grid.PlacedH < placedH - 1e-6;
            result.HumanReadableSummary = BuildSummary(result, input, AppLocalizer.Lf("ImpS_SchemeNUp", n)) +
                (scaled ? AppLocalizer.Lf("ImpS_ScaledToFit", grid.PlacedW / placedW * 100) : "");
            return result;
        }

        /// <summary>
        /// Fit-to-sheet grid: for each factor pair/orientation, scales the placed page down to
        /// fit the sheet slot (keeping aspect) and returns the grid with the LARGEST scale.
        /// </summary>
        private static GridGeometry? ChooseGridFitted(
            int n, double sheetW, double sheetH, double placedW, double placedH,
            Margins margin, Gutters gutter, bool allowRotation, double hf, double vf)
        {
            double availW = sheetW - margin.Horizontal, availH = sheetH - margin.Vertical;
            if (availW <= 0 || availH <= 0) return null;

            GridGeometry? best = null;
            double bestScale = 0;
            foreach (var (cols, rows) in FactorPairs(n))
            {
                foreach (bool rotate in allowRotation ? new[] { false, true } : new[] { false })
                {
                    double pw = rotate ? placedH : placedW;
                    double ph = rotate ? placedW : placedH;
                    double slotW = (availW - (cols - 1) * gutter.Horizontal) / cols;
                    double slotH = (availH - (rows - 1) * gutter.Vertical) / rows;
                    if (slotW <= 0 || slotH <= 0) continue;

                    double scale = Math.Min(slotW / pw, slotH / ph);
                    if (scale <= 0 || scale <= bestScale) continue;

                    double spw = pw * scale, sph = ph * scale;
                    double needW = cols * spw + (cols - 1) * gutter.Horizontal;
                    double needH = rows * sph + (rows - 1) * gutter.Vertical;
                    bestScale = scale;
                    best = new GridGeometry
                    {
                        Cols = cols, Rows = rows, PlacedW = spw, PlacedH = sph,
                        StepX = spw + gutter.Horizontal, StepY = sph + gutter.Vertical,
                        StartX = margin.Left + (availW - needW) * hf,
                        StartY = margin.Top + (availH - needH) * vf,
                        Rotated = rotate
                    };
                }
            }
            return best;
        }

        // ── Saddle Stitch ───────────────────────────────────────────────────────

        private ImpositionResult PlanSaddleStitch(ImpositionInput input)
        {
            // Pad to a multiple of 4 (each sheet = 4 pages: 2 per side, duplex).
            int padded = RoundUpToMultiple(input.SourcePageCount, 4);
            int padding = padded - input.SourcePageCount;
            int sheets = padded / 4;

            // Two portrait pages side-by-side → 2-Up landscape grid (2 cols × 1 row).
            double sheetW = ToMm(input.SheetWidth, input.Unit);
            double sheetH = ToMm(input.SheetHeight, input.Unit);
            var (placedW, placedH) = ResolvePlacedSize(input);
            var margin = ResolveMargins(input);
            var gutter = ResolveGutters(input);

            var grid = BuildGridGeometry(2, 1, sheetW, sheetH, placedW, placedH, margin, gutter);
            if (grid == null)
                return new ImpositionResult
                {
                    IsValid = false,
                    ErrorMessage = AppLocalizer.L("ImpS_PageBiggerThanSheet")
                };

            var result = NewResult(input, sheetW, sheetH, grid);
            result.PagesPerSide = 2;
            result.IsDuplex = true;
            result.SignatureCount = 1;
            result.PaddingPages = padding;
            result.SheetsRequired = sheets;
            if (padding > 0)
                result.Warnings.Add(AppLocalizer.Lf("ImpS_PaddingSaddle", padding));

            double creepPerSheet = CreepPerSheet(input, grid, sheets);
            result.MaxCreepMm = creepPerSheet * Math.Max(0, sheets - 1);
            if (result.MaxCreepMm > 0)
                result.Warnings.Add(AppLocalizer.Lf("ImpS_CreepApplied", result.MaxCreepMm));

            BuildSaddleSheets(result, grid, firstPage: 1, totalPages: padded, sheetOffset: 0, signature: 1,
                              creepPerSheetMm: creepPerSheet);

            result.UtilizationPercent = Utilization(2, placedW, placedH, sheetW, sheetH);
            result.HumanReadableSummary = BuildSummary(result, input, AppLocalizer.L("ImpS_SchemeSaddle"));
            return result;
        }

        // ── Perfect Binding (gathered signatures, each folded internally) ─────────

        private ImpositionResult PlanPerfectBinding(ImpositionInput input)
        {
            int leaves = Math.Max(1, input.LeavesPerSignature);
            int pagesPerSig = leaves * 4;

            int padded = RoundUpToMultiple(input.SourcePageCount, pagesPerSig);
            int padding = padded - input.SourcePageCount;
            int signatures = padded / pagesPerSig;
            int sheetsPerSig = pagesPerSig / 4;

            double sheetW = ToMm(input.SheetWidth, input.Unit);
            double sheetH = ToMm(input.SheetHeight, input.Unit);
            var (placedW, placedH) = ResolvePlacedSize(input);
            var margin = ResolveMargins(input);
            var gutter = ResolveGutters(input);

            var grid = BuildGridGeometry(2, 1, sheetW, sheetH, placedW, placedH, margin, gutter);
            if (grid == null)
                return new ImpositionResult
                {
                    IsValid = false,
                    ErrorMessage = AppLocalizer.L("ImpS_PageBiggerThanSheet")
                };

            var result = NewResult(input, sheetW, sheetH, grid);
            result.PagesPerSide = 2;
            result.IsDuplex = true;
            result.SignatureCount = signatures;
            result.PaddingPages = padding;
            result.SheetsRequired = signatures * sheetsPerSig;
            if (padding > 0)
                result.Warnings.Add(AppLocalizer.Lf("ImpS_PaddingPerfect", padding, pagesPerSig));

            // Creep is a property of ONE folded signature, so it resets for each
            // signature rather than accumulating across the whole book.
            double creepPerSheet = CreepPerSheet(input, grid, sheetsPerSig);
            result.MaxCreepMm = creepPerSheet * Math.Max(0, sheetsPerSig - 1);
            if (result.MaxCreepMm > 0)
                result.Warnings.Add(AppLocalizer.Lf("ImpS_CreepApplied", result.MaxCreepMm));

            for (int sig = 0; sig < signatures; sig++)
            {
                int firstPage = sig * pagesPerSig + 1;
                BuildSaddleSheets(result, grid, firstPage, pagesPerSig,
                                  sheetOffset: sig * sheetsPerSig, signature: sig + 1,
                                  creepPerSheetMm: creepPerSheet);
            }

            result.UtilizationPercent = Utilization(2, placedW, placedH, sheetW, sheetH);
            result.HumanReadableSummary = BuildSummary(result, input, AppLocalizer.L("ImpS_SchemePerfect"));
            return result;
        }

        // ── Cut & Stack and Step & Repeat (reuse the paper-cutting optimizer) ─────

        private ImpositionResult PlanCutStack(ImpositionInput input)
        {
            var (pieces, sheetsRaw, blocks, sheetW, sheetH, util) = OptimizeFill(input, input.RequiredCopies);
            if (pieces <= 0)
                return new ImpositionResult { IsValid = false, ErrorMessage = AppLocalizer.L("ImpS_PageTooBigRepeat") };

            // Cut & Stack: each sheet carries 'pieces' slots; after cutting+stacking the
            // stacks collate in page order. Slot page numbers cycle through the source pages.
            int sheetsRequired = (int)Math.Ceiling((double)(input.RequiredCopies * input.SourcePageCount) / pieces);

            var result = NewResultFromBlocks(input, sheetW, sheetH, pieces, blocks);
            result.SheetsRequired = sheetsRequired;
            result.UtilizationPercent = util;

            int pageNo = 1;
            for (int s = 0; s < sheetsRequired; s++)
            {
                var sheet = new SheetLayout { SheetIndex = s + 1 };
                foreach (var slotPos in EnumerateBlockSlots(blocks))
                {
                    var slot = slotPos;
                    slot.SourcePageNumber = ((pageNo - 1) % input.SourcePageCount) + 1;
                    pageNo++;
                    sheet.Front.Slots.Add(slot);
                }
                result.Sheets.Add(sheet);
            }
            result.HumanReadableSummary = BuildSummary(result, input, AppLocalizer.L("ImpS_SchemeCutStack"));
            return result;
        }

        private ImpositionResult PlanStepRepeat(ImpositionInput input)
        {
            var (pieces, _, blocks, sheetW, sheetH, util) = OptimizeFill(input, input.RequiredCopies);
            if (pieces <= 0)
                return new ImpositionResult { IsValid = false, ErrorMessage = AppLocalizer.L("ImpS_DesignTooBigRepeat") };

            int sheetsRequired = (int)Math.Ceiling((double)input.RequiredCopies / pieces);

            var result = NewResultFromBlocks(input, sheetW, sheetH, pieces, blocks);
            result.SheetsRequired = sheetsRequired;
            result.UtilizationPercent = util;

            for (int s = 0; s < sheetsRequired; s++)
            {
                var sheet = new SheetLayout { SheetIndex = s + 1 };
                foreach (var slotPos in EnumerateBlockSlots(blocks))
                {
                    var slot = slotPos;
                    slot.SourcePageNumber = 1; // single design repeated
                    sheet.Front.Slots.Add(slot);
                }
                result.Sheets.Add(sheet);
            }
            result.HumanReadableSummary = BuildSummary(result, input, AppLocalizer.L("ImpS_SchemeStepRepeat"));
            return result;
        }

        // ── Booklet page ordering ─────────────────────────────────────────────────

        /// <summary>
        /// Builds saddle-stitch sheets for a contiguous block of <paramref name="totalPages"/>
        /// pages starting at <paramref name="firstPage"/>. Standard 2-up nested ordering:
        ///   sheet k front = [last-2k, first+2k], back = [first+1+2k, last-1-2k].
        /// </summary>
        private static void BuildSaddleSheets(
            ImpositionResult result, GridGeometry grid,
            int firstPage, int totalPages, int sheetOffset, int signature,
            double creepPerSheetMm = 0)
        {
            int sheets = totalPages / 4;
            int last = firstPage + totalPages - 1;

            for (int k = 0; k < sheets; k++)
            {
                var sheet = new SheetLayout
                {
                    SheetIndex = sheetOffset + k + 1,
                    SignatureIndex = signature,
                    Back = new SheetSide { IsBack = true }
                };

                // k = 0 is the OUTERMOST sheet of the signature and needs no
                // compensation; each level deeper is pushed further toward the
                // fore-edge by the thickness of the sheets wrapped around it.
                double creep = k * creepPerSheetMm;

                int frontLeft = last - 2 * k;
                int frontRight = firstPage + 2 * k;
                int backLeft = firstPage + 1 + 2 * k;
                int backRight = last - 1 - 2 * k;

                sheet.Front.Slots.Add(SaddleSlot(frontLeft, 0, grid, creep));
                sheet.Front.Slots.Add(SaddleSlot(frontRight, 1, grid, creep));
                sheet.Back!.Slots.Add(SaddleSlot(backLeft, 0, grid, creep));
                sheet.Back!.Slots.Add(SaddleSlot(backRight, 1, grid, creep));

                result.Sheets.Add(sheet);
            }
        }

        /// <summary>
        /// Builds one saddle slot, pre-shifted toward the spine by <paramref name="creep"/>.
        /// The spine sits between column 0 (left page) and column 1 (right page), so the
        /// left page moves right and the right page moves left by the same amount.
        /// </summary>
        private static PageSlot SaddleSlot(int pageNo, int col, GridGeometry g, double creep)
        {
            var slot = SlotForPage(pageNo, col, 0, g);
            if (creep > 0)
                slot.X += col == 0 ? creep : -creep;
            return slot;
        }

        /// <summary>
        /// Creep per nesting level (mm), clamped so compensation can never push the two
        /// pages into each other across the spine.
        /// </summary>
        private static double CreepPerSheet(ImpositionInput input, GridGeometry grid, int sheetsInSignature)
        {
            double t = ToMm(input.PaperThickness, input.Unit);
            if (t <= 0 || sheetsInSignature <= 1) return 0;

            // Half the gutter is the most either page may travel before touching.
            double gutter = Math.Max(0, grid.StepX - grid.PlacedW);
            double maxTotal = gutter / 2.0;
            if (maxTotal <= 0) return 0;

            double deepest = sheetsInSignature - 1;
            return Math.Min(t, maxTotal / deepest);
        }

        // ── Grid helpers ──────────────────────────────────────────────────────────

        private sealed class GridGeometry
        {
            public int Cols, Rows;
            public double PlacedW, PlacedH, StartX, StartY, StepX, StepY;

            /// <summary>
            /// True when this grid was chosen with the source page rotated 90°.
            /// Must flow into <see cref="PageSlot.Rotation"/> — otherwise the PDF
            /// engine stretches the unrotated page into a rotated slot footprint
            /// (anamorphic distortion found in print-quality review).
            /// </summary>
            public bool Rotated;
        }

        private static GridGeometry? ChooseGrid(
            int n, double sheetW, double sheetH, double placedW, double placedH,
            Margins margin, Gutters gutter, bool allowRotation, double hf, double vf)
        {
            GridGeometry? best = null;
            double bestScore = double.MaxValue;

            foreach (var (cols, rows) in FactorPairs(n))
            {
                foreach (bool rotate in allowRotation ? new[] { false, true } : new[] { false })
                {
                    double pw = rotate ? placedH : placedW;
                    double ph = rotate ? placedW : placedH;
                    var g = TryBuild(cols, rows, sheetW, sheetH, pw, ph, margin, gutter, rotate, hf, vf);
                    if (g == null) continue;

                    // Every fitting grid of a fixed n-up covers the SAME area, so waste can't
                    // rank them. Instead: strongly prefer UPRIGHT artwork (only rotate when a
                    // non-rotated grid doesn't fit), then prefer the grid whose aspect ratio
                    // best matches the sheet (cards spread evenly / centre better).
                    double gridW = cols * pw + (cols - 1) * gutter.Horizontal;
                    double gridH = rows * ph + (rows - 1) * gutter.Vertical;
                    double aspectMismatch = Math.Abs((gridW / gridH) - (sheetW / sheetH));
                    double score = (rotate ? 1000.0 : 0.0) + aspectMismatch;
                    if (score < bestScore) { bestScore = score; best = g; }
                }
            }
            return best;
        }

        /// <summary>
        /// Per-edge sheet margins (mm). Separate edges matter because the press
        /// grippers hold one edge of the sheet, which therefore needs more clearance
        /// than the other three.
        /// </summary>
        private readonly record struct Margins(double Left, double Top, double Right, double Bottom)
        {
            public static Margins Uniform(double m) => new(m, m, m, m);
            public double Horizontal => Left + Right;
            public double Vertical => Top + Bottom;
        }

        /// <summary>Gaps between placed pages (mm): between columns and between rows.</summary>
        private readonly record struct Gutters(double Horizontal, double Vertical)
        {
            public static Gutters Uniform(double g) => new(g, g);
        }

        private static GridGeometry? TryBuild(
            int cols, int rows, double sheetW, double sheetH,
            double placedW, double placedH, Margins margin, Gutters gutter, bool rotated,
            double hf = 0.5, double vf = 0.5)
        {
            double availW = sheetW - margin.Horizontal;
            double availH = sheetH - margin.Vertical;
            double needW = cols * placedW + (cols - 1) * gutter.Horizontal;
            double needH = rows * placedH + (rows - 1) * gutter.Vertical;
            if (needW > availW + 1e-6 || needH > availH + 1e-6) return null;

            return new GridGeometry
            {
                Cols = cols,
                Rows = rows,
                PlacedW = placedW,
                PlacedH = placedH,
                StepX = placedW + gutter.Horizontal,
                StepY = placedH + gutter.Vertical,
                // hf/vf: 0 = left/top, 0.5 = centre, 1 = right/bottom of the printable area.
                StartX = margin.Left + (availW - needW) * hf,
                StartY = margin.Top + (availH - needH) * vf,
                Rotated = rotated
            };
        }

        /// <summary>Resolves the per-edge margins from the input (mm), falling back to the uniform value.</summary>
        private static Margins ResolveMargins(ImpositionInput i)
        {
            double d = ToMm(i.SheetMargin, i.Unit);
            double M(double? v) => v.HasValue ? ToMm(v.Value, i.Unit) : d;
            return new Margins(M(i.MarginLeft), M(i.MarginTop), M(i.MarginRight), M(i.MarginBottom));
        }

        /// <summary>Resolves the horizontal/vertical gutters from the input (mm).</summary>
        private static Gutters ResolveGutters(ImpositionInput i)
        {
            double d = ToMm(i.Gutter, i.Unit);
            double G(double? v) => v.HasValue ? ToMm(v.Value, i.Unit) : d;
            return new Gutters(G(i.GutterHorizontal), G(i.GutterVertical));
        }

        /// <summary>Placed page size (mm) = trimmed page plus its bleed on each axis.</summary>
        private static (double W, double H) ResolvePlacedSize(ImpositionInput i) =>
            (ToMm(i.PageWidth, i.Unit) + ResolveBleedH(i) * 2,
             ToMm(i.PageHeight, i.Unit) + ResolveBleedV(i) * 2);

        /// <summary>Re-aligns a grid so a <paramref name="cols"/>×<paramref name="rows"/> block
        /// of pages is positioned per the alignment factors (used for a partial last sheet).</summary>
        private static GridGeometry ReAlignGrid(GridGeometry g, int cols, int rows,
            double sheetW, double sheetH, Margins margin, Gutters gutter, double hf, double vf)
        {
            double availW = sheetW - margin.Horizontal, availH = sheetH - margin.Vertical;
            double boxW = cols * g.PlacedW + (cols - 1) * gutter.Horizontal;
            double boxH = rows * g.PlacedH + (rows - 1) * gutter.Vertical;
            return new GridGeometry
            {
                Cols = g.Cols, Rows = g.Rows, PlacedW = g.PlacedW, PlacedH = g.PlacedH,
                StepX = g.StepX, StepY = g.StepY, Rotated = g.Rotated,
                StartX = margin.Left + (availW - boxW) * hf,
                StartY = margin.Top + (availH - boxH) * vf,
            };
        }

        /// <summary>Maps a <see cref="SheetAlignment"/> to (horizontal, vertical) 0..1 factors.</summary>
        private static (double hf, double vf) AlignFactors(SheetAlignment a) => a switch
        {
            SheetAlignment.TopLeft => (0.0, 0.0),
            SheetAlignment.TopCentre => (0.5, 0.0),
            SheetAlignment.TopRight => (1.0, 0.0),
            SheetAlignment.Left => (0.0, 0.5),
            SheetAlignment.Right => (1.0, 0.5),
            SheetAlignment.BottomLeft => (0.0, 1.0),
            SheetAlignment.BottomCentre => (0.5, 1.0),
            SheetAlignment.BottomRight => (1.0, 1.0),
            _ => (0.5, 0.5), // Centre
        };

        /// <summary>
        /// Builds a fixed cols×rows grid, or returns null when it does not fit the
        /// sheet.
        ///
        /// This used to fall back to an un-checked grid when the pages did not fit,
        /// which silently produced a layout running off the edge of the sheet — the
        /// operator only discovered it after the paper was printed. Not fitting is
        /// now a hard failure the caller must report.
        /// </summary>
        private static GridGeometry? BuildGridGeometry(
            int cols, int rows, double sheetW, double sheetH,
            double placedW, double placedH, Margins margin, Gutters gutter)
            => TryBuild(cols, rows, sheetW, sheetH, placedW, placedH, margin, gutter, false);

        /// <summary>Factor pairs (cols, rows) whose product equals n, widest first.</summary>
        private static IEnumerable<(int cols, int rows)> FactorPairs(int n)
        {
            var pairs = new List<(int, int)>();
            for (int r = 1; r <= n; r++)
                if (n % r == 0) pairs.Add((n / r, r));
            return pairs;
        }

        private static PageSlot MakeSlot(int pageNo, int col, int row, GridGeometry g) => new()
        {
            SourcePageNumber = pageNo,
            X = g.StartX + col * g.StepX,
            Y = g.StartY + row * g.StepY,
            Width = g.PlacedW,
            Height = g.PlacedH,
            Rotation = g.Rotated ? 90 : 0
        };

        private static PageSlot SlotForPage(int pageNo, int col, int row, GridGeometry g)
            => MakeSlot(pageNo, col, row, g);

        // ── Work-and-turn / Work-and-tumble ─────────────────────────────────────

        /// <summary>
        /// Plans a single-plate sheetwise scheme.
        ///
        /// Both sides of the finished piece sit on ONE plate. The press prints the
        /// sheet, the operator flips it, and the SAME plate prints the reverse:
        ///   • turn   — flip left↔right, the gripper edge stays the same (safest);
        ///   • tumble — flip top↔bottom, the gripper moves to the opposite edge, so
        ///              the sheet must be square-trimmed at both ends.
        /// Because every sheet carries a full set on each side, one sheet yields TWO
        /// finished copies after cutting — which is the whole point of the scheme
        /// (one plate, one make-ready, half the sheets).
        /// </summary>
        private ImpositionResult PlanWorkAndTurn(ImpositionInput input, bool tumble)
        {
            // A piece has a front and a back, so the page count must be even.
            int padded = RoundUpToMultiple(input.SourcePageCount, 2);
            int padding = padded - input.SourcePageCount;

            double sheetW = ToMm(input.SheetWidth, input.Unit);
            double sheetH = ToMm(input.SheetHeight, input.Unit);
            var (placedW, placedH) = ResolvePlacedSize(input);
            var margin = ResolveMargins(input);
            var gutter = ResolveGutters(input);

            var (hf, vf) = AlignFactors(input.Alignment);
            var grid = ChooseGrid(padded, sheetW, sheetH, placedW, placedH, margin, gutter, input.AllowRotation, hf, vf);
            if (grid == null && input.ScaleToFit)
                grid = ChooseGridFitted(padded, sheetW, sheetH, placedW, placedH, margin, gutter, input.AllowRotation, hf, vf);
            if (grid == null)
                return new ImpositionResult
                {
                    IsValid = false,
                    ErrorMessage = AppLocalizer.L("ImpS_PageBiggerThanSheet")
                };

            // Every sheet is printed twice from one plate and cut into two copies.
            int sheetsRequired = (int)Math.Ceiling(Math.Max(1, input.RequiredCopies) / 2.0);

            var result = NewResult(input, sheetW, sheetH, grid);
            result.PagesPerSide = padded;
            result.SheetsRequired = sheetsRequired;
            result.IsDuplex = true;
            result.PaddingPages = padding;

            if (padding > 0)
                result.Warnings.Add(AppLocalizer.Lf("ImpS_PaddingWorkTurn", padding));
            result.Warnings.Add(AppLocalizer.L(tumble ? "ImpS_TumbleHint" : "ImpS_TurnHint"));

            for (int s = 0; s < sheetsRequired; s++)
            {
                var sheet = new SheetLayout { SheetIndex = s + 1, SignatureIndex = 1 };

                for (int idx = 0; idx < padded; idx++)
                {
                    int c = idx % grid.Cols;
                    int r = idx / grid.Cols;
                    int pageNo = idx < input.SourcePageCount ? idx + 1 : 0;   // 0 = blank padding
                    sheet.Front.Slots.Add(MakeSlot(pageNo, c, r, grid));
                }

                // The back is the SAME plate seen after the sheet is flipped, so the
                // images land mirrored about the flip axis. Showing it explicitly lets
                // the operator verify backup registration before running the job.
                sheet.Back = new SheetSide { IsBack = true };
                foreach (var f in sheet.Front.Slots)
                {
                    sheet.Back.Slots.Add(new PageSlot
                    {
                        SourcePageNumber = f.SourcePageNumber,
                        X = tumble ? f.X : sheetW - f.X - f.Width,
                        Y = tumble ? sheetH - f.Y - f.Height : f.Y,
                        Width = f.Width,
                        Height = f.Height,
                        Rotation = f.Rotation,
                    });
                }

                result.Sheets.Add(sheet);
            }

            result.UtilizationPercent = Utilization(padded, grid.PlacedW, grid.PlacedH, sheetW, sheetH);
            result.HumanReadableSummary = BuildSummary(
                result, input, AppLocalizer.L(tumble ? "ImpS_SchemeWorkTumble" : "ImpS_SchemeWorkTurn"));
            return result;
        }

        // ── Paper-cutting reuse ─────────────────────────────────────────────────

        private (int pieces, int sheets, List<CuttingPlanBlock> blocks, double sheetW, double sheetH, double util)
            OptimizeFill(ImpositionInput input, int copies)
        {
            var pcInput = new PaperCuttingInput
            {
                ProductWidth = input.PageWidth,
                ProductHeight = input.PageHeight,
                RawSheetWidth = input.SheetWidth,
                RawSheetHeight = input.SheetHeight,
                RequiredQuantity = Math.Max(1, copies),
                Bleed = input.Bleed,
                CuttingMargin = input.Gutter,
                IsBleedPerSide = true,
                IsCuttingMarginPerSide = true,
                AllowRotation = input.AllowRotation,
                Unit = input.Unit,
                OptimizationMode = PaperOptimizationMode.MaximumPieces
            };
            var r = _paperOptimizer.Calculate(pcInput);
            if (!r.IsValid || r.BestPattern == null)
                return (0, 0, new(), 0, 0, 0);
            return (r.PiecesPerSheet, r.SheetsNeeded, r.BestPattern.Blocks,
                    r.SheetWidthMm, r.SheetHeightMm, r.UtilizationPercent);
        }

        /// <summary>Expands cutting blocks (rows×cols grids) into individual placed slots (mm).</summary>
        private static IEnumerable<PageSlot> EnumerateBlockSlots(List<CuttingPlanBlock> blocks)
        {
            foreach (var b in blocks)
                for (int row = 0; row < b.Rows; row++)
                    for (int col = 0; col < b.Columns; col++)
                        yield return new PageSlot
                        {
                            X = b.X + col * b.PieceWidth,
                            Y = b.Y + row * b.PieceHeight,
                            Width = b.PieceWidth,
                            Height = b.PieceHeight,
                            Rotation = b.IsRotated ? 90 : 0
                        };
        }

        // ── Result builders ─────────────────────────────────────────────────────

        private static ImpositionResult NewResult(ImpositionInput input, double sheetW, double sheetH, GridGeometry g)
            => new()
            {
                IsValid = true,
                SheetWidthMm = sheetW,
                SheetHeightMm = sheetH,
                PlacedPageWidthMm = g.PlacedW,
                PlacedPageHeightMm = g.PlacedH,
                BleedMm = ToMm(input.Bleed, input.Unit),
                BleedHorizontalMm = ResolveBleedH(input),
                BleedVerticalMm = ResolveBleedV(input),
                Columns = g.Cols,
                Rows = g.Rows
            };

        /// <summary>Left/right bleed in mm, honouring the per-axis override.</summary>
        private static double ResolveBleedH(ImpositionInput i) =>
            i.BleedHorizontal.HasValue ? ToMm(i.BleedHorizontal.Value, i.Unit) : ToMm(i.Bleed, i.Unit);

        /// <summary>Top/bottom bleed in mm, honouring the per-axis override.</summary>
        private static double ResolveBleedV(ImpositionInput i) =>
            i.BleedVertical.HasValue ? ToMm(i.BleedVertical.Value, i.Unit) : ToMm(i.Bleed, i.Unit);

        private static ImpositionResult NewResultFromBlocks(
            ImpositionInput input, double sheetW, double sheetH, int pieces, List<CuttingPlanBlock> blocks)
        {
            var first = blocks.FirstOrDefault();
            return new ImpositionResult
            {
                IsValid = true,
                SheetWidthMm = sheetW,
                SheetHeightMm = sheetH,
                PlacedPageWidthMm = first?.PieceWidth ?? 0,
                PlacedPageHeightMm = first?.PieceHeight ?? 0,
                BleedMm = ToMm(input.Bleed, input.Unit),
                BleedHorizontalMm = ResolveBleedH(input),
                BleedVerticalMm = ResolveBleedV(input),
                PagesPerSide = pieces,
                Columns = blocks.Sum(b => b.Columns),
                Rows = blocks.Count > 0 ? blocks.Max(b => b.Rows) : 0,
                IsDuplex = false
            };
        }

        private static double Utilization(int perSheet, double placedW, double placedH, double sheetW, double sheetH)
        {
            double sheetArea = sheetW * sheetH;
            if (sheetArea <= 0) return 0;
            return Math.Min(100.0, perSheet * placedW * placedH / sheetArea * 100.0);
        }

        private static int RoundUpToMultiple(int value, int multiple)
            => multiple <= 0 ? value : (int)Math.Ceiling((double)value / multiple) * multiple;

        // ── Arabic summary ────────────────────────────────────────────────────────

        private static string BuildSummary(ImpositionResult r, ImpositionInput input, string schemeName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine(AppLocalizer.L("ImpS_SummaryTitle"));
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine(AppLocalizer.Lf("ImpS_SumScheme", schemeName));
            sb.AppendLine(AppLocalizer.Lf("ImpS_SumSourcePages", input.SourcePageCount));
            sb.AppendLine(AppLocalizer.Lf("ImpS_SumPagesPerSide", r.PagesPerSide));
            sb.AppendLine(AppLocalizer.Lf("ImpS_SumGrid", r.Columns, r.Rows));
            sb.AppendLine(AppLocalizer.Lf("ImpS_SumDuplex", r.IsDuplex ? AppLocalizer.L("ImpS_Yes") : AppLocalizer.L("ImpS_No")));
            sb.AppendLine();
            sb.AppendLine(AppLocalizer.L("ImpS_SumQuantities"));
            sb.AppendLine(AppLocalizer.Lf("ImpS_SumSheets", r.SheetsRequired));
            if (r.SignatureCount > 0)
                sb.AppendLine(AppLocalizer.Lf("ImpS_SumSignatures", r.SignatureCount));
            if (r.PaddingPages > 0)
                sb.AppendLine(AppLocalizer.Lf("ImpS_SumPadding", r.PaddingPages));
            sb.AppendLine(AppLocalizer.Lf("ImpS_SumUtilization", r.UtilizationPercent));
            if (r.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(AppLocalizer.L("ImpS_SumWarnings"));
                foreach (var w in r.Warnings) sb.AppendLine($"  ⚠ {w}");
            }
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════");
            return sb.ToString();
        }
    }
}
