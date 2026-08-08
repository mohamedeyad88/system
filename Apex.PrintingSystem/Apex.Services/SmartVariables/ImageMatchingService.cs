using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.SmartVariables
{
    public enum ImageMatchMode
    {
        /// <summary>Match image file name (without extension) to a row's column value.</summary>
        ByFileName,
        /// <summary>Match on the ID column value (e.g. "1001" → "1001.jpg").</summary>
        ById,
        /// <summary>Match on the Name column value (e.g. "أحمد" → "أحمد.png").</summary>
        ByName,
        /// <summary>Each row specifies a full/relative image path in a designated column.</summary>
        ByPathColumn,
        /// <summary>All rows share a single static image (useful for logo/watermark).</summary>
        StaticSingle,
        /// <summary>User assigns images manually per-row.</summary>
        Manual,
    }

    public class ImageMatchingOptions
    {
        public ImageMatchMode Mode { get; set; } = ImageMatchMode.ByFileName;
        /// <summary>The data column to use as the matching key (for ByFileName / ById / ByName / ByPathColumn).</summary>
        public string KeyColumn { get; set; } = "";
        /// <summary>Base folder where images live.</summary>
        public string ImageFolder { get; set; } = "";
        /// <summary>Static image path (used when Mode = StaticSingle).</summary>
        public string StaticImagePath { get; set; } = "";
        /// <summary>Allowed extensions to consider (empty = all supported).</summary>
        public List<string> Extensions { get; set; } = new() { ".jpg", ".jpeg", ".png" };
        /// <summary>Case-insensitive file name matching.</summary>
        public bool CaseInsensitive { get; set; } = true;
    }

    public interface IImageMatchingService
    {
        void ResolveImages(
            SmartDataSource source,
            List<ImageAsset> library,
            ImageMatchingOptions options);
    }

    public class ImageMatchingService : IImageMatchingService
    {
        public void ResolveImages(
            SmartDataSource source,
            List<ImageAsset> library,
            ImageMatchingOptions options)
        {
            if (source == null || source.Rows.Count == 0) return;

            // Build a fast lookup: normalized-filename-without-ext → list of assets
            var lookup = BuildLookup(library, options);

            foreach (var row in source.Rows)
            {
                ResolveRow(row, library, lookup, options);
            }

            // Mark which assets were actually used
            var usedPaths = new HashSet<string>(
                source.Rows
                    .Where(r => r.ResolvedImagePath != null)
                    .Select(r => r.ResolvedImagePath!),
                StringComparer.OrdinalIgnoreCase);

            foreach (var asset in library)
                asset.IsUsed = usedPaths.Contains(asset.FullPath);
        }

        // ── Per-row resolution ─────────────────────────────────────────────────

        private static void ResolveRow(
            SmartDataRow row,
            List<ImageAsset> library,
            Dictionary<string, List<ImageAsset>> lookup,
            ImageMatchingOptions options)
        {
            row.ResolvedImagePath = null;
            row.ImageStatus = ImageStatus.NotRequired;

            switch (options.Mode)
            {
                case ImageMatchMode.StaticSingle:
                    ResolveStatic(row, options);
                    break;

                case ImageMatchMode.Manual:
                    ResolveManual(row);
                    break;

                case ImageMatchMode.ByPathColumn:
                    ResolveByPathColumn(row, options);
                    break;

                default:
                    ResolveByKey(row, lookup, options);
                    break;
            }
        }

        private static void ResolveStatic(SmartDataRow row, ImageMatchingOptions options)
        {
            if (string.IsNullOrEmpty(options.StaticImagePath))
            {
                row.ImageStatus = ImageStatus.Missing;
                return;
            }

            if (File.Exists(options.StaticImagePath))
            {
                row.ResolvedImagePath = options.StaticImagePath;
                row.ImageStatus = ImageStatus.Found;
            }
            else
            {
                row.ImageStatus = ImageStatus.Missing;
                row.Warnings.Add($"الصورة الثابتة غير موجودة: {options.StaticImagePath}");
                if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
            }
        }

        private static void ResolveManual(SmartDataRow row)
        {
            if (!string.IsNullOrEmpty(row.ManualImagePath) && File.Exists(row.ManualImagePath))
            {
                row.ResolvedImagePath = row.ManualImagePath;
                row.ImageStatus = ImageStatus.Found;
            }
            else if (!string.IsNullOrEmpty(row.ManualImagePath))
            {
                row.ImageStatus = ImageStatus.Missing;
                row.Warnings.Add("الصورة المحددة يدوياً غير موجودة.");
                if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
            }
            else
            {
                row.ImageStatus = ImageStatus.Missing;
            }
        }

        private static void ResolveByPathColumn(SmartDataRow row, ImageMatchingOptions options)
        {
            if (!row.Values.TryGetValue(options.KeyColumn, out string? pathValue)
                || string.IsNullOrWhiteSpace(pathValue))
            {
                row.ImageStatus = ImageStatus.Missing;
                return;
            }

            // Resolve relative to ImageFolder if not absolute
            string fullPath = Path.IsPathRooted(pathValue)
                ? pathValue
                : Path.Combine(options.ImageFolder, pathValue);

            if (File.Exists(fullPath))
            {
                row.ResolvedImagePath = fullPath;
                row.ImageStatus = ImageStatus.Found;
            }
            else
            {
                row.ImageStatus = ImageStatus.Missing;
                row.Warnings.Add($"الصورة '{pathValue}' غير موجودة.");
                if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
            }
        }

        private static void ResolveByKey(
            SmartDataRow row,
            Dictionary<string, List<ImageAsset>> lookup,
            ImageMatchingOptions options)
        {
            if (!row.Values.TryGetValue(options.KeyColumn, out string? keyValue)
                || string.IsNullOrWhiteSpace(keyValue))
            {
                row.ImageStatus = ImageStatus.Missing;
                return;
            }

            string normKey = NormalizeKey(keyValue, options.CaseInsensitive);

            if (!lookup.TryGetValue(normKey, out var matches) || matches.Count == 0)
            {
                row.ImageStatus = ImageStatus.Missing;
                row.Warnings.Add($"لم يتم العثور على صورة للقيمة '{keyValue}'.");
                if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
                return;
            }

            // Filter to only Available assets
            var available = matches.Where(a => a.Status == AssetStatus.Available).ToList();

            if (available.Count == 0)
            {
                var first = matches[0];
                row.ImageStatus = first.Status == AssetStatus.Corrupted
                    ? ImageStatus.Corrupted
                    : ImageStatus.Missing;
                row.Warnings.Add($"الصورة '{first.FileName}' تالفة أو غير مدعومة.");
                if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
                return;
            }

            if (available.Count > 1)
            {
                // Multiple matches: warn but use the first one
                row.ResolvedImagePath = available[0].FullPath;
                row.ImageStatus = ImageStatus.MultipleMatches;
                row.Warnings.Add($"تطابق متعدد للقيمة '{keyValue}' ({available.Count} صور). تم استخدام '{available[0].FileName}'.");
                if (row.Status < RowStatus.Warning) row.Status = RowStatus.Warning;
                return;
            }

            row.ResolvedImagePath = available[0].FullPath;
            row.ImageStatus = ImageStatus.Found;
        }

        // ── Lookup builder ─────────────────────────────────────────────────────

        private static Dictionary<string, List<ImageAsset>> BuildLookup(
            List<ImageAsset> library,
            ImageMatchingOptions options)
        {
            var dict = new Dictionary<string, List<ImageAsset>>(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in library)
            {
                if (!MatchesExtensionFilter(asset.Extension, options.Extensions))
                    continue;

                string fileNameNoExt = Path.GetFileNameWithoutExtension(asset.FileName);
                string key = NormalizeKey(fileNameNoExt, options.CaseInsensitive);

                if (!dict.TryGetValue(key, out var list))
                {
                    list = new List<ImageAsset>();
                    dict[key] = list;
                }
                list.Add(asset);
            }

            return dict;
        }

        private static bool MatchesExtensionFilter(string ext, List<string> allowed)
        {
            if (allowed.Count == 0) return true;
            string dotExt = ext.StartsWith('.') ? ext : "." + ext;
            return allowed.Any(a => string.Equals(
                a.StartsWith('.') ? a : "." + a,
                dotExt,
                StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeKey(string value, bool caseInsensitive)
        {
            string v = value.Trim();
            return caseInsensitive ? v.ToLowerInvariant() : v;
        }
    }
}
