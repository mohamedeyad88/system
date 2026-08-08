using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace Apex.Services.Printing.Color
{
    /// <summary>Which route a colour actually took to CMYK.</summary>
    public enum ColorPath
    {
        /// <summary>An ICC profile Windows associates with this specific printer.</summary>
        PrinterProfile,

        /// <summary>The shop's standard press profile, because the printer has none.</summary>
        ShopDefaultProfile,

        /// <summary>No usable profile: the built-in separation with the press ink limit.</summary>
        Formula,
    }

    /// <summary>What will be used for a given printer, and why.</summary>
    public sealed record ColorResolution(
        ColorPath Path,
        string? ProfilePath,
        string Reason);

    /// <summary>
    /// Decides how RGB becomes CMYK for a given printer.
    ///
    /// "Convert to CMYK" is not a complete instruction — CMYK belongs to a device. The
    /// same red prints differently on an inkjet, on coated offset and on newsprint, and
    /// the ICC profile is what encodes that difference. This service answers the missing
    /// half of the question, in a fixed order of preference, and reports which answer it
    /// used so the operator can see it rather than guess.
    /// </summary>
    public sealed class ColorManagementService
    {
        private static readonly Lazy<ColorManagementService> _instance =
            new(() => new ColorManagementService(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public static ColorManagementService Instance => _instance.Value;

        private static readonly string ColorFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                         "spool", "drivers", "color");

        // Transforms are expensive to build and are safe to reuse for the session.
        private readonly ConcurrentDictionary<string, IccColorTransform?> _transforms =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, IccLookupTable?> _lookups =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Creates an independent configuration. <see cref="Instance"/> is the shop-wide
        /// one; a separate instance is useful when a single job runs to a different
        /// stock than the house standard.
        /// </summary>
        public ColorManagementService()
        {
            SourceRgbProfilePath = FindFirst(
                "sRGB Color Space Profile.icm", "sRGB.icm", "sRGB Color Space Profile.ICM");

            DefaultPressProfilePath = FindFirst(
                // ISO coated — the standard commercial work is delivered against across
                // Europe and the Middle East. The alternatives are regional equivalents.
                "CoatedFOGRA39.icc",
                "CoatedFOGRA27.icc",
                "EuroscaleCoated.icc",
                "CoatedGRACoL2006.icc",
                "USWebCoatedSWOP.icc",
                "RSWOP.icm");
        }

        /// <summary>The space untagged RGB artwork is assumed to be in (sRGB).</summary>
        public string? SourceRgbProfilePath { get; set; }

        /// <summary>
        /// The press profile used when a printer has none of its own. Settable so a shop
        /// printing on uncoated or newsprint can point it at their own standard.
        /// </summary>
        public string? DefaultPressProfilePath { get; set; }

        /// <summary>Ink limits and black generation for the formula fallback.</summary>
        public CmykSeparationSettings Separation { get; set; } = CmykSeparationSettings.Default;

        public IccRenderingIntent Intent { get; set; } = IccRenderingIntent.RelativeColorimetric;

        /// <summary>
        /// Reports what would be used for <paramref name="printerName"/> without converting
        /// anything — so the UI can show the operator which profile is in play before a
        /// run rather than after it.
        /// </summary>
        public ColorResolution Resolve(string? printerName)
        {
            if (string.IsNullOrWhiteSpace(SourceRgbProfilePath) || !File.Exists(SourceRgbProfilePath))
                return new ColorResolution(ColorPath.Formula, null,
                    "No sRGB source profile on this machine.");

            var printerProfile = FindPrinterProfile(printerName);
            if (printerProfile != null)
                return new ColorResolution(ColorPath.PrinterProfile, printerProfile,
                    $"Profile installed for '{printerName}'.");

            if (!string.IsNullOrWhiteSpace(DefaultPressProfilePath) && File.Exists(DefaultPressProfilePath))
                return new ColorResolution(ColorPath.ShopDefaultProfile, DefaultPressProfilePath,
                    $"No profile for '{printerName}'; using the shop press standard.");

            return new ColorResolution(ColorPath.Formula, null,
                "No press profile available; using the built-in separation.");
        }

        /// <summary>
        /// Converts one sRGB colour to CMYK for the given printer, taking the best route
        /// available. Never throws and never returns nothing: a colour-managed run is
        /// better than an unmanaged one, and an unmanaged one is better than no print.
        /// </summary>
        public (byte C, byte M, byte Y, byte K) RgbToCmyk(byte r, byte g, byte b, string? printerName)
        {
            var resolution = Resolve(printerName);

            if (resolution.Path != ColorPath.Formula && resolution.ProfilePath != null)
            {
                var transform = GetTransform(resolution.ProfilePath);
                var ink = transform?.RgbToCmyk(r, g, b);
                if (ink.HasValue) return ink.Value;
                // Profile turned out unusable — fall through rather than fail the job.
            }

            return ColorTransformEngine.RgbToCmyk(r, g, b, Separation);
        }

        /// <summary>
        /// A fast, profile-accurate converter for image work, or null when no profile
        /// applies. Build it once per job: per-pixel CMM calls are roughly 14× slower,
        /// which on a full page is the difference between a second and a quarter of a
        /// minute — slow enough that colour management stops being used at all.
        /// </summary>
        public IccLookupTable? CreateLookup(string? printerName)
        {
            var resolution = Resolve(printerName);
            if (resolution.Path == ColorPath.Formula || resolution.ProfilePath == null)
                return null;

            string key = $"lut|{SourceRgbProfilePath}|{resolution.ProfilePath}|{Intent}";
            return _lookups.GetOrAdd(key, _ =>
            {
                var transform = GetTransform(resolution.ProfilePath);
                return transform == null ? null : IccLookupTable.Build(transform);
            });
        }

        private IccColorTransform? GetTransform(string destinationProfilePath)
        {
            string key = $"{SourceRgbProfilePath}|{destinationProfilePath}|{Intent}";
            return _transforms.GetOrAdd(key, _ =>
                IccColorTransform.Create(SourceRgbProfilePath!, destinationProfilePath, Intent));
        }

        /// <summary>
        /// The profile Windows associates with this printer, or null.
        ///
        /// Deliberately returns null rather than "some other printer's profile": applying
        /// a different device's characterisation is worse than applying none, because it
        /// is wrong with confidence and nothing on screen says so.
        /// </summary>
        private static string? FindPrinterProfile(string? printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return null;

            try
            {
                var profile = IccProfileManager.Instance
                    .GetAllPrinterProfiles()
                    .FirstOrDefault(p => IsForPrinter(p, printerName));

                return profile?.FilePath;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsForPrinter(IccProfile profile, string printerName)
        {
            // Match on the distinctive words of the printer name — "EPSON WF-C5210 Series"
            // should find an Epson WF-C5210 profile, but must not match on "Series".
            var words = printerName.Split(new[] { ' ', '-', '_', '(', ')' },
                                          StringSplitOptions.RemoveEmptyEntries)
                                   .Where(w => w.Length >= 4
                                            && !w.Equals("Series", StringComparison.OrdinalIgnoreCase)
                                            && !w.Equals("Printer", StringComparison.OrdinalIgnoreCase))
                                   .ToArray();
            if (words.Length == 0) return false;

            string haystack = profile.ProfileName + " " + Path.GetFileNameWithoutExtension(profile.FilePath);

            // Every distinctive word must appear: a single common word is a coincidence,
            // not an identification.
            return words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase));
        }

        private static string? FindFirst(params string[] fileNames)
        {
            foreach (var name in fileNames)
            {
                var path = Path.Combine(ColorFolder, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }
}
