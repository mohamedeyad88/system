using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Apex.Services.Preflight
{
    public enum PreflightSeverity { Info, Warning, Error }

    /// <summary>Which check produced a finding. The UI turns this into a localized line.</summary>
    public enum PreflightCheck
    {
        PageCount,
        PageSize,
        InconsistentSizes,
        NoBleed,
        TinyPage,
        Encrypted,
        RgbImages,
        LowResImages,
        NoImages,
        Clean,
        ImageScanSkipped
    }

    /// <summary>One line of a preflight report. <see cref="Detail"/> carries a value the
    /// UI drops into the message (a count, a size…). <see cref="Page"/> is 1-based.</summary>
    public sealed record PreflightFinding(
        PreflightSeverity Severity, PreflightCheck Check, string? Detail = null, int? Page = null);

    public sealed class PreflightReport
    {
        public int PageCount { get; init; }
        public IReadOnlyList<PreflightFinding> Findings { get; init; } = Array.Empty<PreflightFinding>();
        public int Errors => Findings.Count(f => f.Severity == PreflightSeverity.Error);
        public int Warnings => Findings.Count(f => f.Severity == PreflightSeverity.Warning);
        /// <summary>Pass = nothing that would ruin a print run (no errors).</summary>
        public bool Passed => Errors == 0;
    }

    /// <summary>
    /// Checks a customer's PDF for the things that ruin a print run before it starts:
    /// mixed page sizes, missing bleed, tiny/zero pages, password protection, and
    /// RGB / low-resolution images. Reliable checks (sizes, boxes, encryption) run on
    /// PdfSharpCore metadata; the image scan is best-effort and never fails the report.
    /// </summary>
    public sealed class PreflightService
    {
        private const double PtToMm = 25.4 / 72.0;

        /// <summary>Flag images whose smaller pixel dimension is below this as "check resolution".</summary>
        public int LowResPixelThreshold { get; init; } = 300;

        public PreflightReport Check(byte[] pdf)
        {
            if (pdf == null || pdf.Length == 0)
                return new PreflightReport { Findings = new[] { new PreflightFinding(PreflightSeverity.Error, PreflightCheck.Encrypted) } };

            PdfDocument doc;
            try
            {
                doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly);
            }
            catch (Exception)
            {
                // Password-protected or otherwise unreadable — the press cannot use it.
                return new PreflightReport { Findings = new[] { new PreflightFinding(PreflightSeverity.Error, PreflightCheck.Encrypted) } };
            }

            var findings = new List<PreflightFinding>();
            int pages = doc.PageCount;
            findings.Add(new PreflightFinding(PreflightSeverity.Info, PreflightCheck.PageCount, pages.ToString()));

            if (doc.SecuritySettings.DocumentSecurityLevel != PdfSharpCore.Pdf.Security.PdfDocumentSecurityLevel.None)
                findings.Add(new PreflightFinding(PreflightSeverity.Error, PreflightCheck.Encrypted));

            // ── Page sizes + bleed ────────────────────────────────────────────
            var sizes = new HashSet<string>();
            int noBleed = 0;
            for (int i = 0; i < pages; i++)
            {
                var page = doc.Pages[i];
                double wmm = Math.Round(page.Width.Point * PtToMm);
                double hmm = Math.Round(page.Height.Point * PtToMm);
                sizes.Add($"{wmm:0}×{hmm:0}");

                if (wmm < 5 || hmm < 5)
                    findings.Add(new PreflightFinding(PreflightSeverity.Error, PreflightCheck.TinyPage, $"{wmm:0}×{hmm:0}", i + 1));

                if (!HasBleed(page)) noBleed++;
            }

            var firstSize = sizes.FirstOrDefault();
            if (firstSize != null)
                findings.Add(new PreflightFinding(PreflightSeverity.Info, PreflightCheck.PageSize, firstSize));
            if (sizes.Count > 1)
                findings.Add(new PreflightFinding(PreflightSeverity.Warning, PreflightCheck.InconsistentSizes, string.Join(" ، ", sizes)));
            if (noBleed > 0)
                findings.Add(new PreflightFinding(PreflightSeverity.Warning, PreflightCheck.NoBleed, $"{noBleed}/{pages}"));

            // ── Images (best-effort — never fails the report) ─────────────────
            try
            {
                int images = 0, rgb = 0, lowRes = 0;
                for (int i = 0; i < pages; i++)
                    ScanImages(doc.Pages[i], ref images, ref rgb, ref lowRes);

                if (images == 0)
                    findings.Add(new PreflightFinding(PreflightSeverity.Info, PreflightCheck.NoImages));
                if (rgb > 0)
                    findings.Add(new PreflightFinding(PreflightSeverity.Warning, PreflightCheck.RgbImages, rgb.ToString()));
                if (lowRes > 0)
                    findings.Add(new PreflightFinding(PreflightSeverity.Warning, PreflightCheck.LowResImages, lowRes.ToString()));
            }
            catch (Exception)
            {
                findings.Add(new PreflightFinding(PreflightSeverity.Info, PreflightCheck.ImageScanSkipped));
            }

            if (findings.All(f => f.Severity == PreflightSeverity.Info))
                findings.Add(new PreflightFinding(PreflightSeverity.Info, PreflightCheck.Clean));

            return new PreflightReport { PageCount = pages, Findings = findings };
        }

        /// <summary>A page has real bleed when its BleedBox is larger than its trim/media box.</summary>
        private static bool HasBleed(PdfPage page)
        {
            if (!page.Elements.ContainsKey("/BleedBox")) return false;
            var bleed = page.BleedBox;
            var trim = page.Elements.ContainsKey("/TrimBox") ? page.TrimBox : page.MediaBox;
            return bleed.Width > trim.Width + 0.5 || bleed.Height > trim.Height + 0.5;
        }

        private void ScanImages(PdfPage page, ref int images, ref int rgb, ref int lowRes)
        {
            var res = page.Elements.GetDictionary("/Resources");
            var xobjects = res?.Elements.GetDictionary("/XObject");
            if (xobjects == null) return;

            foreach (var key in xobjects.Elements.Keys.ToList())
            {
                var xo = xobjects.Elements.GetDictionary(key);
                if (xo == null) continue;
                if (xo.Elements.GetName("/Subtype") != "/Image") continue;

                images++;
                int w = xo.Elements.GetInteger("/Width");
                int h = xo.Elements.GetInteger("/Height");
                if (w > 0 && h > 0 && Math.Min(w, h) < LowResPixelThreshold) lowRes++;

                var cs = ColorSpaceName(xo.Elements.GetObject("/ColorSpace"));
                if (cs.IndexOf("RGB", StringComparison.OrdinalIgnoreCase) >= 0) rgb++;
            }
        }

        private static string ColorSpaceName(PdfItem? cs)
        {
            if (cs is PdfReference r) cs = r.Value;
            if (cs is PdfName n) return n.Value;
            if (cs is PdfArray a && a.Elements.Count > 0)
            {
                var first = a.Elements[0];
                if (first is PdfReference fr) first = fr.Value;
                if (first is PdfName fn)
                {
                    // ICCBased streams carry an /N component count: 3 = RGB, 4 = CMYK.
                    if (fn.Value == "/ICCBased" && a.Elements.Count > 1)
                    {
                        var streamItem = a.Elements[1];
                        if (streamItem is PdfReference sr) streamItem = sr.Value;
                        if (streamItem is PdfDictionary sd)
                        {
                            int nComp = sd.Elements.GetInteger("/N");
                            if (nComp == 3) return "RGB";
                            if (nComp == 4) return "CMYK";
                        }
                    }
                    return fn.Value;
                }
            }
            return "";
        }
    }
}
