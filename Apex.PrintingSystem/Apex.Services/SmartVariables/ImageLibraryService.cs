using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.SmartVariables
{
    public class LibraryScanOptions
    {
        public bool   Recursive          { get; set; } = true;
        public bool   GenerateThumbnails { get; set; } = true;
        public int    ThumbnailSizePx    { get; set; } = 80;
        public long   MaxFileSizeBytes   { get; set; } = 50 * 1024 * 1024; // 50 MB
    }

    public interface IImageLibraryService
    {
        Task<List<ImageAsset>> ScanFolderAsync(
            string folderPath,
            LibraryScanOptions? options = null,
            IProgress<int>? progress    = null,
            CancellationToken ct        = default);

        ImageAsset ProbeFile(string fullPath, bool generateThumbnail, int thumbSizePx);
        string?    GenerateThumbnailBase64(string fullPath, int sizePx);
    }

    public class ImageLibraryService : IImageLibraryService
    {
        private static readonly HashSet<string> SupportedExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };

        // ── Public API ─────────────────────────────────────────────────────────

        public async Task<List<ImageAsset>> ScanFolderAsync(
            string              folderPath,
            LibraryScanOptions? options  = null,
            IProgress<int>?     progress = null,
            CancellationToken   ct       = default)
        {
            options ??= new LibraryScanOptions();
            var assets = new List<ImageAsset>();

            if (!Directory.Exists(folderPath))
                return assets;

            var searchOption = options.Recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var files = Directory
                .EnumerateFiles(folderPath, "*.*", searchOption)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            int processed = 0;
            foreach (string file in files)
            {
                ct.ThrowIfCancellationRequested();

                var asset = await Task.Run(
                    () => ProbeFile(file, options.GenerateThumbnails, options.ThumbnailSizePx),
                    ct);

                if (asset.FileSizeBytes <= options.MaxFileSizeBytes || asset.FileSizeBytes == 0)
                    assets.Add(asset);

                processed++;
                progress?.Report((int)(processed * 100.0 / files.Count));
            }

            return assets;
        }

        public ImageAsset ProbeFile(string fullPath, bool generateThumbnail, int thumbSizePx)
        {
            var asset = new ImageAsset
            {
                FileName  = Path.GetFileName(fullPath),
                FullPath  = fullPath,
                Extension = Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant(),
            };

            if (!File.Exists(fullPath))
            {
                asset.Status = AssetStatus.Missing;
                return asset;
            }

            try
            {
                var fi = new FileInfo(fullPath);
                asset.FileSizeBytes = fi.Length;

                // Load image to get dimensions
                using var bmp = LoadBitmap(fullPath);
                if (bmp == null)
                {
                    asset.Status = AssetStatus.UnsupportedFormat;
                    return asset;
                }

                asset.Width  = bmp.Width;
                asset.Height = bmp.Height;
                asset.Status = AssetStatus.Available;

                if (generateThumbnail)
                    asset.ThumbnailBase64 = GenerateThumbnailBase64Internal(bmp, thumbSizePx);
            }
            catch (OutOfMemoryException)
            {
                // System.Drawing throws OOM for corrupt/unsupported files
                asset.Status = AssetStatus.Corrupted;
            }
            catch (Exception)
            {
                asset.Status = AssetStatus.Corrupted;
            }

            return asset;
        }

        public string? GenerateThumbnailBase64(string fullPath, int sizePx)
        {
            if (!File.Exists(fullPath)) return null;
            try
            {
                using var bmp = LoadBitmap(fullPath);
                if (bmp == null) return null;
                return GenerateThumbnailBase64Internal(bmp, sizePx);
            }
            catch
            {
                return null;
            }
        }

        // ── Internal helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Loads an image using System.Drawing.Common.
        /// Returns null if the format is unsupported or file is corrupt.
        /// </summary>
        private static System.Drawing.Bitmap? LoadBitmap(string path)
        {
            try
            {
                // Load from stream to avoid file-lock on the original
                var bytes = File.ReadAllBytes(path);
                using var ms = new MemoryStream(bytes);
                return new System.Drawing.Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        private static string? GenerateThumbnailBase64Internal(
            System.Drawing.Bitmap src, int sizePx)
        {
            try
            {
                int srcW = src.Width;
                int srcH = src.Height;

                if (srcW <= 0 || srcH <= 0) return null;

                // Maintain aspect ratio
                double scale = Math.Min((double)sizePx / srcW, (double)sizePx / srcH);
                int    dstW  = Math.Max(1, (int)(srcW * scale));
                int    dstH  = Math.Max(1, (int)(srcH * scale));

                using var thumb = new System.Drawing.Bitmap(dstW, dstH,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                using (var g = System.Drawing.Graphics.FromImage(thumb))
                {
                    g.InterpolationMode  = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.SmoothingMode      = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.DrawImage(src, 0, 0, dstW, dstH);
                }

                using var ms = new MemoryStream();
                thumb.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return Convert.ToBase64String(ms.ToArray());
            }
            catch
            {
                return null;
            }
        }
    }
}
