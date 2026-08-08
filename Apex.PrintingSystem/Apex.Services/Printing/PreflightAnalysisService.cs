using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Apex.Services.Printing.VendorDetection;

namespace Apex.Services.Printing
{
    // ══════════════════════════════════════════════════════════════════════════
    // PREFLIGHT SEVERITY
    // ══════════════════════════════════════════════════════════════════════════

    public enum PreflightSeverity { Pass, Info, Warning, Error, Critical }

    // ══════════════════════════════════════════════════════════════════════════
    // PREFLIGHT ITEM — one finding
    // ══════════════════════════════════════════════════════════════════════════

    public class PreflightItem
    {
        public PreflightSeverity Severity { get; init; }
        public string Category { get; init; } = "";   // "File", "Image", "Font", "Color", "Printer"
        public string Message { get; init; } = "";   // English/Arabic message
        public string? Suggestion { get; init; }         // How to fix
        public bool BlocksPrint { get; init; }         // Critical = blocks print

        public override string ToString() =>
            $"[{Severity}] {Category}: {Message}" +
            (Suggestion != null ? $" → {Suggestion}" : "");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PREFLIGHT REPORT — full analysis result
    // ══════════════════════════════════════════════════════════════════════════

    public class PreflightReport
    {
        public string FilePath { get; init; } = "";
        public string? PrinterName { get; init; }
        public DateTime AnalyzedAt { get; init; } = DateTime.Now;
        public TimeSpan AnalysisDuration { get; init; }

        public List<PreflightItem> Items { get; init; } = new();

        // ── Convenience accessors ─────────────────────────────────────────
        public bool CanPrint => !Items.Any(i => i.BlocksPrint);
        public bool HasWarnings => Items.Any(i => i.Severity == PreflightSeverity.Warning);
        public bool HasErrors => Items.Any(i => i.Severity >= PreflightSeverity.Error);
        public int ErrorCount => Items.Count(i => i.Severity >= PreflightSeverity.Error);
        public int WarningCount => Items.Count(i => i.Severity == PreflightSeverity.Warning);

        // ── Summary ───────────────────────────────────────────────────────
        public PreflightSeverity OverallSeverity =>
            Items.Count == 0 ? PreflightSeverity.Pass :
            Items.Any(i => i.Severity == PreflightSeverity.Critical) ? PreflightSeverity.Critical :
            Items.Any(i => i.Severity == PreflightSeverity.Error) ? PreflightSeverity.Error :
            Items.Any(i => i.Severity == PreflightSeverity.Warning) ? PreflightSeverity.Warning :
            Items.Any(i => i.Severity == PreflightSeverity.Info) ? PreflightSeverity.Info :
            PreflightSeverity.Pass;

        public string SummaryArabic =>
            OverallSeverity switch
            {
                PreflightSeverity.Pass => "✅ الملف جاهز للطباعة",
                PreflightSeverity.Info => $"ℹ️ جاهز مع {Items.Count} ملاحظة",
                PreflightSeverity.Warning => $"⚠️ {WarningCount} تحذير — يُنصح بالمراجعة",
                PreflightSeverity.Error => $"❌ {ErrorCount} خطأ — يُنصح بالإصلاح",
                PreflightSeverity.Critical => "🚫 لا يمكن الطباعة — يرجى إصلاح الأخطاء الحرجة",
                _ => "غير معروف"
            };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PREFLIGHT ANALYSIS SERVICE
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔍 PRE-FLIGHT ANALYSIS SERVICE
    ///
    /// Validates a print file before it reaches the printer.
    /// Catches common problems early: low resolution, missing files,
    /// incompatible formats, oversized files, unsupported features.
    ///
    /// Usage:
    ///   var report = await PreflightAnalysisService.Instance.AnalyzeAsync(filePath, printerName);
    ///   if (!report.CanPrint) { show report.SummaryArabic; return; }
    /// </summary>
    public sealed class PreflightAnalysisService
    {
        // ── Singleton ─────────────────────────────────────────────────────
        private static readonly Lazy<PreflightAnalysisService> _instance =
            new(() => new PreflightAnalysisService());
        public static PreflightAnalysisService Instance => _instance.Value;

        private PreflightAnalysisService() { }

        // ── Configuration ─────────────────────────────────────────────────
        private const long MaxFileSizeBytes = 500 * 1024 * 1024; // 500 MB
        private const long WarnFileSizeBytes = 100 * 1024 * 1024; // 100 MB
        private const int MinRecommendedDpi = 150;
        private const int MinAcceptableDpi = 72;
        private const long MaxPageCount = 2000;
        private const long WarnPageCount = 500;

        // ── Supported extensions ──────────────────────────────────────────
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif",
            ".txt", ".csv", ".rtf", ".doc", ".docx", ".xls", ".xlsx"
        };

        private static readonly HashSet<string> NativeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif", ".txt"
        };

        // ── Main API ──────────────────────────────────────────────────────

        /// <summary>
        /// Analyze a file for print readiness. Optionally validates against a specific printer.
        /// </summary>
        public async Task<PreflightReport> AnalyzeAsync(
            string filePath,
            string? printerName = null,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var items = new List<PreflightItem>();

            await Task.Run(async () =>
            {
                // ── 1. File existence & accessibility ───────────────────
                CheckFileAccessibility(filePath, items);
                if (items.Any(i => i.BlocksPrint))
                    return; // No point continuing if file can't be opened

                var fileInfo = new FileInfo(filePath);
                var ext = fileInfo.Extension.ToLowerInvariant();

                // ── 2. File format ──────────────────────────────────────
                CheckFileFormat(ext, items);

                // ── 3. File size ────────────────────────────────────────
                CheckFileSize(fileInfo.Length, items);

                // ── 4. Format-specific checks ───────────────────────────
                if (ext == ".pdf")
                    await CheckPdfAsync(filePath, items, cancellationToken);
                else if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".tif")
                    CheckImage(filePath, items);
                else if (ext is ".doc" or ".docx" or ".xls" or ".xlsx")
                    CheckOfficeDocument(filePath, items);

                // ── 5. Printer compatibility ────────────────────────────
                if (printerName != null)
                    await CheckPrinterCompatibilityAsync(filePath, ext, printerName, items, cancellationToken);

            }, cancellationToken);

            sw.Stop();

            return new PreflightReport
            {
                FilePath = filePath,
                PrinterName = printerName,
                AnalyzedAt = DateTime.Now,
                AnalysisDuration = sw.Elapsed,
                Items = items
            };
        }

        // ── Check methods ─────────────────────────────────────────────────

        private static void CheckFileAccessibility(string filePath, List<PreflightItem> items)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Critical,
                    Category = "File",
                    Message = "مسار الملف فارغ",
                    BlocksPrint = true
                });
                return;
            }

            if (!File.Exists(filePath))
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Critical,
                    Category = "File",
                    Message = $"الملف غير موجود: {Path.GetFileName(filePath)}",
                    Suggestion = "تأكد من مسار الملف وحاول مرة أخرى",
                    BlocksPrint = true
                });
                return;
            }

            try
            {
                using var _ = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Pass,
                    Category = "File",
                    Message = "الملف موجود ويمكن الوصول إليه"
                });
            }
            catch (UnauthorizedAccessException)
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Critical,
                    Category = "File",
                    Message = "لا توجد صلاحية لفتح الملف",
                    Suggestion = "تحقق من صلاحيات الملف",
                    BlocksPrint = true
                });
            }
            catch (IOException ex)
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Error,
                    Category = "File",
                    Message = $"الملف مفتوح في تطبيق آخر: {ex.Message}",
                    Suggestion = "أغلق الملف في البرامج الأخرى قبل الطباعة",
                    BlocksPrint = true
                });
            }
        }

        private static void CheckFileFormat(string ext, List<PreflightItem> items)
        {
            if (!SupportedExtensions.Contains(ext))
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Error,
                    Category = "Format",
                    Message = $"نوع الملف '{ext}' غير مدعوم",
                    Suggestion = "الأنواع المدعومة: PDF, PNG, JPG, BMP, TIFF, TXT, DOC, DOCX",
                    BlocksPrint = true
                });
                return;
            }

            if (!NativeExtensions.Contains(ext))
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Info,
                    Category = "Format",
                    Message = $"ملف Office ({ext}) يتطلب تحويلاً أولياً",
                    Suggestion = "التحويل يحدث تلقائياً ولكن قد يأخذ وقتاً إضافياً"
                });
            }
        }

        private static void CheckFileSize(long bytes, List<PreflightItem> items)
        {
            if (bytes == 0)
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Critical,
                    Category = "File",
                    Message = "الملف فارغ (0 بايت)",
                    BlocksPrint = true
                });
                return;
            }

            if (bytes > MaxFileSizeBytes)
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Error,
                    Category = "File",
                    Message = $"حجم الملف كبير جداً: {bytes / (1024 * 1024):N0} MB (الحد: 500 MB)",
                    Suggestion = "قسّم الملف إلى أجزاء أصغر",
                    BlocksPrint = false
                });
            }
            else if (bytes > WarnFileSizeBytes)
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Warning,
                    Category = "File",
                    Message = $"الملف كبير الحجم: {bytes / (1024 * 1024):N0} MB",
                    Suggestion = "قد يستغرق التحميل وقتاً أطول من المعتاد"
                });
            }
        }

        private static async Task CheckPdfAsync(string filePath, List<PreflightItem> items, CancellationToken ct)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var doc = PdfiumViewer.PdfDocument.Load(filePath);
                    int pageCount = doc.PageCount;

                    if (pageCount == 0)
                    {
                        items.Add(new PreflightItem
                        {
                            Severity = PreflightSeverity.Critical,
                            Category = "PDF",
                            Message = "ملف PDF لا يحتوي على صفحات",
                            BlocksPrint = true
                        });
                        return;
                    }

                    items.Add(new PreflightItem
                    {
                        Severity = PreflightSeverity.Pass,
                        Category = "PDF",
                        Message = $"عدد الصفحات: {pageCount}"
                    });

                    if (pageCount > MaxPageCount)
                    {
                        items.Add(new PreflightItem
                        {
                            Severity = PreflightSeverity.Warning,
                            Category = "PDF",
                            Message = $"عدد كبير من الصفحات: {pageCount} صفحة",
                            Suggestion = "يُنصح بتقسيم الملف إلى أجزاء أصغر"
                        });
                    }
                    else if (pageCount > WarnPageCount)
                    {
                        items.Add(new PreflightItem
                        {
                            Severity = PreflightSeverity.Info,
                            Category = "PDF",
                            Message = $"عدد الصفحات كبير نسبياً: {pageCount}",
                            Suggestion = "قد تستغرق الطباعة وقتاً أطول"
                        });
                    }

                    // Check page resolution via a quick sample render
                    try
                    {
                        var pageSize = doc.PageSizes[0];
                        // PDF dimensions are in points (1 point = 1/72 inch)
                        // At 300 DPI: pixels = (points/72) * 300
                        double widthInches = pageSize.Width / 72.0;
                        double heightInches = pageSize.Height / 72.0;

                        // Render a tiny sample to detect effective resolution
                        int sampleDpi = 36; // Very low = fast
                        using var img = doc.Render(0, (int)(widthInches * sampleDpi), (int)(heightInches * sampleDpi), sampleDpi, sampleDpi, false);

                        // Check if the page is valid (has real content)
                        bool hasContent = false;
                        if (img is System.Drawing.Bitmap bmp)
                        {
                            // Sample a few pixels
                            int whitePx = 0, colorPx = 0;
                            for (int x = 10; x < bmp.Width - 10; x += 15)
                                for (int y = 10; y < bmp.Height - 10; y += 15)
                                {
                                    var px = bmp.GetPixel(x, y);
                                    if (px.R > 240 && px.G > 240 && px.B > 240) whitePx++;
                                    else colorPx++;
                                }
                            hasContent = colorPx > 5;
                        }

                        if (!hasContent)
                        {
                            items.Add(new PreflightItem
                            {
                                Severity = PreflightSeverity.Warning,
                                Category = "PDF",
                                Message = "الصفحة الأولى تبدو فارغة",
                                Suggestion = "تأكد أن الملف يحتوي على محتوى مرئي"
                            });
                        }

                        // Check page dimensions (warn if unusually small or large)
                        if (widthInches < 1 || heightInches < 1)
                        {
                            items.Add(new PreflightItem
                            {
                                Severity = PreflightSeverity.Warning,
                                Category = "PDF",
                                Message = $"أبعاد الصفحة صغيرة جداً: {widthInches:F1}\" × {heightInches:F1}\"",
                                Suggestion = "تأكد من صحة حجم الصفحة في ملف PDF"
                            });
                        }
                        else if (widthInches > 24 || heightInches > 36)
                        {
                            items.Add(new PreflightItem
                            {
                                Severity = PreflightSeverity.Warning,
                                Category = "PDF",
                                Message = $"أبعاد الصفحة كبيرة: {widthInches:F1}\" × {heightInches:F1}\"",
                                Suggestion = "تأكد من توافق حجم الصفحة مع ورق الطابعة"
                            });
                        }
                        else
                        {
                            items.Add(new PreflightItem
                            {
                                Severity = PreflightSeverity.Pass,
                                Category = "PDF",
                                Message = $"أبعاد الصفحة: {widthInches:F1}\" × {heightInches:F1}\""
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Preflight] Page analysis error: {ex.Message}");
                        // Non-fatal — continue
                    }

                }, ct);
            }
            catch (Exception ex)
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Critical,
                    Category = "PDF",
                    Message = $"تعذّر فتح ملف PDF: {ex.Message}",
                    Suggestion = "قد يكون الملف تالفاً أو محمياً بكلمة مرور",
                    BlocksPrint = true
                });
            }
        }

        private static void CheckImage(string filePath, List<PreflightItem> items)
        {
            try
            {
                using var img = System.Drawing.Image.FromFile(filePath);
                float dpiX = img.HorizontalResolution;
                float dpiY = img.VerticalResolution;
                int w = img.Width;
                int h = img.Height;

                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Pass,
                    Category = "Image",
                    Message = $"أبعاد الصورة: {w}×{h} بكسل"
                });

                if (dpiX < MinAcceptableDpi || dpiY < MinAcceptableDpi)
                {
                    items.Add(new PreflightItem
                    {
                        Severity = PreflightSeverity.Warning,
                        Category = "Image",
                        Message = $"دقة الصورة منخفضة جداً: {dpiX:F0} DPI",
                        Suggestion = $"يُنصح بدقة لا تقل عن {MinRecommendedDpi} DPI للحصول على جودة طباعة جيدة"
                    });
                }
                else if (dpiX < MinRecommendedDpi || dpiY < MinRecommendedDpi)
                {
                    items.Add(new PreflightItem
                    {
                        Severity = PreflightSeverity.Info,
                        Category = "Image",
                        Message = $"دقة الصورة متوسطة: {dpiX:F0} DPI",
                        Suggestion = $"للحصول على جودة أفضل استخدم {MinRecommendedDpi} DPI أو أعلى"
                    });
                }
                else
                {
                    items.Add(new PreflightItem
                    {
                        Severity = PreflightSeverity.Pass,
                        Category = "Image",
                        Message = $"دقة الصورة جيدة: {dpiX:F0} DPI"
                    });
                }

                // Warn if image is very small in print terms
                double printWidthInches = w / (dpiX > 0 ? dpiX : 72f);
                double printHeightInches = h / (dpiY > 0 ? dpiY : 72f);
                if (printWidthInches < 0.5 || printHeightInches < 0.5)
                {
                    items.Add(new PreflightItem
                    {
                        Severity = PreflightSeverity.Warning,
                        Category = "Image",
                        Message = $"الصورة صغيرة جداً عند الطباعة: {printWidthInches:F1}\" × {printHeightInches:F1}\"",
                        Suggestion = "قد تظهر الصورة ضبابية عند التكبير"
                    });
                }
            }
            catch (Exception ex)
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Error,
                    Category = "Image",
                    Message = $"تعذّر قراءة الصورة: {ex.Message}",
                    BlocksPrint = false
                });
            }
        }

        private static void CheckOfficeDocument(string filePath, List<PreflightItem> items)
        {
            // Check if LibreOffice is available for conversion
            string[] libreofficePaths =
            {
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\LibreOffice\program\soffice.exe")
            };

            bool libreOfficeFound = libreofficePaths.Any(File.Exists);

            if (!libreOfficeFound)
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Warning,
                    Category = "Office",
                    Message = "LibreOffice غير مثبّت — قد لا يتم تحويل ملفات Office بشكل صحيح",
                    Suggestion = "ثبّت LibreOffice من https://www.libreoffice.org لدعم ملفات Word/Excel"
                });
            }
            else
            {
                items.Add(new PreflightItem
                {
                    Severity = PreflightSeverity.Pass,
                    Category = "Office",
                    Message = "LibreOffice متوفر لتحويل ملفات Office"
                });
            }
        }

        private static async Task CheckPrinterCompatibilityAsync(
            string filePath, string ext,
            string printerName,
            List<PreflightItem> items,
            CancellationToken ct)
        {
            try
            {
                var metadata = VendorDetectionEngine.Instance.GetPrinterMetadata(printerName);

                if (metadata == null)
                {
                    items.Add(new PreflightItem
                    {
                        Severity = PreflightSeverity.Warning,
                        Category = "Printer",
                        Message = $"تعذّر الحصول على معلومات الطابعة: {printerName}",
                        Suggestion = "تأكد من تثبيت الطابعة وتشغيلها"
                    });
                    return;
                }

                // Online check
                if (!metadata.IsOnline)
                {
                    items.Add(new PreflightItem
                    {
                        Severity = PreflightSeverity.Error,
                        Category = "Printer",
                        Message = $"الطابعة '{printerName}' غير متصلة",
                        Suggestion = "تأكد من تشغيل الطابعة وتوصيلها",
                        BlocksPrint = false   // Circuit breaker will handle retries
                    });
                }
                else
                {
                    items.Add(new PreflightItem
                    {
                        Severity = PreflightSeverity.Pass,
                        Category = "Printer",
                        Message = $"الطابعة '{printerName}' متصلة وجاهزة"
                    });
                }

                // Color check
                if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".tif")
                {
                    try
                    {
                        using var img = System.Drawing.Image.FromFile(filePath);
                        // Simple color detection: sample pixels
                        if (img is System.Drawing.Bitmap bmp)
                        {
                            bool hasColor = false;
                            for (int x = 0; x < bmp.Width && !hasColor; x += bmp.Width / 10 + 1)
                                for (int y = 0; y < bmp.Height && !hasColor; y += bmp.Height / 10 + 1)
                                {
                                    var px = bmp.GetPixel(x, y);
                                    if (Math.Abs(px.R - px.G) > 20 || Math.Abs(px.G - px.B) > 20)
                                        hasColor = true;
                                }

                            if (hasColor && !metadata.SupportsColor)
                            {
                                items.Add(new PreflightItem
                                {
                                    Severity = PreflightSeverity.Warning,
                                    Category = "Color",
                                    Message = "الصورة ملونة لكن الطابعة تطبع بالأبيض والأسود فقط",
                                    Suggestion = "ستتم طباعة الصورة بتدرجات الرمادي تلقائياً"
                                });
                            }
                        }
                    }
                    catch { /* ignore color check failures */ }
                }

                // Large file + network printer warning
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 50 * 1024 * 1024 && metadata.IsNetworkPrinter)
                {
                    items.Add(new PreflightItem
                    {
                        Severity = PreflightSeverity.Info,
                        Category = "Network",
                        Message = "ملف كبير على طابعة شبكية — قد يستغرق الإرسال وقتاً أطول",
                        Suggestion = "تأكد من استقرار اتصال الشبكة"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Preflight] Printer check error: {ex.Message}");
                // Non-fatal
            }

            await Task.CompletedTask;
        }
    }
}
