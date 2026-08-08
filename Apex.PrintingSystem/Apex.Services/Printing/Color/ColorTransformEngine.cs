using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Apex.Services.Printing.Color
{
    /// <summary>
    /// Provides static color-space transformation utilities:
    /// RGB ↔ CIELab, RGB/Lab → CMYK (GCR), bitmap simulation, and CIEDE2000.
    /// </summary>
    public static class ColorTransformEngine
    {
        // ── D50 white point ───────────────────────────────────────────────────
        private const double Xn = 0.9642;
        private const double Yn = 1.0000;
        private const double Zn = 0.8251;

        // ── sRGB → XYZ (D50) matrix ──────────────────────────────────────────
        // Bradford-adapted sRGB primaries to D50
        private const double M_RX = 0.4360747, M_RY = 0.3850649, M_RZ = 0.1430804;
        private const double M_GX = 0.2225045, M_GY = 0.7168786, M_GZ = 0.0606169;
        private const double M_BX = 0.0139322, M_BY = 0.0971045, M_BZ = 0.7141733;

        // ── XYZ → sRGB (D50 inverse) matrix ─────────────────────────────────
        // Row-major inverse of the above 3×3
        private const double INV_XX = 3.1338561, INV_XY = -1.6168667, INV_XZ = -0.4906146;
        private const double INV_YX = -0.9787684, INV_YY = 1.9161415, INV_YZ = 0.0334540;
        private const double INV_ZX = 0.0719453, INV_ZY = -0.2289914, INV_ZZ = 1.4052427;

        // ═════════════════════════════════════════════════════════════════════
        // 1. RGB → CIELab
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Converts an sRGB triplet (0–255) to CIELab using the D50 illuminant.</summary>
        public static (double L, double a, double b) RgbToLab(byte r, byte g, byte b)
        {
            // Step 1 – linearise sRGB
            double rLin = Linearise(r / 255.0);
            double gLin = Linearise(g / 255.0);
            double bLin = Linearise(b / 255.0);

            // Step 2 – sRGB → XYZ (D50)
            double X = M_RX * rLin + M_RY * gLin + M_RZ * bLin;
            double Y = M_GX * rLin + M_GY * gLin + M_GZ * bLin;
            double Z = M_BX * rLin + M_BY * gLin + M_BZ * bLin;

            // Step 3 – XYZ → Lab
            double fx = LabF(X / Xn);
            double fy = LabF(Y / Yn);
            double fz = LabF(Z / Zn);

            double L = 116.0 * fy - 16.0;
            double a = 500.0 * (fx - fy);
            double bStar = 200.0 * (fy - fz);

            return (L, a, bStar);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Linearise(double c) =>
            c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double LabF(double t) =>
            t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;

        // ═════════════════════════════════════════════════════════════════════
        // 2. CIELab → CMYK
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Converts a CIELab colour to CMYK (0–255 each) using the default
        /// separation settings.
        /// </summary>
        public static (byte C, byte M, byte Y, byte K) LabToCmyk(double L, double a, double b)
            => LabToCmyk(L, a, b, CmykSeparationSettings.Default);

        /// <summary>
        /// Converts a CIELab colour to CMYK (0–255 each) using GCR and the
        /// total-area-coverage limit of <paramref name="settings"/>.
        /// </summary>
        public static (byte C, byte M, byte Y, byte K) LabToCmyk(
            double L, double a, double b, CmykSeparationSettings settings)
        {
            // Lab → XYZ (D50)
            double fy = (L + 16.0) / 116.0;
            double fx = a / 500.0 + fy;
            double fz = fy - b / 200.0;

            double X = Xn * LabFInv(fx);
            double Y = Yn * LabFInv(fy);
            double Z = Zn * LabFInv(fz);

            // XYZ → linear RGB (D50)
            double rLin = INV_XX * X + INV_XY * Y + INV_XZ * Z;
            double gLin = INV_YX * X + INV_YY * Y + INV_YZ * Z;
            double bLin = INV_ZX * X + INV_ZY * Y + INV_ZZ * Z;

            // Clamp linear values before gamma
            rLin = Math.Max(0.0, Math.Min(1.0, rLin));
            gLin = Math.Max(0.0, Math.Min(1.0, gLin));
            bLin = Math.Max(0.0, Math.Min(1.0, bLin));

            // Gamma-encode back to sRGB [0,1]
            double R = GammaEncode(rLin);
            double G = GammaEncode(gLin);
            double Bv = GammaEncode(bLin);

            return RgbDoublesToCmyk(R, G, Bv, settings);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double LabFInv(double t)
        {
            double t3 = t * t * t;
            return t3 > 0.008856 ? t3 : (t - 16.0 / 116.0) / 7.787;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double GammaEncode(double c) =>
            c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;

        // ═════════════════════════════════════════════════════════════════════
        // 3. RGB → CMYK  (via Lab intermediate)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Converts sRGB (0–255) to CMYK (0–255) using Lab as an intermediate,
        /// with the default separation settings.
        /// </summary>
        public static (byte C, byte M, byte Y, byte K) RgbToCmyk(byte r, byte g, byte b)
            => RgbToCmyk(r, g, b, CmykSeparationSettings.Default);

        /// <summary>
        /// Converts sRGB (0–255) to CMYK (0–255) for a specific press/stock.
        /// </summary>
        public static (byte C, byte M, byte Y, byte K) RgbToCmyk(
            byte r, byte g, byte b, CmykSeparationSettings settings)
        {
            var (L, a, lab_b) = RgbToLab(r, g, b);
            return LabToCmyk(L, a, lab_b, settings);
        }

        // ── Shared GCR logic ─────────────────────────────────────────────────

        /// <summary>
        /// Grey-component replacement followed by an ENFORCED total-ink limit.
        ///
        /// The previous version multiplied black by 0.85 and called that a 320% cap.
        /// It is the opposite: shrinking K pushes ink back into C, M and Y, and pure
        /// black separated to C100 M100 Y100 K85 — 385% total. No press or paper holds
        /// that; the sheet never dries and the run is scrap. Nothing here may exceed
        /// <see cref="CmykSeparationSettings.TotalAreaCoverage"/>.
        /// </summary>
        private static (byte C, byte M, byte Y, byte K) RgbDoublesToCmyk(
            double R, double G, double B, CmykSeparationSettings settings)
        {
            settings ??= CmykSeparationSettings.Default;

            double cRaw = 1.0 - R;
            double mRaw = 1.0 - G;
            double yRaw = 1.0 - B;

            // The neutral component present in all three inks — the part black can replace.
            double neutral = Math.Min(cRaw, Math.Min(mRaw, yRaw));

            double start = Clamp01(settings.BlackStart);
            double kMax = Clamp01(settings.BlackMax);
            double gcr = Clamp01(settings.GcrAmount);

            // Black ramps in from BlackStart so light greys stay built from CMY and
            // do not break into visible black dots.
            double k = neutral <= start || start >= 1.0
                ? 0.0
                : (neutral - start) / (1.0 - start) * kMax * gcr;

            k = Math.Min(k, kMax);

            double c, m, y;
            if (k >= 1.0)
            {
                // Fully replaced: solid black is K alone, not four inks.
                c = m = y = 0.0;
                k = 1.0;
            }
            else
            {
                double denom = 1.0 - k;
                c = (cRaw - k) / denom;
                m = (mRaw - k) / denom;
                y = (yRaw - k) / denom;
            }

            c = Clamp01(c); m = Clamp01(m); y = Clamp01(y); k = Clamp01(k);

            // Enforce the ink limit. Black carries the density and is cheapest, so the
            // chromatic inks give way first; only if K alone still exceeds the limit
            // does K itself get capped.
            double tac = Math.Max(0.0, settings.TotalAreaCoverage);
            double total = c + m + y + k;
            if (total > tac)
            {
                double cmy = c + m + y;
                double allowedCmy = tac - k;

                if (allowedCmy <= 0.0)
                {
                    c = m = y = 0.0;
                    k = Math.Min(k, tac);
                }
                else if (cmy > 0.0)
                {
                    double scale = allowedCmy / cmy;
                    c *= scale; m *= scale; y *= scale;
                }
            }

            return (ToByte(c), ToByte(m), ToByte(y), ToByte(k));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ToByte(double v) =>
            (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, v)) * 255.0);

        // ═════════════════════════════════════════════════════════════════════
        // 4. Bitmap CMYK simulation
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a new bitmap that visually simulates the CMYK round-trip of
        /// the source image (gamut-compression visible on-screen).
        /// Uses LockBits + unsafe pointer arithmetic for performance.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static unsafe Bitmap ApplyRgbToCmykSimulation(Bitmap source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            int width = source.Width;
            int height = source.Height;

            var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            // Lock source (read)
            var srcRect = new Rectangle(0, 0, width, height);
            var srcData = source.LockBits(srcRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(srcRect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                int stride = srcData.Stride;
                byte* srcPtr = (byte*)srcData.Scan0;
                byte* dstPtr = (byte*)dstData.Scan0;
                int pixelBytes = 3; // 24bpp

                for (int y = 0; y < height; y++)
                {
                    byte* srcRow = srcPtr + y * stride;
                    byte* dstRow = dstPtr + y * dstData.Stride;

                    for (int x = 0; x < width; x++)
                    {
                        int offset = x * pixelBytes;
                        // GDI+ 24bpp stores BGR
                        byte bv = srcRow[offset];
                        byte gv = srcRow[offset + 1];
                        byte rv = srcRow[offset + 2];

                        var (C, M, Y, K) = RgbToCmyk(rv, gv, bv);

                        // Convert CMYK back to RGB for on-screen display
                        double kFrac = K / 255.0;
                        double rOut = 255.0 * (1.0 - C / 255.0) * (1.0 - kFrac);
                        double gOut = 255.0 * (1.0 - M / 255.0) * (1.0 - kFrac);
                        double bOut = 255.0 * (1.0 - Y / 255.0) * (1.0 - kFrac);

                        dstRow[offset] = (byte)Math.Round(Math.Max(0, Math.Min(255, bOut)));
                        dstRow[offset + 1] = (byte)Math.Round(Math.Max(0, Math.Min(255, gOut)));
                        dstRow[offset + 2] = (byte)Math.Round(Math.Max(0, Math.Min(255, rOut)));
                    }
                }
            }
            finally
            {
                source.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }

            return result;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 5. CIEDE2000 colour difference
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Computes the CIEDE2000 perceptual colour difference between two Lab values.
        /// kL = kC = kH = 1 (standard parametric factors).
        /// </summary>
        public static double DeltaE2000(
            (double L, double a, double b) lab1,
            (double L, double a, double b) lab2)
        {
            const double kL = 1.0, kC = 1.0, kH = 1.0;
            const double Deg360 = 2.0 * Math.PI;

            double L1 = lab1.L, a1 = lab1.a, b1 = lab1.b;
            double L2 = lab2.L, a2 = lab2.a, b2 = lab2.b;

            // Step 1 – C'ab and a' adjustments
            double C1ab = Math.Sqrt(a1 * a1 + b1 * b1);
            double C2ab = Math.Sqrt(a2 * a2 + b2 * b2);
            double CabAvg = (C1ab + C2ab) / 2.0;
            double CabAvg7 = Math.Pow(CabAvg, 7.0);
            double G = 0.5 * (1.0 - Math.Sqrt(CabAvg7 / (CabAvg7 + 6103515625.0))); // 25^7

            double a1p = a1 * (1.0 + G);
            double a2p = a2 * (1.0 + G);

            double C1p = Math.Sqrt(a1p * a1p + b1 * b1);
            double C2p = Math.Sqrt(a2p * a2p + b2 * b2);

            // Step 2 – h'
            double h1p = Atan2Deg360(b1, a1p);
            double h2p = Atan2Deg360(b2, a2p);

            // Step 3 – ΔL', ΔC', ΔH'
            double dLp = L2 - L1;
            double dCp = C2p - C1p;

            double dhp;
            if (C1p * C2p == 0.0)
            {
                dhp = 0.0;
            }
            else
            {
                double diff = h2p - h1p;
                if (diff > Math.PI) dhp = diff - Deg360;
                else if (diff < -Math.PI) dhp = diff + Deg360;
                else dhp = diff;
            }

            double dHp = 2.0 * Math.Sqrt(C1p * C2p) * Math.Sin(dhp / 2.0);

            // Step 4 – CIEDE2000 weighting functions
            double Lp_avg = (L1 + L2) / 2.0;
            double Cp_avg = (C1p + C2p) / 2.0;

            double hp_avg;
            if (C1p * C2p == 0.0)
            {
                hp_avg = h1p + h2p;
            }
            else
            {
                double sumH = h1p + h2p;
                double diffH = Math.Abs(h1p - h2p);
                if (diffH <= Math.PI) hp_avg = sumH / 2.0;
                else if (sumH < Deg360) hp_avg = (sumH + Deg360) / 2.0;
                else hp_avg = (sumH - Deg360) / 2.0;
            }

            double T = 1.0
                - 0.17 * Math.Cos(hp_avg - DegToRad(30.0))
                + 0.24 * Math.Cos(2.0 * hp_avg)
                + 0.32 * Math.Cos(3.0 * hp_avg + DegToRad(6.0))
                - 0.20 * Math.Cos(4.0 * hp_avg - DegToRad(63.0));

            double Lp50 = Lp_avg - 50.0;
            double Lp50sq = Lp50 * Lp50;
            double SL = 1.0 + 0.015 * Lp50sq / Math.Sqrt(20.0 + Lp50sq);
            double SC = 1.0 + 0.045 * Cp_avg;
            double SH = 1.0 + 0.015 * Cp_avg * T;

            double Cp_avg7 = Math.Pow(Cp_avg, 7.0);
            double RC = 2.0 * Math.Sqrt(Cp_avg7 / (Cp_avg7 + 6103515625.0));

            double dTheta = DegToRad(30.0) * Math.Exp(-Math.Pow((RadToDeg(hp_avg) - 275.0) / 25.0, 2.0));
            double RT = -Math.Sin(2.0 * dTheta) * RC;

            double term1 = dLp / (kL * SL);
            double term2 = dCp / (kC * SC);
            double term3 = dHp / (kH * SH);

            double dE = Math.Sqrt(
                term1 * term1
                + term2 * term2
                + term3 * term3
                + RT * term2 * term3);

            return dE;
        }

        // ── Math helpers ─────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Atan2Deg360(double y, double x)
        {
            double a = Math.Atan2(y, x);
            if (a < 0) a += 2.0 * Math.PI;
            return a; // radians in [0, 2π)
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;
    }
}
