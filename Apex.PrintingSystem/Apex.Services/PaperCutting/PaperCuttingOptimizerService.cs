using Apex.Core.Models.PaperCutting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Apex.Services.PaperCutting
{
    /// <summary>
    /// Core paper-cutting optimizer.  Evaluates up to five cutting patterns and
    /// selects the best one according to the requested <see cref="PaperOptimizationMode"/>.
    ///
    /// Patterns:
    ///   A – Normal orientation only
    ///   B – Rotated 90° (only when AllowRotation = true)
    ///   C – Normal primary block + rotated fill in the right-side waste strip
    ///   D – Normal primary block + rotated fill in the bottom waste strip
    ///   E – Rotated primary block + normal fill in the waste areas
    /// </summary>
    public class PaperCuttingOptimizerService
    {
        // ─── Unit conversion ────────────────────────────────────────────────────────

        private static double ToMm(double value, MeasurementUnit unit) => unit switch
        {
            MeasurementUnit.Centimeter => value * 10.0,
            MeasurementUnit.Inch => value * 25.4,
            _ => value           // mm
        };

        // ─── Public entry point ─────────────────────────────────────────────────────

        public PaperCuttingResult Calculate(PaperCuttingInput input)
        {
            var validation = Validate(input);
            if (!string.IsNullOrEmpty(validation))
                return new PaperCuttingResult { IsValid = false, ErrorMessage = validation };

            // Convert everything to millimetres
            double sheetW = ToMm(input.RawSheetWidth, input.Unit);
            double sheetH = ToMm(input.RawSheetHeight, input.Unit);
            double prodW = ToMm(input.ProductWidth, input.Unit);
            double prodH = ToMm(input.ProductHeight, input.Unit);
            double bleed = ToMm(input.Bleed, input.Unit);
            double margin = ToMm(input.CuttingMargin, input.Unit);

            // Effective piece size (product + bleed on both sides)
            double bleedTotal = input.IsBleedPerSide ? bleed * 2 : bleed;
            double marginStep = input.IsCuttingMarginPerSide ? margin : margin / 2.0;

            double effW = prodW + bleedTotal;
            double effH = prodH + bleedTotal;

            // The step between piece origins includes the cutting gutter on one side
            double stepW = effW + marginStep;
            double stepH = effH + marginStep;
            double stepWr = effH + marginStep;  // rotated
            double stepHr = effW + marginStep;

            // Evaluate patterns
            var patterns = new List<CuttingPatternResult>();
            patterns.Add(BuildPatternA(sheetW, sheetH, effW, effH, stepW, stepH));

            if (input.AllowRotation)
            {
                patterns.Add(BuildPatternB(sheetW, sheetH, effW, effH, stepWr, stepHr));
                patterns.Add(BuildPatternC(sheetW, sheetH, effW, effH, stepW, stepH, stepWr, stepHr));
                patterns.Add(BuildPatternD(sheetW, sheetH, effW, effH, stepW, stepH, stepWr, stepHr));
                patterns.Add(BuildPatternE(sheetW, sheetH, effW, effH, stepW, stepH, stepWr, stepHr));
            }

            // Remove zero-piece patterns
            patterns = patterns.Where(p => p.Pieces > 0).ToList();
            if (!patterns.Any())
                return new PaperCuttingResult
                {
                    IsValid = false,
                    ErrorMessage = "المنتج أكبر من الورقة الخام. لا يمكن قطع أي قطعة."
                };

            // Annotate area stats
            double sheetArea = sheetW * sheetH;
            foreach (var p in patterns)
            {
                p.UsedArea = p.Pieces * effW * effH;
                p.WasteArea = sheetArea - p.UsedArea;
                p.UtilizationPercentage = p.UsedArea / sheetArea * 100.0;
            }

            // Select best
            var best = SelectBest(patterns, input.OptimizationMode);

            // Build cutting instructions for best
            best.Instructions = BuildInstructions(best, sheetW, sheetH);

            // Quantities
            int qty = input.RequiredQuantity;
            int sheetsNeeded = (int)Math.Ceiling((double)qty / best.Pieces);
            double wasteFactor = 1.0 + input.ProductionWastePercentage / 100.0;
            int sheetsWithWaste = (int)Math.Ceiling(sheetsNeeded * wasteFactor);
            int totalProduced = sheetsWithWaste * best.Pieces;

            var result = new PaperCuttingResult
            {
                IsValid = true,
                BestPattern = best,
                AllPatterns = patterns,
                PiecesPerSheet = best.Pieces,
                SheetsNeeded = sheetsNeeded,
                SheetsWithWaste = sheetsWithWaste,
                TotalPiecesProduced = totalProduced,
                ExtraPieces = totalProduced - qty,
                EffectiveProductWidth = effW,
                EffectiveProductHeight = effH,
                SheetWidthMm = sheetW,
                SheetHeightMm = sheetH,
                TotalSheetArea = sheetArea,
                UsedArea = best.UsedArea,
                WasteArea = best.WasteArea,
                UtilizationPercent = best.UtilizationPercentage,
                RequiredQuantity = qty,
                ProductionWastePercentage = input.ProductionWastePercentage
            };
            result.HumanReadableSummary = BuildArabicSummary(result, input);
            return result;
        }

        // ─── Multi-product entry point (separate mode) ───────────────────────────────

        /// <summary>
        /// Optimizes several products in one job. In this (separate) mode each product is
        /// cut on its own raw sheets using the shared sheet / pre-press / mode settings in
        /// <paramref name="sharedTemplate"/>; the per-product dimensions and quantity come
        /// from each <see cref="ProductSpec"/>. Results are rolled up into grand totals.
        /// </summary>
        /// <param name="products">The products to plan (must contain at least one).</param>
        /// <param name="sharedTemplate">
        /// Carries the shared inputs: raw sheet size, unit, bleed, margin, rotation,
        /// production-waste %, and optimization mode. Its product fields are ignored.
        /// </param>
        public MultiProductResult CalculateMulti(
            IReadOnlyList<ProductSpec> products,
            PaperCuttingInput sharedTemplate)
        {
            if (products == null || products.Count == 0)
                return new MultiProductResult
                {
                    IsValid = false,
                    ErrorMessage = "لا توجد منتجات في القائمة. أضف منتجاً واحداً على الأقل."
                };

            var multi = new MultiProductResult { IsValid = true, ProductCount = products.Count };

            for (int idx = 0; idx < products.Count; idx++)
            {
                var p = products[idx];

                // Per-product input = shared template with this product's dimensions/qty.
                var input = new PaperCuttingInput
                {
                    ProductWidth = p.Width,
                    ProductHeight = p.Height,
                    RequiredQuantity = p.Quantity,
                    RawSheetWidth = sharedTemplate.RawSheetWidth,
                    RawSheetHeight = sharedTemplate.RawSheetHeight,
                    ProductionWastePercentage = sharedTemplate.ProductionWastePercentage,
                    Bleed = sharedTemplate.Bleed,
                    CuttingMargin = sharedTemplate.CuttingMargin,
                    IsBleedPerSide = sharedTemplate.IsBleedPerSide,
                    IsCuttingMarginPerSide = sharedTemplate.IsCuttingMarginPerSide,
                    AllowRotation = sharedTemplate.AllowRotation,
                    Unit = sharedTemplate.Unit,
                    OptimizationMode = sharedTemplate.OptimizationMode
                };

                var result = Calculate(input);

                if (!result.IsValid)
                {
                    string label = string.IsNullOrWhiteSpace(p.Name) ? $"المنتج {idx + 1}" : p.Name;
                    return new MultiProductResult
                    {
                        IsValid = false,
                        ErrorMessage = $"خطأ في «{label}»: {result.ErrorMessage}"
                    };
                }

                multi.Items.Add(new ProductPlanItem { Product = p, Result = result });

                // Roll up grand totals
                multi.TotalRequiredQuantity += result.RequiredQuantity;
                multi.TotalSheetsNet += result.SheetsNeeded;
                multi.TotalSheetsWithWaste += result.SheetsWithWaste;
                multi.TotalPiecesProduced += result.TotalPiecesProduced;
                multi.TotalSheetArea += result.TotalSheetArea * result.SheetsWithWaste;
                multi.TotalUsedArea += result.UsedArea * result.SheetsWithWaste;
            }

            multi.TotalWasteArea = multi.TotalSheetArea - multi.TotalUsedArea;
            multi.CombinedUtilizationPercent = multi.TotalSheetArea > 0
                ? multi.TotalUsedArea / multi.TotalSheetArea * 100.0
                : 0;
            multi.HumanReadableSummary = BuildMultiArabicSummary(multi, sharedTemplate);

            return multi;
        }

        // ─── Validation (9 rules) ────────────────────────────────────────────────────

        private static string Validate(PaperCuttingInput i)
        {
            if (i.ProductWidth <= 0) return "عرض المنتج يجب أن يكون أكبر من صفر.";
            if (i.ProductHeight <= 0) return "ارتفاع المنتج يجب أن يكون أكبر من صفر.";
            if (i.RawSheetWidth <= 0) return "عرض الورقة الخام يجب أن يكون أكبر من صفر.";
            if (i.RawSheetHeight <= 0) return "ارتفاع الورقة الخام يجب أن يكون أكبر من صفر.";
            if (i.RequiredQuantity <= 0) return "الكمية المطلوبة يجب أن تكون أكبر من صفر.";
            if (i.ProductionWastePercentage < 0 || i.ProductionWastePercentage > 50)
                return "نسبة الهدر يجب أن تكون بين 0 و 50.";
            if (i.Bleed < 0) return "قيمة النزيف لا يمكن أن تكون سالبة.";
            if (i.CuttingMargin < 0) return "هامش القطع لا يمكن أن يكون سالباً.";

            double bleed = ToMm(i.Bleed, i.Unit);
            double margin = ToMm(i.CuttingMargin, i.Unit);
            double pw = ToMm(i.ProductWidth, i.Unit) + (i.IsBleedPerSide ? bleed * 2 : bleed);
            double ph = ToMm(i.ProductHeight, i.Unit) + (i.IsBleedPerSide ? bleed * 2 : bleed);
            double sw = ToMm(i.RawSheetWidth, i.Unit);
            double sh = ToMm(i.RawSheetHeight, i.Unit);

            if (pw > sw && ph > sh && pw > sh && ph > sw)
                return "المنتج (بعد إضافة النزيف) أكبر من الورقة الخام في كلا الاتجاهين.";

            return string.Empty;
        }

        // ─── Pattern builders ────────────────────────────────────────────────────────

        /// Pattern A: Normal orientation, simple grid.
        private static CuttingPatternResult BuildPatternA(
            double sw, double sh,
            double effW, double effH,
            double stepW, double stepH)
        {
            int cols = (int)Math.Floor((sw + 1e-9) / stepW);
            int rows = (int)Math.Floor((sh + 1e-9) / stepH);
            cols = Math.Max(cols, 0);
            rows = Math.Max(rows, 0);
            int pieces = cols * rows;

            var block = new CuttingPlanBlock
            {
                X = 0,
                Y = 0,
                Width = cols * stepW - (stepW - effW),
                Height = rows * stepH - (stepH - effH),
                PieceWidth = effW,
                PieceHeight = effH,
                Columns = cols,
                Rows = rows,
                IsRotated = false,
                BlockName = "النمط الأساسي"
            };

            return new CuttingPatternResult
            {
                PatternName = "A",
                Pieces = pieces,
                IsMixed = false,
                IsRotated = false,
                ComplexityScore = 1,
                Blocks = pieces > 0 ? new List<CuttingPlanBlock> { block } : new()
            };
        }

        /// Pattern B: 90° rotated.
        private static CuttingPatternResult BuildPatternB(
            double sw, double sh,
            double effW, double effH,
            double stepWr, double stepHr)
        {
            int cols = (int)Math.Floor((sw + 1e-9) / stepWr);
            int rows = (int)Math.Floor((sh + 1e-9) / stepHr);
            cols = Math.Max(cols, 0);
            rows = Math.Max(rows, 0);
            int pieces = cols * rows;

            var block = new CuttingPlanBlock
            {
                X = 0,
                Y = 0,
                Width = cols * stepWr - (stepWr - effH),
                Height = rows * stepHr - (stepHr - effW),
                PieceWidth = effH,
                PieceHeight = effW,
                Columns = cols,
                Rows = rows,
                IsRotated = true,
                BlockName = "النمط المدوّر"
            };

            return new CuttingPatternResult
            {
                PatternName = "B",
                Pieces = pieces,
                IsMixed = false,
                IsRotated = true,
                ComplexityScore = 1,
                Blocks = pieces > 0 ? new List<CuttingPlanBlock> { block } : new()
            };
        }

        /// Pattern C: Normal primary block + rotated fill in right-side waste strip.
        private static CuttingPatternResult BuildPatternC(
            double sw, double sh,
            double effW, double effH,
            double stepW, double stepH,
            double stepWr, double stepHr)
        {
            int colsA = (int)Math.Floor((sw + 1e-9) / stepW);
            int rowsA = (int)Math.Floor((sh + 1e-9) / stepH);
            colsA = Math.Max(colsA, 0); rowsA = Math.Max(rowsA, 0);

            double usedW = colsA > 0 ? colsA * stepW - (stepW - effW) : 0;
            double rightWaste = sw - usedW;

            int colsFill = (int)Math.Floor((rightWaste + 1e-9) / stepWr);
            int rowsFill = (int)Math.Floor((sh + 1e-9) / stepHr);
            colsFill = Math.Max(colsFill, 0); rowsFill = Math.Max(rowsFill, 0);

            int total = colsA * rowsA + colsFill * rowsFill;

            var blocks = new List<CuttingPlanBlock>();
            if (colsA > 0 && rowsA > 0)
                blocks.Add(new CuttingPlanBlock
                {
                    X = 0,
                    Y = 0,
                    Width = usedW,
                    Height = rowsA * stepH - (stepH - effH),
                    PieceWidth = effW,
                    PieceHeight = effH,
                    Columns = colsA,
                    Rows = rowsA,
                    IsRotated = false,
                    BlockName = "النمط الأساسي"
                });
            if (colsFill > 0 && rowsFill > 0)
                blocks.Add(new CuttingPlanBlock
                {
                    X = usedW,
                    Y = 0,
                    Width = colsFill * stepWr - (stepWr - effH),
                    Height = rowsFill * stepHr - (stepHr - effW),
                    PieceWidth = effH,
                    PieceHeight = effW,
                    Columns = colsFill,
                    Rows = rowsFill,
                    IsRotated = true,
                    BlockName = "تعبئة يمين"
                });

            return new CuttingPatternResult
            {
                PatternName = "C",
                Pieces = total,
                IsMixed = colsA * rowsA > 0 && colsFill * rowsFill > 0,
                IsRotated = false,
                ComplexityScore = 2,
                Blocks = blocks
            };
        }

        /// Pattern D: Normal primary block + rotated fill in bottom waste strip.
        private static CuttingPatternResult BuildPatternD(
            double sw, double sh,
            double effW, double effH,
            double stepW, double stepH,
            double stepWr, double stepHr)
        {
            int colsA = (int)Math.Floor((sw + 1e-9) / stepW);
            int rowsA = (int)Math.Floor((sh + 1e-9) / stepH);
            colsA = Math.Max(colsA, 0); rowsA = Math.Max(rowsA, 0);

            double usedH = rowsA > 0 ? rowsA * stepH - (stepH - effH) : 0;
            double botWaste = sh - usedH;

            int colsFill = (int)Math.Floor((sw + 1e-9) / stepWr);
            int rowsFill = (int)Math.Floor((botWaste + 1e-9) / stepHr);
            colsFill = Math.Max(colsFill, 0); rowsFill = Math.Max(rowsFill, 0);

            int total = colsA * rowsA + colsFill * rowsFill;

            var blocks = new List<CuttingPlanBlock>();
            if (colsA > 0 && rowsA > 0)
                blocks.Add(new CuttingPlanBlock
                {
                    X = 0,
                    Y = 0,
                    Width = colsA * stepW - (stepW - effW),
                    Height = usedH,
                    PieceWidth = effW,
                    PieceHeight = effH,
                    Columns = colsA,
                    Rows = rowsA,
                    IsRotated = false,
                    BlockName = "النمط الأساسي"
                });
            if (colsFill > 0 && rowsFill > 0)
                blocks.Add(new CuttingPlanBlock
                {
                    X = 0,
                    Y = usedH,
                    Width = colsFill * stepWr - (stepWr - effH),
                    Height = rowsFill * stepHr - (stepHr - effW),
                    PieceWidth = effH,
                    PieceHeight = effW,
                    Columns = colsFill,
                    Rows = rowsFill,
                    IsRotated = true,
                    BlockName = "تعبئة أسفل"
                });

            return new CuttingPatternResult
            {
                PatternName = "D",
                Pieces = total,
                IsMixed = colsA * rowsA > 0 && colsFill * rowsFill > 0,
                IsRotated = false,
                ComplexityScore = 2,
                Blocks = blocks
            };
        }

        /// Pattern E: Rotated primary + normal fill in waste areas.
        private static CuttingPatternResult BuildPatternE(
            double sw, double sh,
            double effW, double effH,
            double stepW, double stepH,
            double stepWr, double stepHr)
        {
            // Primary: rotated
            int colsB = (int)Math.Floor((sw + 1e-9) / stepWr);
            int rowsB = (int)Math.Floor((sh + 1e-9) / stepHr);
            colsB = Math.Max(colsB, 0); rowsB = Math.Max(rowsB, 0);

            double usedW = colsB > 0 ? colsB * stepWr - (stepWr - effH) : 0;
            double rightWaste = sw - usedW;

            // Fill right waste with normal orientation
            int colsFill = (int)Math.Floor((rightWaste + 1e-9) / stepW);
            int rowsFill = (int)Math.Floor((sh + 1e-9) / stepH);
            colsFill = Math.Max(colsFill, 0); rowsFill = Math.Max(rowsFill, 0);

            int total = colsB * rowsB + colsFill * rowsFill;

            var blocks = new List<CuttingPlanBlock>();
            if (colsB > 0 && rowsB > 0)
                blocks.Add(new CuttingPlanBlock
                {
                    X = 0,
                    Y = 0,
                    Width = usedW,
                    Height = rowsB * stepHr - (stepHr - effW),
                    PieceWidth = effH,
                    PieceHeight = effW,
                    Columns = colsB,
                    Rows = rowsB,
                    IsRotated = true,
                    BlockName = "النمط المدوّر الأساسي"
                });
            if (colsFill > 0 && rowsFill > 0)
                blocks.Add(new CuttingPlanBlock
                {
                    X = usedW,
                    Y = 0,
                    Width = colsFill * stepW - (stepW - effW),
                    Height = rowsFill * stepH - (stepH - effH),
                    PieceWidth = effW,
                    PieceHeight = effH,
                    Columns = colsFill,
                    Rows = rowsFill,
                    IsRotated = false,
                    BlockName = "تعبئة عادية"
                });

            return new CuttingPatternResult
            {
                PatternName = "E",
                Pieces = total,
                IsMixed = colsB * rowsB > 0 && colsFill * rowsFill > 0,
                IsRotated = true,
                ComplexityScore = 2,
                Blocks = blocks
            };
        }

        // ─── Pattern selection ───────────────────────────────────────────────────────

        private static CuttingPatternResult SelectBest(
            List<CuttingPatternResult> patterns,
            PaperOptimizationMode mode)
        {
            return mode switch
            {
                PaperOptimizationMode.MaximumPieces =>
                    patterns.OrderByDescending(p => p.Pieces)
                            .ThenBy(p => p.ComplexityScore)
                            .First(),

                PaperOptimizationMode.MinimumWaste =>
                    patterns.OrderByDescending(p => p.UtilizationPercentage)
                            .ThenByDescending(p => p.Pieces)
                            .First(),

                PaperOptimizationMode.BestUtilization =>
                    patterns.OrderByDescending(p => p.UtilizationPercentage)
                            .ThenByDescending(p => p.Pieces)
                            .First(),

                PaperOptimizationMode.SimpleCutFirst =>
                    patterns.OrderBy(p => p.ComplexityScore)
                            .ThenByDescending(p => p.Pieces)
                            .First(),

                _ => patterns.OrderByDescending(p => p.Pieces).First()
            };
        }

        // ─── Cutting instructions ────────────────────────────────────────────────────

        private static List<CuttingInstruction> BuildInstructions(
            CuttingPatternResult pattern, double sheetW, double sheetH)
        {
            var instructions = new List<CuttingInstruction>();
            int stepNum = 1;

            foreach (var block in pattern.Blocks)
            {
                double pieceW = block.PieceWidth;
                double pieceH = block.PieceHeight;

                // Vertical cuts (divide columns)
                if (block.Columns > 1)
                {
                    var cuts = new List<double>();
                    double wasteV = sheetW - (block.X + block.Columns * pieceW);
                    for (int c = 1; c < block.Columns; c++)
                        cuts.Add(block.X + c * pieceW);

                    instructions.Add(new CuttingInstruction
                    {
                        Axis = 'V',
                        TotalLength = sheetH,
                        CutSegments = cuts,
                        WasteLength = wasteV > 0 ? wasteV : 0,
                        Description = $"الخطوة {stepNum++}: قطع رأسي — {block.Columns} عمود في {block.BlockName}"
                    });
                }

                // Horizontal cuts (divide rows)
                if (block.Rows > 1)
                {
                    var cuts = new List<double>();
                    double wasteH = sheetH - (block.Y + block.Rows * pieceH);
                    for (int r = 1; r < block.Rows; r++)
                        cuts.Add(block.Y + r * pieceH);

                    instructions.Add(new CuttingInstruction
                    {
                        Axis = 'H',
                        TotalLength = sheetW,
                        CutSegments = cuts,
                        WasteLength = wasteH > 0 ? wasteH : 0,
                        Description = $"الخطوة {stepNum++}: قطع أفقي — {block.Rows} صف في {block.BlockName}"
                    });
                }
            }

            // If multiple blocks, add separator cut between them
            if (pattern.Blocks.Count >= 2)
            {
                var b0 = pattern.Blocks[0];
                var b1 = pattern.Blocks[1];
                // Determine if blocks are side-by-side or stacked
                if (Math.Abs(b1.X - (b0.X + b0.Width)) < 1.0)
                {
                    instructions.Insert(0, new CuttingInstruction
                    {
                        Axis = 'V',
                        TotalLength = sheetH,
                        CutSegments = new List<double> { b0.X + b0.Width },
                        WasteLength = 0,
                        Description = $"الخطوة 0: قطع فصل رأسي بين {b0.BlockName} و {b1.BlockName}"
                    });
                }
                else if (Math.Abs(b1.Y - (b0.Y + b0.Height)) < 1.0)
                {
                    instructions.Insert(0, new CuttingInstruction
                    {
                        Axis = 'H',
                        TotalLength = sheetW,
                        CutSegments = new List<double> { b0.Y + b0.Height },
                        WasteLength = 0,
                        Description = $"الخطوة 0: قطع فصل أفقي بين {b0.BlockName} و {b1.BlockName}"
                    });
                }
            }

            return instructions;
        }

        // ─── Human-readable Arabic summary ──────────────────────────────────────────

        private static string BuildArabicSummary(PaperCuttingResult r, PaperCuttingInput input)
        {
            var sb = new StringBuilder();
            var bp = r.BestPattern!;

            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("       نتيجة حاسبة قص الورق الذكية");
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"▸ النمط المختار : النمط {bp.PatternName}" +
                          (bp.IsMixed ? " (مختلط)" : bp.IsRotated ? " (مدوّر)" : " (عادي)"));
            sb.AppendLine($"▸ القطع لكل ورقة: {r.PiecesPerSheet} قطعة");
            sb.AppendLine();
            sb.AppendLine("── الكميات ──────────────────────────────");
            sb.AppendLine($"  الكمية المطلوبة   : {r.RequiredQuantity} قطعة");
            sb.AppendLine($"  أوراق بدون هدر    : {r.SheetsNeeded} ورقة");
            if (r.ProductionWastePercentage > 0)
            {
                sb.AppendLine($"  نسبة هدر الإنتاج  : {r.ProductionWastePercentage}%");
                sb.AppendLine($"  أوراق مع الهدر    : {r.SheetsWithWaste} ورقة");
            }
            sb.AppendLine($"  إجمالي الإنتاج    : {r.TotalPiecesProduced} قطعة");
            sb.AppendLine($"  قطع زائدة         : {r.ExtraPieces} قطعة");
            sb.AppendLine();
            sb.AppendLine("── تحليل المساحة ────────────────────────");
            sb.AppendLine($"  نسبة الاستخدام    : {r.UtilizationPercent:F1}%");
            sb.AppendLine($"  الهدر             : {r.WasteArea:F0} مم²");
            sb.AppendLine();
            sb.AppendLine("── تعليمات القطع ────────────────────────");

            int step = 1;
            foreach (var instr in bp.Instructions)
            {
                string axis = instr.Axis == 'V' ? "رأسي" : "أفقي";
                sb.AppendLine($"  {step++}. {instr.Description}");
                sb.AppendLine($"     عدد القطعات: {instr.CutSegments.Count}");
            }

            if (bp.Blocks.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("── تفاصيل المناطق ───────────────────────");
                foreach (var blk in bp.Blocks)
                {
                    sb.AppendLine($"  {blk.BlockName}: {blk.Columns}×{blk.Rows} = {blk.PiecesCount} قطعة" +
                                  (blk.IsRotated ? " (مدوّرة)" : ""));
                }
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════");
            return sb.ToString();
        }

        // ─── Multi-product Arabic summary ────────────────────────────────────────────

        private static string BuildMultiArabicSummary(MultiProductResult m, PaperCuttingInput input)
        {
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("     نتيجة قص الورق — منتجات متعددة");
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"▸ عدد المنتجات     : {m.ProductCount}");
            sb.AppendLine($"▸ نمط الورقة الخام : {input.RawSheetWidth} × {input.RawSheetHeight}");
            sb.AppendLine();

            int n = 1;
            foreach (var item in m.Items)
            {
                var p = item.Product;
                var r = item.Result;
                string name = string.IsNullOrWhiteSpace(p.Name) ? $"المنتج {n}" : p.Name;

                sb.AppendLine($"── ({n}) {name} ──────────────────────────");
                sb.AppendLine($"   المقاس            : {p.Width} × {p.Height}");
                sb.AppendLine($"   الكمية المطلوبة   : {p.Quantity} قطعة");
                sb.AppendLine($"   النمط المختار     : النمط {r.BestPattern?.PatternName}");
                sb.AppendLine($"   القطع لكل ورقة    : {r.PiecesPerSheet} قطعة");
                sb.AppendLine($"   أوراق (مع الهدر)  : {r.SheetsWithWaste} ورقة");
                sb.AppendLine($"   نسبة الاستخدام    : {r.UtilizationPercent:F1}%");
                sb.AppendLine();
                n++;
            }

            sb.AppendLine("══ الإجمالي الكلي ═════════════════════════");
            sb.AppendLine($"  إجمالي الكمية المطلوبة : {m.TotalRequiredQuantity} قطعة");
            sb.AppendLine($"  إجمالي الأوراق (صافي)  : {m.TotalSheetsNet} ورقة");
            sb.AppendLine($"  إجمالي الأوراق (هدر)   : {m.TotalSheetsWithWaste} ورقة");
            sb.AppendLine($"  إجمالي الإنتاج         : {m.TotalPiecesProduced} قطعة");
            sb.AppendLine($"  الاستخدام المجمّع      : {m.CombinedUtilizationPercent:F1}%");
            sb.AppendLine($"  إجمالي الهدر           : {m.TotalWasteArea:F0} مم²");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════");
            return sb.ToString();
        }
    }
}
