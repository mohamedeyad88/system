using System;
using System.Runtime.InteropServices;

namespace Apex.Services.Printing.Color
{
    /// <summary>Which compromise the CMM makes for colours the press cannot reproduce.</summary>
    public enum IccRenderingIntent
    {
        /// <summary>Compresses the whole gamut so relationships survive — photographs.</summary>
        Perceptual = 0,

        /// <summary>Keeps in-gamut colours exact, clips the rest — logos and brand colours.</summary>
        RelativeColorimetric = 1,

        /// <summary>Maximises vividness at the cost of accuracy — charts and business graphics.</summary>
        Saturation = 2,

        /// <summary>Relative, but also reproduces the source paper white — proofing.</summary>
        AbsoluteColorimetric = 3,
    }

    /// <summary>
    /// A real ICC transform between two profiles, performed by the operating system's
    /// colour management module.
    ///
    /// Everything else in this namespace converts RGB to CMYK with a formula. A formula
    /// knows nothing about the press, the ink or the paper: FOGRA39 coated stock and
    /// uncoated newsprint get identical numbers even though they print nothing alike.
    /// Matching a customer's brand colour, or predicting what will come off the press,
    /// requires the profiles themselves — which is what this class uses.
    ///
    /// Windows only (mscms.dll), which matches the rest of the application.
    /// <see cref="Create"/> returns null rather than throwing when a profile is missing
    /// or unusable, so callers can fall back to <see cref="ColorTransformEngine"/>.
    /// </summary>
    public sealed class IccColorTransform : IDisposable
    {
        private IntPtr _transform;
        private IntPtr _sourceProfile;
        private IntPtr _destProfile;
        private bool _disposed;

        /// <summary>Path of the profile the colours are coming from.</summary>
        public string SourceProfilePath { get; }

        /// <summary>Path of the profile the colours are going to (the press).</summary>
        public string DestinationProfilePath { get; }

        public IccRenderingIntent Intent { get; }

        private IccColorTransform(
            IntPtr transform, IntPtr src, IntPtr dst,
            string srcPath, string dstPath, IccRenderingIntent intent)
        {
            _transform = transform;
            _sourceProfile = src;
            _destProfile = dst;
            SourceProfilePath = srcPath;
            DestinationProfilePath = dstPath;
            Intent = intent;
        }

        /// <summary>
        /// Builds a transform, or returns null if either profile cannot be opened or the
        /// CMM refuses the pair. Never throws: a missing profile is an ordinary condition
        /// on a customer machine, not an error worth taking the print run down for.
        /// </summary>
        public static IccColorTransform? Create(
            string sourceProfilePath,
            string destinationProfilePath,
            IccRenderingIntent intent = IccRenderingIntent.RelativeColorimetric)
        {
            if (string.IsNullOrWhiteSpace(sourceProfilePath) ||
                string.IsNullOrWhiteSpace(destinationProfilePath))
                return null;

            if (!System.IO.File.Exists(sourceProfilePath) ||
                !System.IO.File.Exists(destinationProfilePath))
                return null;

            IntPtr src = IntPtr.Zero, dst = IntPtr.Zero, transform = IntPtr.Zero;
            try
            {
                src = OpenProfile(sourceProfilePath);
                if (src == IntPtr.Zero) return null;

                dst = OpenProfile(destinationProfilePath);
                if (dst == IntPtr.Zero) { CloseColorProfile(src); return null; }

                var profiles = new[] { src, dst };
                var intents = new[] { (uint)intent };

                transform = CreateMultiProfileTransform(
                    profiles, (uint)profiles.Length,
                    intents, (uint)intents.Length,
                    BEST_MODE, INDEX_DONT_CARE);

                if (transform == IntPtr.Zero)
                {
                    CloseColorProfile(src);
                    CloseColorProfile(dst);
                    return null;
                }

                return new IccColorTransform(
                    transform, src, dst, sourceProfilePath, destinationProfilePath, intent);
            }
            catch (DllNotFoundException) { Cleanup(transform, src, dst); return null; }
            catch (EntryPointNotFoundException) { Cleanup(transform, src, dst); return null; }
        }

        private static void Cleanup(IntPtr transform, IntPtr src, IntPtr dst)
        {
            if (transform != IntPtr.Zero) DeleteColorTransform(transform);
            if (src != IntPtr.Zero) CloseColorProfile(src);
            if (dst != IntPtr.Zero) CloseColorProfile(dst);
        }

        /// <summary>
        /// Converts one sRGB triplet (0–255) to CMYK (0–255) through the profiles.
        /// Returns null if the CMM rejected the conversion.
        /// </summary>
        public (byte C, byte M, byte Y, byte K)? RgbToCmyk(byte r, byte g, byte b)
        {
            ThrowIfDisposed();

            Span<byte> rgb = stackalloc byte[3] { r, g, b };
            Span<byte> cmyk = stackalloc byte[4];

            if (!RgbToCmyk(rgb, cmyk)) return null;

            return (cmyk[0], cmyk[1], cmyk[2], cmyk[3]);
        }

        /// <summary>
        /// Converts a run of sRGB triplets. Convenience only — this is one interop call
        /// per pixel, so it suits swatches, proofs and spot checks, not full-page images.
        /// </summary>
        /// <param name="rgb">Source, 3 bytes per pixel (R,G,B).</param>
        /// <param name="cmyk">Destination, 4 bytes per pixel (C,M,Y,K).</param>
        /// <returns>False if the CMM rejected the batch; <paramref name="cmyk"/> is then untouched.</returns>
        public bool RgbToCmyk(ReadOnlySpan<byte> rgb, Span<byte> cmyk)
        {
            ThrowIfDisposed();

            int pixels = rgb.Length / 3;
            if (pixels == 0) return true;
            if (cmyk.Length < pixels * 4)
                throw new ArgumentException("Destination is too small for the pixel count.", nameof(cmyk));

            // One colour per call, deliberately.
            //
            // Passing the whole run in a single TranslateColors call is faster, and it
            // is what the signature invites — but the CMM's actual per-colour stride
            // does not match the documented COLOR union: with a multi-colour array,
            // alternate pixels came back holding their own INPUT values rather than the
            // separation. Wrong colour that still looks like a plausible colour is the
            // worst possible failure here; it would reach the press unnoticed.
            //
            // Bulk image work should move to TranslateBitmapBits, which takes the format
            // and stride explicitly instead of leaving them to be inferred.
            int stride = ColorStrideBytes;
            int bytes = stride + BufferGuardBytes;
            IntPtr inBuf = Marshal.AllocHGlobal(bytes);
            IntPtr outBuf = Marshal.AllocHGlobal(bytes);

            try
            {
                unsafe
                {
                    byte* pin = (byte*)inBuf;
                    byte* pout = (byte*)outBuf;

                    for (int i = 0; i < pixels; i++)
                    {
                        // Zero both: unwritten channels must not read as stale memory.
                        for (int z = 0; z < bytes; z++) { pin[z] = 0; pout[z] = 0; }

                        int o = i * 3;
                        ushort* src = (ushort*)pin;
                        src[0] = Expand(rgb[o]);
                        src[1] = Expand(rgb[o + 1]);
                        src[2] = Expand(rgb[o + 2]);

                        if (!TranslateColors(_transform, inBuf, 1, COLOR_RGB, outBuf, COLOR_CMYK))
                            return false;

                        ushort* dst = (ushort*)pout;
                        int q = i * 4;
                        cmyk[q] = Shrink(dst[0]);
                        cmyk[q + 1] = Shrink(dst[1]);
                        cmyk[q + 2] = Shrink(dst[2]);
                        cmyk[q + 3] = Shrink(dst[3]);
                    }
                }
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(inBuf);
                Marshal.FreeHGlobal(outBuf);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IccColorTransform));
        }

        /// <summary>
        /// Releases the CMM handles. Deliberately has NO finalizer: a finalizer would
        /// call into mscms.dll from the finalizer thread during process shutdown, when
        /// the library may already be unloaded — which takes the whole process down.
        /// Leaking three handles until exit is the lesser failure by a wide margin.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Cleanup(_transform, _sourceProfile, _destProfile);
            _transform = _sourceProfile = _destProfile = IntPtr.Zero;
        }

        // ── ICM interop ──────────────────────────────────────────────────────────

        // ICM works in 16-bit channels; our pipeline is 8-bit. 257 = 65535/255, so
        // 0→0 and 255→65535 exactly, with no drift in between.
        private const int Scale = 257;

        private static ushort Expand(byte v) => (ushort)(v * Scale);
        private static byte Shrink(ushort v) => (byte)((v + Scale / 2) / Scale);

        private static IntPtr OpenProfile(string path)
        {
            IntPtr namePtr = Marshal.StringToHGlobalUni(path);
            try
            {
                var profile = new ProfileStruct
                {
                    dwType = PROFILE_FILENAME,
                    pProfileData = namePtr,
                    cbDataSize = (uint)((path.Length + 1) * 2),
                };
                return OpenColorProfile(ref profile, PROFILE_READ, FILE_SHARE_READ, OPEN_EXISTING);
            }
            finally
            {
                Marshal.FreeHGlobal(namePtr);
            }
        }

        /// <summary>
        /// Bytes per colour in ICM's COLOR union: four 16-bit channels. This is the
        /// CMM's own stride — proven, not assumed: with any other value the CMM writes
        /// colour <c>i</c> at <c>i*8</c> while we read at <c>i*stride</c>, so only the
        /// first pixel of a batch is correct and the rest come back empty.
        /// </summary>
        private const int ColorStrideBytes = 8;

        /// <summary>
        /// Slack past the end of both buffers. The stride above is right, but a CMM that
        /// ever wrote a wider colour would otherwise corrupt the heap — and that failure
        /// surfaces as a crash somewhere else entirely, minutes later.
        /// </summary>
        private const int BufferGuardBytes = 64;

        private const uint PROFILE_FILENAME = 1;
        private const uint PROFILE_READ = 1;
        private const uint FILE_SHARE_READ = 1;
        private const uint OPEN_EXISTING = 3;
        private const uint BEST_MODE = 0x0003;
        private const uint INDEX_DONT_CARE = 0;

        private const int COLOR_RGB = 2;
        private const int COLOR_CMYK = 7;

        [StructLayout(LayoutKind.Sequential)]
        private struct ProfileStruct
        {
            public uint dwType;
            public IntPtr pProfileData;
            public uint cbDataSize;
        }

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenColorProfile(
            ref ProfileStruct pProfile, uint dwDesiredAccess, uint dwShareMode, uint dwCreationMode);

        [DllImport("mscms.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseColorProfile(IntPtr hProfile);

        [DllImport("mscms.dll", SetLastError = true)]
        private static extern IntPtr CreateMultiProfileTransform(
            IntPtr[] pahProfiles, uint nProfiles,
            uint[] padwIntent, uint nIntents,
            uint dwFlags, uint indexPreferredCMM);

        [DllImport("mscms.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteColorTransform(IntPtr hxform);

        // Raw pointers, not arrays: the marshaller cannot know the CMM's per-colour
        // stride, and getting it wrong writes past the buffer.
        [DllImport("mscms.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateColors(
            IntPtr hColorTransform,
            IntPtr paInputColors, uint nColors, int ctInput,
            IntPtr paOutputColors, int ctOutput);
    }
}
