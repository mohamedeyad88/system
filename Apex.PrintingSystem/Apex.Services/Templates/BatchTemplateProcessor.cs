using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Templates
{
    public class BatchProcessOptions
    {
        public string OutputFolder { get; set; } = "";
        public string OutputFormat { get; set; } = "PNG";
        public int DpiResolution { get; set; } = 300;
        public bool GeneratePdf { get; set; } = false;
        public string FileNamePattern { get; set; } = "output_{row:000}";
        public int MaxConcurrency { get; set; } = 4;
        public bool SkipOnError { get; set; } = true;
    }

    public class BatchProcessResult
    {
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public long ElapsedMs { get; set; }
        public List<string> OutputFiles { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string SummaryArabic { get; set; } = "";
    }

    public class BatchProgressEventArgs : EventArgs
    {
        public int ProcessedRows { get; set; }
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public double PercentDone { get; set; }
        public string CurrentItem { get; set; } = "";
    }

    public class BatchTemplateProcessor
    {
        // ── Singleton ────────────────────────────────────────────────────────────

        private static readonly Lazy<BatchTemplateProcessor> _instance =
            new(() => new BatchTemplateProcessor());

        public static BatchTemplateProcessor Instance => _instance.Value;

        private BatchTemplateProcessor() { }

        // ── Events ───────────────────────────────────────────────────────────────

        public event EventHandler<BatchProgressEventArgs>? Progress;

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Process a template against all data rows, writing output files.
        /// Uses SemaphoreSlim with MaxConcurrency for parallel rendering.
        /// </summary>
        public async Task<BatchProcessResult> ProcessBatchAsync(
            ApextTemplate template,
            List<TemplateDataRow> dataRows,
            BatchProcessOptions options,
            CancellationToken cancellationToken = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (dataRows == null) throw new ArgumentNullException(nameof(dataRows));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.OutputFolder))
                throw new ArgumentException("OutputFolder must be specified.", nameof(options));

            Directory.CreateDirectory(options.OutputFolder);

            BatchProcessResult result = new()
            {
                TotalRows = dataRows.Count
            };

            object lockObj = new();
            Stopwatch sw = Stopwatch.StartNew();

            int maxConcurrency = Math.Max(1, options.MaxConcurrency);
            SemaphoreSlim semaphore = new(maxConcurrency, maxConcurrency);

            List<Task> tasks = new(dataRows.Count);

            foreach (TemplateDataRow row in dataRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                TemplateDataRow capturedRow = row;

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileBase = BuildFileName(options.FileNamePattern, capturedRow);
                        string ext = options.OutputFormat.ToUpperInvariant() switch
                        {
                            "BMP" => ".bmp",
                            _ => ".png"
                        };
                        string outputPath = Path.Combine(options.OutputFolder, fileBase + ext);

                        List<Bitmap> pages = RenderTemplate(template, capturedRow, dpi: options.DpiResolution);

                        try
                        {
                            if (pages.Count == 1)
                            {
                                SaveBitmap(pages[0], outputPath, options.OutputFormat);
                            }
                            else
                            {
                                // Multi-page: save each page with _p1, _p2 suffix
                                for (int pi = 0; pi < pages.Count; pi++)
                                {
                                    string pageFile = Path.Combine(
                                        options.OutputFolder,
                                        $"{fileBase}_p{pi + 1}{ext}");
                                    SaveBitmap(pages[pi], pageFile, options.OutputFormat);

                                    lock (lockObj)
                                        result.OutputFiles.Add(pageFile);
                                }
                            }

                            if (pages.Count == 1)
                            {
                                lock (lockObj)
                                    result.OutputFiles.Add(outputPath);
                            }

                            int successCount, processedCount;
                            lock (lockObj)
                            {
                                result.SuccessCount++;
                                successCount = result.SuccessCount;
                                processedCount = result.SuccessCount + result.FailureCount;
                            }

                            FireProgress(result, processedCount, dataRows.Count, fileBase);
                        }
                        finally
                        {
                            foreach (Bitmap bmp in pages)
                                bmp.Dispose();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = $"Row {capturedRow.RowIndex}: {ex.Message}";

                        lock (lockObj)
                        {
                            result.FailureCount++;
                            result.Errors.Add(errorMsg);
                        }

                        if (!options.SkipOnError)
                            throw;

                        int processedCount;
                        lock (lockObj)
                            processedCount = result.SuccessCount + result.FailureCount;

                        FireProgress(result, processedCount, dataRows.Count, $"Error: Row {capturedRow.RowIndex}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.SummaryArabic = BuildArabicSummary(result);

            return result;
        }

        /// <summary>
        /// Render a single template page with data bindings to a Bitmap.
        /// </summary>
        public Bitmap RenderPage(
            TemplatePageDefinition page,
            TemplateDataRow data,
            Dictionary<string, byte[]>? assets = null,
            int dpi = 300)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (data == null) data = new TemplateDataRow();
            if (dpi <= 0) dpi = 300;

            int widthPx = (int)(page.WidthMm / 25.4 * dpi);
            int heightPx = (int)(page.HeightMm / 25.4 * dpi);

            // Guard against absurdly large bitmaps
            widthPx = Math.Clamp(widthPx, 1, 20000);
            heightPx = Math.Clamp(heightPx, 1, 20000);

            Bitmap bitmap = new(widthPx, heightPx, PixelFormat.Format32bppArgb);
            bitmap.SetResolution(dpi, dpi);

            using Graphics g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Fill background
            Color bgColor = ParseColor(page.BackgroundColor, Color.White);
            g.FillRectangle(new SolidBrush(bgColor), 0, 0, widthPx, heightPx);

            // Draw background image
            if (!string.IsNullOrEmpty(page.BackgroundImageAssetId) && assets != null)
            {
                byte[]? imgBytes = FindAsset(assets, page.BackgroundImageAssetId);
                if (imgBytes != null)
                {
                    using MemoryStream ms = new(imgBytes);
                    try
                    {
                        using Image bgImage = Image.FromStream(ms);
                        g.DrawImage(bgImage, new Rectangle(0, 0, widthPx, heightPx));
                    }
                    catch { /* ignore corrupted background image */ }
                }
            }

            // Draw slots
            foreach (TemplateSlotDefinition slot in page.Slots)
            {
                float slotX = (float)(slot.X / 25.4 * dpi);
                float slotY = (float)(slot.Y / 25.4 * dpi);
                float slotW = (float)(slot.Width / 25.4 * dpi);
                float slotH = (float)(slot.Height / 25.4 * dpi);
                RectangleF slotRect = new(slotX, slotY, slotW, slotH);

                // Skip zero-size slots
                if (slotW <= 0 || slotH <= 0) continue;

                // Opacity < 1 → render the slot onto a transparent layer, then
                // composite it back with the requested alpha. Opacity == 1 draws directly.
                double opacity = Math.Clamp(slot.Opacity, 0.0, 1.0);
                if (opacity < 0.999)
                {
                    using var layer = new Bitmap(bitmap.Width, bitmap.Height,
                                                 System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var lg = Graphics.FromImage(layer))
                    {
                        lg.SmoothingMode     = g.SmoothingMode;
                        lg.TextRenderingHint = g.TextRenderingHint;
                        lg.InterpolationMode = g.InterpolationMode;
                        DrawSlot(lg, slot, slotRect, data, assets, dpi);
                    }

                    var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)opacity };
                    using var attrs = new System.Drawing.Imaging.ImageAttributes();
                    attrs.SetColorMatrix(matrix);
                    g.DrawImage(layer,
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        0, 0, bitmap.Width, bitmap.Height,
                        GraphicsUnit.Pixel, attrs);
                }
                else
                {
                    DrawSlot(g, slot, slotRect, data, assets, dpi);
                }
            }

            return bitmap;
        }

        /// <summary>
        /// Render all pages of a template for a given data row.
        /// Caller is responsible for disposing the returned bitmaps.
        /// </summary>
        public List<Bitmap> RenderTemplate(
            ApextTemplate template,
            TemplateDataRow data,
            Dictionary<string, byte[]>? assets = null,
            int dpi = 300)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (data == null) data = new TemplateDataRow();

            List<Bitmap> pages = new(template.Pages.Count);
            foreach (TemplatePageDefinition page in template.Pages)
                pages.Add(RenderPage(page, data, assets, dpi));
            return pages;
        }

        // ── Private rendering helpers ─────────────────────────────────────────────

        /// <summary>Dispatches a single slot to the correct draw routine by type.</summary>
        private static void DrawSlot(
            Graphics g,
            TemplateSlotDefinition slot,
            RectangleF slotRect,
            TemplateDataRow data,
            Dictionary<string, byte[]>? assets,
            int dpi)
        {
            switch (slot.DataType)
            {
                case SlotDataType.Image:
                    DrawImageSlot(g, slot, slotRect, data, assets);
                    break;
                case SlotDataType.QrCode:
                    DrawCodeSlot(g, slot, slotRect, data, dpi, ZXing.BarcodeFormat.QR_CODE);
                    break;
                case SlotDataType.Barcode:
                    DrawCodeSlot(g, slot, slotRect, data, dpi,
                        CodeRenderer.ResolveBarcodeFormat(slot.BarcodeType));
                    break;
                default:
                    DrawTextSlot(g, slot, slotRect, data, dpi);
                    break;
            }
        }

        private static void DrawTextSlot(
            Graphics g,
            TemplateSlotDefinition slot,
            RectangleF slotRect,
            TemplateDataRow data,
            int dpi)
        {
            // Draw slot background if not transparent
            if (!string.IsNullOrEmpty(slot.BackgroundColor) &&
                !slot.BackgroundColor.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
            {
                Color bg = ParseColor(slot.BackgroundColor, Color.Transparent);
                if (bg != Color.Transparent)
                    using (var bgBrush = new SolidBrush(bg))
                        g.FillRectangle(bgBrush, slotRect);
            }

            // Resolve text value
            string rawValue = data.Get(slot.VariableName, slot.DefaultValue ?? "");

            // Apply format string if defined on slot
            string renderedText;
            if (!string.IsNullOrEmpty(slot.FormatString))
                renderedText = TemplateVariableBindingSystem.RenderText(
                    $"{{{{{slot.VariableName}:{slot.FormatString}}}}}", data);
            else
                renderedText = rawValue;

            if (string.IsNullOrEmpty(renderedText)) return;

            // Build font
            FontStyle fontStyle = FontStyle.Regular;
            if (slot.Bold) fontStyle |= FontStyle.Bold;
            if (slot.Italic) fontStyle |= FontStyle.Italic;

            // Convert pt font size to pixels at DPI
            float fontSizePx = (float)(slot.FontSize * dpi / 72.0);

            Font? font = null;
            try
            {
                // GraphicsUnit.Pixel is needed because fontSizePx is already in pixels
                font = new Font(slot.FontFamily, fontSizePx, fontStyle, GraphicsUnit.Pixel);
            }
            catch
            {
                font = new Font("Tahoma", fontSizePx, fontStyle, GraphicsUnit.Pixel);
            }

            using (font)
            {
                Color textColor = ParseColor(slot.TextColor, Color.Black);
                using SolidBrush brush = new(textColor);

                StringFormat sf = new()
                {
                    LineAlignment = slot.VerticalAlign switch
                    {
                        VerticalAlign.Top => StringAlignment.Near,
                        VerticalAlign.Bottom => StringAlignment.Far,
                        _ => StringAlignment.Center
                    },
                    Alignment = slot.TextAlign switch
                    {
                        HorizontalAlign.Left => StringAlignment.Near,
                        HorizontalAlign.Right => StringAlignment.Far,
                        _ => StringAlignment.Center
                    },
                    FormatFlags = slot.IsRtl
                        ? StringFormatFlags.DirectionRightToLeft
                        : StringFormatFlags.NoClip,
                    Trimming = StringTrimming.EllipsisCharacter
                };

                if (slot.RotationDegrees != 0)
                {
                    // Translate to slot center, rotate, draw, restore
                    float cx = slotRect.X + slotRect.Width / 2f;
                    float cy = slotRect.Y + slotRect.Height / 2f;

                    GraphicsState state = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(slot.RotationDegrees);

                    RectangleF rotatedRect = new(
                        -slotRect.Width / 2f,
                        -slotRect.Height / 2f,
                        slotRect.Width,
                        slotRect.Height);

                    g.DrawString(renderedText, font, brush, rotatedRect, sf);
                    g.Restore(state);
                }
                else
                {
                    g.DrawString(renderedText, font, brush, slotRect, sf);
                }
            }
        }

        /// <summary>Renders a QR or 1-D barcode; falls back to text if the value can't be encoded.</summary>
        private static void DrawCodeSlot(
            Graphics g,
            TemplateSlotDefinition slot,
            RectangleF slotRect,
            TemplateDataRow data,
            int dpi,
            ZXing.BarcodeFormat format)
        {
            // Slot background
            if (!string.IsNullOrEmpty(slot.BackgroundColor) &&
                !slot.BackgroundColor.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
            {
                Color bg = ParseColor(slot.BackgroundColor, Color.Transparent);
                if (bg != Color.Transparent)
                    using (var bgBrush = new SolidBrush(bg))
                        g.FillRectangle(bgBrush, slotRect);
            }

            string content = data.Get(slot.VariableName, slot.DefaultValue ?? "");
            if (string.IsNullOrEmpty(content)) return;

            // QR is square (use the smaller side); barcodes fill the slot.
            int w = Math.Max(1, (int)Math.Round(slotRect.Width));
            int h = Math.Max(1, (int)Math.Round(slotRect.Height));
            int codeW = w, codeH = h;
            if (format == ZXing.BarcodeFormat.QR_CODE)
            {
                int side = Math.Min(w, h);
                codeW = codeH = side;
            }

            using Bitmap? code = CodeRenderer.TryRender(content, format, codeW, codeH);
            if (code == null)
            {
                // Unencodable value (e.g. EAN-13 with letters) → show the text instead.
                DrawTextSlot(g, slot, slotRect, data, dpi);
                return;
            }

            // Center the code within the slot rectangle.
            float ox = slotRect.X + (slotRect.Width - code.Width) / 2f;
            float oy = slotRect.Y + (slotRect.Height - code.Height) / 2f;
            g.DrawImage(code, new RectangleF(ox, oy, code.Width, code.Height));
        }

        private static void DrawImageSlot(
            Graphics g,
            TemplateSlotDefinition slot,
            RectangleF slotRect,
            TemplateDataRow data,
            Dictionary<string, byte[]>? assets)
        {
            // Slot background
            if (!string.IsNullOrEmpty(slot.BackgroundColor) &&
                !slot.BackgroundColor.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
            {
                Color bg = ParseColor(slot.BackgroundColor, Color.Transparent);
                if (bg != Color.Transparent)
                    using (var bgBrush = new SolidBrush(bg))
                        g.FillRectangle(bgBrush, slotRect);
            }

            // Determine asset key: prefer data-driven value, fallback to static ImageAssetId
            string? assetKey = data.Get(slot.VariableName, slot.ImageAssetId ?? "");
            if (string.IsNullOrEmpty(assetKey)) assetKey = slot.ImageAssetId;
            if (string.IsNullOrEmpty(assetKey) || assets == null) return;

            byte[]? imgBytes = FindAsset(assets, assetKey);
            if (imgBytes == null) return;

            using MemoryStream ms = new(imgBytes);
            try
            {
                using Image img = Image.FromStream(ms);
                Rectangle destRect = Rectangle.Round(slotRect);

                if (slot.RotationDegrees != 0)
                {
                    float cx = slotRect.X + slotRect.Width / 2f;
                    float cy = slotRect.Y + slotRect.Height / 2f;
                    GraphicsState state = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(slot.RotationDegrees);
                    RectangleF rotatedRect = new(-slotRect.Width / 2f, -slotRect.Height / 2f,
                        slotRect.Width, slotRect.Height);
                    g.DrawImage(img, rotatedRect);
                    g.Restore(state);
                }
                else
                {
                    g.DrawImage(img, destRect);
                }
            }
            catch { /* skip unrenderable image */ }
        }

        // ── Utilities ─────────────────────────────────────────────────────────────

        private static byte[]? FindAsset(Dictionary<string, byte[]> assets, string key)
        {
            if (assets.TryGetValue(key, out byte[]? val)) return val;

            // Also try matching by filename (strip path prefix if any)
            string fileName = Path.GetFileName(key);
            foreach (KeyValuePair<string, byte[]> kv in assets)
            {
                if (Path.GetFileName(kv.Key).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }

        private static Color ParseColor(string? colorStr, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(colorStr)) return fallback;
            if (colorStr.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
                return Color.Transparent;
            try { return ColorTranslator.FromHtml(colorStr); }
            catch { return fallback; }
        }

        private static void SaveBitmap(Bitmap bitmap, string filePath, string format)
        {
            ImageFormat imgFormat = format.ToUpperInvariant() switch
            {
                "BMP" => ImageFormat.Bmp,
                _ => ImageFormat.Png
            };
            bitmap.Save(filePath, imgFormat);
        }

        private static string BuildFileName(string pattern, TemplateDataRow row)
        {
            if (string.IsNullOrEmpty(pattern)) pattern = "output_{row:000}";

            // Replace {row:format} or {row}
            string result = Regex.Replace(pattern, @"\{row(?::([^}]*))?\}", m =>
            {
                string fmt = m.Groups[1].Success ? m.Groups[1].Value : "000";
                return row.RowIndex.ToString(fmt);
            });

            // Replace {VariableName} with data values
            result = Regex.Replace(result, @"\{([^{}]+)\}", m =>
            {
                string varName = m.Groups[1].Value;
                if (row.Values.TryGetValue(varName, out string? val))
                    return SanitizeFileName(val);
                return m.Value;
            });

            return result;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private void FireProgress(BatchProcessResult result, int processed, int total, string currentItem)
        {
            double pct = total > 0 ? processed * 100.0 / total : 0;
            Progress?.Invoke(this, new BatchProgressEventArgs
            {
                ProcessedRows = processed,
                TotalRows = total,
                SuccessCount = result.SuccessCount,
                FailureCount = result.FailureCount,
                PercentDone = pct,
                CurrentItem = currentItem
            });
        }

        private static string BuildArabicSummary(BatchProcessResult result)
        {
            long seconds = result.ElapsedMs / 1000;
            return $"اكتملت المعالجة: {result.SuccessCount} ناجح، {result.FailureCount} فاشل " +
                   $"من أصل {result.TotalRows} سجل في {seconds} ثانية.";
        }
    }
}
