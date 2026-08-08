using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using Apex.Services.Printing;

namespace Apex.Services.Numbering
{
    public record TrayVerificationResult(bool Success, string? ErrorMessage, IReadOnlyList<string> Errors)
    {
        public static TrayVerificationResult Ok() => new(true, null, new List<string>());
        public static TrayVerificationResult Fail(IEnumerable<string> errors)
        {
            var list = errors.ToList();
            return new TrayVerificationResult(false, string.Join("; ", list), list);
        }
    }

    /// <summary>
    /// Verifies required trays are available before starting a cycle.
    /// </summary>
    public class TrayVerificationService : ITrayVerificationService
    {
        private readonly PrinterTrayDetectionService _trayDetector = new();

        /// <summary>
        /// Verify that all trays in mapping exist for the given printer.
        /// (Paper level availability is not exposed by the current APIs).
        /// </summary>
        public TrayVerificationResult Verify(string printerName, Dictionary<int, PaperSourceKind> trayMapping)
        {
            var errors = new List<string>();

            var available = _trayDetector.GetAvailableTrays(printerName);
            var availableKinds = available.Select(t => t.Kind).ToHashSet();

            foreach (var kvp in trayMapping)
            {
                if (!availableKinds.Contains(kvp.Value))
                {
                    errors.Add($"Tray for copy index {kvp.Key} (kind {kvp.Value}) not found on printer '{printerName}'.");
                }
            }

            if (errors.Count > 0)
                return TrayVerificationResult.Fail(errors);

            return TrayVerificationResult.Ok();
        }
    }
}

