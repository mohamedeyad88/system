using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Apex.Services.SmartVariables;

namespace Apex.Services.Templates
{
    public enum SlotDataType { Text, Number, Date, Image, Barcode, QrCode, Counter }
    public enum HorizontalAlign { Left, Center, Right }
    public enum VerticalAlign { Top, Middle, Bottom }
    public enum PageOrientation { Portrait, Landscape }

    public class TemplateSlotDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "";
        public string VariableName { get; set; } = "";
        public SlotDataType DataType { get; set; } = SlotDataType.Text;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string FontFamily { get; set; } = "Tahoma";
        public double FontSize { get; set; } = 12;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string TextColor { get; set; } = "#000000";
        public string BackgroundColor { get; set; } = "Transparent";
        public HorizontalAlign TextAlign { get; set; } = HorizontalAlign.Right;
        public VerticalAlign VerticalAlign { get; set; } = VerticalAlign.Middle;
        public string? DefaultValue { get; set; }
        public bool Required { get; set; }
        public string? FormatString { get; set; }
        public bool IsRtl { get; set; } = true;
        public int RotationDegrees { get; set; } = 0;
        public string? BarcodeType { get; set; }
        public string? ImageAssetId { get; set; }

        // Image-specific display properties
        /// <summary>How the image fills its slot: Contain | Cover | Stretch | Fill</summary>
        public string ImageFitMode { get; set; } = "Contain";
        /// <summary>Opacity 0.0–1.0 (1 = fully opaque)</summary>
        public double Opacity { get; set; } = 1.0;
    }

    public class TemplatePageDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public double WidthMm { get; set; } = 210;
        public double HeightMm { get; set; } = 297;
        public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;
        public string BackgroundColor { get; set; } = "#FFFFFF";
        public string? BackgroundImageAssetId { get; set; }
        public double MarginTopMm { get; set; } = 10;
        public double MarginBottomMm { get; set; } = 10;
        public double MarginLeftMm { get; set; } = 10;
        public double MarginRightMm { get; set; } = 10;
        public List<TemplateSlotDefinition> Slots { get; set; } = new();
    }

    public class ApextTemplate
    {
        public string FormatVersion { get; set; } = "2.0";
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;
        public string Category { get; set; } = "General";
        public List<TemplatePageDefinition> Pages { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public string? PreviewBase64 { get; set; }
    }

    public static class ApextFileFormat
    {
        public const string FileExtension = ".apext";
        public const string MimeType = "application/x-apex-template";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Save template to .apext file (ZIP archive).
        /// Contains: template.json, assets/{id}.{ext}, manifest.json
        /// </summary>
        public static void Save(ApextTemplate template, string filePath,
                                Dictionary<string, byte[]>? assets = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty.", nameof(filePath));

            template.ModifiedAt = DateTime.Now;

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using ZipArchive zip = new(fs, ZipArchiveMode.Create, leaveOpen: false);

            // Write template.json
            ZipArchiveEntry templateEntry = zip.CreateEntry("template.json", CompressionLevel.Optimal);
            using (Stream templateStream = templateEntry.Open())
            {
                byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(template, SerializerOptions);
                templateStream.Write(jsonBytes, 0, jsonBytes.Length);
            }

            // Write assets
            int assetCount = 0;
            if (assets != null)
            {
                foreach (KeyValuePair<string, byte[]> asset in assets)
                {
                    if (asset.Value == null || asset.Value.Length == 0) continue;
                    string entryName = $"assets/{SanitizeAssetName(asset.Key)}";
                    ZipArchiveEntry assetEntry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    using Stream assetStream = assetEntry.Open();
                    assetStream.Write(asset.Value, 0, asset.Value.Length);
                    assetCount++;
                }
            }

            // Write manifest.json
            ZipArchiveEntry manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (Stream manifestStream = manifestEntry.Open())
            {
                var manifest = new
                {
                    version = "2.0",
                    assetCount,
                    pageCount = template.Pages.Count,
                    templateId = template.Id,
                    savedAt = DateTime.UtcNow.ToString("O")
                };
                byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
                manifestStream.Write(manifestBytes, 0, manifestBytes.Length);
            }
        }

        /// <summary>
        /// Load .apext file. Returns template and all embedded assets.
        /// </summary>
        public static (ApextTemplate Template, Dictionary<string, byte[]> Assets) Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Template file not found: {filePath}", filePath);

            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using ZipArchive zip = new(fs, ZipArchiveMode.Read, leaveOpen: false);

            // Read template.json
            ZipArchiveEntry? templateEntry = zip.GetEntry("template.json");
            if (templateEntry == null)
                throw new InvalidDataException($"Invalid .apext file: missing template.json in '{filePath}'");

            ApextTemplate template;
            using (Stream templateStream = templateEntry.Open())
            using (MemoryStream ms = new())
            {
                templateStream.CopyTo(ms);
                template = JsonSerializer.Deserialize<ApextTemplate>(ms.ToArray(), SerializerOptions)
                           ?? throw new InvalidDataException("Failed to deserialize template.json");
            }

            // Read assets
            Dictionary<string, byte[]> assets = new(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Length == 0) continue;

                string assetKey = entry.Name; // filename without path prefix
                using Stream assetStream = entry.Open();
                using MemoryStream ms = new();
                assetStream.CopyTo(ms);
                assets[assetKey] = ms.ToArray();
            }

            return (template, assets);
        }

        /// <summary>
        /// Create a new template with defaults: one A4 portrait page, no slots.
        /// </summary>
        public static ApextTemplate CreateNew(string name, string category = "General")
        {
            return new ApextTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Category = category,
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now,
                Pages =
                [
                    new TemplatePageDefinition
                    {
                        WidthMm = 210,
                        HeightMm = 297,
                        Orientation = PageOrientation.Portrait,
                        BackgroundColor = "#FFFFFF",
                        Slots = new List<TemplateSlotDefinition>()
                    }
                ]
            };
        }

        /// <summary>
        /// Export template as indented JSON string.
        /// </summary>
        public static string ExportJson(ApextTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            return JsonSerializer.Serialize(template, SerializerOptions);
        }

        /// <summary>
        /// Import template from JSON string.
        /// </summary>
        public static ApextTemplate ImportJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON string cannot be empty.", nameof(json));

            ApextTemplate? template = JsonSerializer.Deserialize<ApextTemplate>(json, SerializerOptions);
            return template ?? throw new InvalidDataException("Failed to deserialize template JSON.");
        }

        /// <summary>
        /// Validate template structure. Returns (IsValid, list of error messages).
        /// </summary>
        public static (bool IsValid, List<string> Errors) Validate(ApextTemplate template)
        {
            List<string> errors = new();

            if (template == null)
            {
                errors.Add("Template is null.");
                return (false, errors);
            }

            if (string.IsNullOrWhiteSpace(template.Name))
                errors.Add("Template name is required.");

            if (template.Pages == null || template.Pages.Count == 0)
                errors.Add("Template must have at least one page.");

            if (template.Pages != null)
            {
                for (int pi = 0; pi < template.Pages.Count; pi++)
                {
                    TemplatePageDefinition page = template.Pages[pi];
                    if (page.WidthMm <= 0)
                        errors.Add($"Page {pi + 1}: WidthMm must be greater than 0.");
                    if (page.HeightMm <= 0)
                        errors.Add($"Page {pi + 1}: HeightMm must be greater than 0.");

                    if (page.Slots != null)
                    {
                        HashSet<string> slotIds = new();
                        for (int si = 0; si < page.Slots.Count; si++)
                        {
                            TemplateSlotDefinition slot = page.Slots[si];

                            if (string.IsNullOrWhiteSpace(slot.VariableName))
                                errors.Add($"Page {pi + 1}, Slot {si + 1} '{slot.Name}': VariableName is required.");

                            if (!string.IsNullOrEmpty(slot.Id) && !slotIds.Add(slot.Id))
                                errors.Add($"Page {pi + 1}: Duplicate slot Id '{slot.Id}'.");

                            if (slot.Width <= 0)
                                errors.Add($"Page {pi + 1}, Slot '{slot.Name}': Width must be greater than 0.");
                            if (slot.Height <= 0)
                                errors.Add($"Page {pi + 1}, Slot '{slot.Name}': Height must be greater than 0.");

                            if (slot.X < 0 || slot.X + slot.Width > page.WidthMm)
                                errors.Add($"Page {pi + 1}, Slot '{slot.Name}': X position or width exceeds page bounds.");
                            if (slot.Y < 0 || slot.Y + slot.Height > page.HeightMm)
                                errors.Add($"Page {pi + 1}, Slot '{slot.Name}': Y position or height exceeds page bounds.");

                            if (slot.FontSize <= 0)
                                errors.Add($"Page {pi + 1}, Slot '{slot.Name}': FontSize must be greater than 0.");

                            if (slot.DataType == SlotDataType.Barcode && string.IsNullOrWhiteSpace(slot.BarcodeType))
                                errors.Add($"Page {pi + 1}, Slot '{slot.Name}': BarcodeType is required for Barcode slots.");

                            if (slot.DataType == SlotDataType.Image && string.IsNullOrWhiteSpace(slot.ImageAssetId))
                                errors.Add($"Page {pi + 1}, Slot '{slot.Name}': ImageAssetId is required for Image slots.");
                        }

                        // Check for overlapping required slots
                        List<TemplateSlotDefinition> requiredSlots = page.Slots
                            .Where(s => s.Required)
                            .ToList();

                        for (int i = 0; i < requiredSlots.Count; i++)
                        {
                            for (int j = i + 1; j < requiredSlots.Count; j++)
                            {
                                if (SlotsOverlap(requiredSlots[i], requiredSlots[j]))
                                    errors.Add($"Page {pi + 1}: Required slots '{requiredSlots[i].Name}' and '{requiredSlots[j].Name}' overlap.");
                            }
                        }
                    }
                }
            }

            return (errors.Count == 0, errors);
        }

        // ── Smart Variables extensions ────────────────────────────────────────────

        private const string SmartDataEntry = "smartdata.json";

        /// <summary>
        /// Save template plus SmartVariablesState into the same .apext ZIP.
        /// Re-opens the existing file and adds/replaces the smartdata.json entry.
        /// If the file doesn't exist yet, calls Save() first.
        /// </summary>
        public static void SaveWithSmartData(
            ApextTemplate template,
            SmartVariablesState smartState,
            string filePath,
            Dictionary<string, byte[]>? assets = null)
        {
            // Always write a fresh ZIP (avoids ZipArchiveMode.Update corruption edge-cases)
            template.ModifiedAt = DateTime.Now;

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // If file exists, read all existing entries except ones we're overwriting
            var existingEntries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(filePath))
            {
                using var fsRead = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var zipRead = new ZipArchive(fsRead, ZipArchiveMode.Read, leaveOpen: false);
                foreach (var entry in zipRead.Entries)
                {
                    if (entry.FullName.Equals("template.json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.FullName.Equals(SmartDataEntry, StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
                        && assets != null) continue; // will be re-written

                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    existingEntries[entry.FullName] = ms.ToArray();
                }
            }

            using var fsWrite = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var zipWrite = new ZipArchive(fsWrite, ZipArchiveMode.Create, leaveOpen: false);

            // template.json
            WriteEntry(zipWrite, "template.json", JsonSerializer.SerializeToUtf8Bytes(template, SerializerOptions));

            // smartdata.json
            smartState.LastSaved = DateTime.Now;
            WriteEntry(zipWrite, SmartDataEntry, JsonSerializer.SerializeToUtf8Bytes(smartState, SerializerOptions));

            // assets
            int assetCount = 0;
            if (assets != null)
            {
                foreach (var (key, bytes) in assets)
                {
                    if (bytes == null || bytes.Length == 0) continue;
                    WriteEntry(zipWrite, $"assets/{SanitizeAssetName(key)}", bytes);
                    assetCount++;
                }
            }

            // Preserved existing entries (e.g. assets from before if assets==null)
            foreach (var (name, bytes) in existingEntries)
            {
                WriteEntry(zipWrite, name, bytes);
                if (name.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    assetCount++;
            }

            // manifest.json
            var manifest = new
            {
                version = "2.1",
                hasSmartData = true,
                assetCount,
                pageCount = template.Pages.Count,
                templateId = template.Id,
                savedAt = DateTime.UtcNow.ToString("O")
            };
            WriteEntry(zipWrite, "manifest.json",
                JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>
        /// Load template and SmartVariablesState from a .apext file.
        /// If the file has no smartdata.json, returns a fresh default state.
        /// </summary>
        public static (ApextTemplate Template, Dictionary<string, byte[]> Assets, SmartVariablesState SmartState)
            LoadWithSmartData(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Template file not found: {filePath}", filePath);

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

            // template.json (required)
            var templateEntry = zip.GetEntry("template.json")
                ?? throw new InvalidDataException($"Invalid .apext: missing template.json in '{filePath}'");

            ApextTemplate template;
            using (var s = templateEntry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                template = JsonSerializer.Deserialize<ApextTemplate>(ms.ToArray(), SerializerOptions)
                    ?? throw new InvalidDataException("Failed to deserialize template.json");
            }

            // smartdata.json (optional — may be absent in old files)
            SmartVariablesState smartState = new();
            var smartEntry = zip.GetEntry(SmartDataEntry);
            if (smartEntry != null)
            {
                using var s = smartEntry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                smartState = JsonSerializer.Deserialize<SmartVariablesState>(ms.ToArray(), SerializerOptions)
                             ?? new SmartVariablesState();
            }

            // assets
            var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Length == 0) continue;
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                assets[entry.Name] = ms.ToArray();
            }

            return (template, assets, smartState);
        }

        private static void WriteEntry(ZipArchive zip, string entryName, byte[] data)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(data, 0, data.Length);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool SlotsOverlap(TemplateSlotDefinition a, TemplateSlotDefinition b)
        {
            bool noOverlap = a.X + a.Width <= b.X
                          || b.X + b.Width <= a.X
                          || a.Y + a.Height <= b.Y
                          || b.Y + b.Height <= a.Y;
            return !noOverlap;
        }

        private static string SanitizeAssetName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
