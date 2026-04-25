using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace Apex.Services.Printing.Color
{
    /// <summary>Soft-proofing mode controlling how the renderer processes each pixel.</summary>
    public enum ProofMode
    {
        /// <summary>No proof — return source image unchanged.</summary>
        Disabled,

        /// <summary>Simulate printer gamut by performing an RGB→CMYK→RGB round-trip.</summary>
        PrinterSimulation,

        /// <summary>Paint pixels whose DeltaE &gt; 6.0 in magenta to highlight gamut violations.</summary>
        GamutWarning
    }

    /// <summary>Result produced by <see cref="SoftProofRenderer.Render"/>.</summary>
    public class SoftProofResult
    {
        public Bitmap ProofedImage         { get; init; } = null!;
        public int    OutOfGamutPixelCount { get; init; }
        public double OutOfGamutPercent    { get; init; }
        public double AverageDeltaE        { get; init; }
        public string SummaryArabic        { get; init; } = "";
    }

    /// <summary>
    /// Simulates on-screen how an image will look when printed, optionally
    /// highlighting colours that fall outside the printer's gamut.
    /// </summary>
    public class SoftProofRenderer
    {
        // Thresholds
        private const double NoticeableDeltaE = 3.0;
        private const double GamutWarnDeltaE  = 6.0;

        // Gamut-warning highlight colour (magenta)
        private const byte WarningR = 255, WarningG = 0, WarningB = 255;

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Renders a soft proof of <paramref name="source"/> using the given
        /// <paramref name="mode"/>.  The optional <paramref name="printerProfile"/>
        /// is accepted for future profile-guided transforms; the current
        /// implementation uses the built-in GCR-based transform.
        /// </summary>
        public SoftProofResult Render(
            Bitmap      source,
            ProofMode   mode,
            IccProfile? printerProfile = null)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            return mode switch
            {
                ProofMode.Disabled          => RenderDisabled(source),
                ProofMode.PrinterSimulation => RenderSimulation(source, gamutWarning: false),
                ProofMode.GamutWarning      => RenderSimulation(source, gamutWarning: true),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }

        // ── Mode: Disabled ────────────────────────────────────────────────────

        private static SoftProofResult RenderDisabled(Bitmap source)
        {
            var copy = new Bitmap(source);
            return new SoftProofResult
            {
                ProofedImage         = copy,
                OutOfGamutPixelCount = 0,
                OutOfGamutPercent    = 0.0,
                AverageDeltaE        = 0.0,
                SummaryArabic        = BuildSummary(0.0)
            };
        }

        // ── Mode: PrinterSimulation / GamutWarning ────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static unsafe SoftProofResult RenderSimulation(Bitmap source, bool gamutWarning)
        {
            int width  = source.Width;
            int height = source.Height;
            int total  = width * height;

            var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var rect   = new Rectangle(0, 0, width, height);

            var srcData = source.LockBits(rect, ImageLockMode.ReadOnly,  PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            long   outOfGamutCount = 0;
            double totalDeltaE     = 0.0;

            try
            {
                byte* srcPtr = (byte*)srcData.Scan0;
                byte* dstPtr = (byte*)dstData.Scan0;

                for (int y = 0; y < height; y++)
                {
                    byte* srcRow = srcPtr + y * srcData.Stride;
                    byte* dstRow = dstPtr + y * dstData.Stride;

                    for (int x = 0; x < width; x++)
                    {
                        int offset = x * 3;

                        // GDI+ 24bpp = BGR storage
                        byte bOrig = srcRow[offset];
                        byte gOrig = srcRow[offset + 1];
                        byte rOrig = srcRow[offset + 2];

                        // RGB → Lab (original)
                        var labOrig = ColorTransformEngine.RgbToLab(rOrig, gOrig, bOrig);

                        // RGB → CMYK → RGB  (gamut-compressed)
                        var (C, M, Y, K) = ColorTransformEngine.RgbToCmyk(rOrig, gOrig, bOrig);
                        double kFrac = K / 255.0;
                        double rSim  = 255.0 * (1.0 - C / 255.0) * (1.0 - kFrac);
                        double gSim  = 255.0 * (1.0 - M / 255.0) * (1.0 - kFrac);
                        double bSim  = 255.0 * (1.0 - Y / 255.0) * (1.0 - kFrac);

                        byte rSimB = ClampByte(rSim);
                        byte gSimB = ClampByte(gSim);
                        byte bSimB = ClampByte(bSim);

                        // Lab of simulated pixel
                        var labSim = ColorTransformEngine.RgbToLab(rSimB, gSimB, bSimB);

                        // Perceptual difference
                        double dE = ColorTransformEngine.DeltaE2000(labOrig, labSim);
                        totalDeltaE += dE;

                        bool isOutOfGamut = dE > NoticeableDeltaE;
                        if (isOutOfGamut) outOfGamutCount++;

                        if (gamutWarning && dE > GamutWarnDeltaE)
                        {
                            // Paint gamut-warning magenta
                            dstRow[offset]     = WarningB;
                            dstRow[offset + 1] = WarningG;
                            dstRow[offset + 2] = WarningR;
                        }
                        else
                        {
                            // Show gamut-compressed colour
                            dstRow[offset]     = bSimB;
                            dstRow[offset + 1] = gSimB;
                            dstRow[offset + 2] = rSimB;
                        }
                    }
                }
            }
            finally
            {
                source.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }

            double outOfGamutPct = total > 0 ? 100.0 * outOfGamutCount / total : 0.0;
            double avgDeltaE     = total > 0 ? totalDeltaE / total : 0.0;

            return new SoftProofResult
            {
                ProofedImage         = result,
                OutOfGamutPixelCount = (int)outOfGamutCount,
                OutOfGamutPercent    = outOfGamutPct,
                AverageDeltaE        = avgDeltaE,
                SummaryArabic        = BuildSummary(outOfGamutPct)
            };
        }

        // ── Arabic summary ────────────────────────────────────────────────────

        private static string BuildSummary(double outOfGamutPercent)
        {
            if (outOfGamutPercent <= 0.0)
                return "الألوان ضمن نطاق الطابعة ✅";
            if (outOfGamutPercent < 5.0)
                return "انتباه: نسبة صغيرة من الألوان خارج النطاق";
            if (outOfGamutPercent < 20.0)
                return "تحذير: بعض الألوان ستظهر مختلفة في الطباعة";
            return "خطر: نسبة كبيرة من الألوان خارج نطاق الطابعة، يُنصح بمراجعة التصميم";
        }

        // ── Helper ────────────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ClampByte(double v) =>
            (byte)Math.Round(Math.Max(0.0, Math.Min(255.0, v)));
    }
}
