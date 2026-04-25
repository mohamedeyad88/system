using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apex.Services.Templates
{
    public class TemplateLibraryEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public int PageCount { get; set; }
        public List<string> Tags { get; set; } = new();
        public string? PreviewBase64 { get; set; }
    }

    public class TemplateLibrary
    {
        // ── Singleton ────────────────────────────────────────────────────────────

        private static readonly Lazy<TemplateLibrary> _instance =
            new(() => new TemplateLibrary());

        public static TemplateLibrary Instance => _instance.Value;

        // ── Fields ───────────────────────────────────────────────────────────────

        private List<TemplateLibraryEntry> _entries = new();
        private readonly object _lock = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        // ── Constructor ──────────────────────────────────────────────────────────

        private TemplateLibrary()
        {
            LibraryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apex", "Templates");

            Directory.CreateDirectory(LibraryPath);
            LoadIndex();
        }

        // ── Properties ───────────────────────────────────────────────────────────

        /// <summary>Library root: %AppData%\Apex\Templates\</summary>
        public string LibraryPath { get; }

        private string IndexFilePath => Path.Combine(LibraryPath, "library-index.json");

        // ── Query ─────────────────────────────────────────────────────────────────

        /// <summary>Get all templates in the library.</summary>
        public IReadOnlyList<TemplateLibraryEntry> GetAll()
        {
            lock (_lock)
                return _entries.AsReadOnly();
        }

        /// <summary>Get templates filtered by category (case-insensitive).</summary>
        public IReadOnlyList<TemplateLibraryEntry> GetByCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return GetAll();
            lock (_lock)
                return _entries
                    .Where(e => e.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                    .AsReadOnly();
        }

        /// <summary>
        /// Search templates by name, category, or tags (case-insensitive, partial match).
        /// </summary>
        public IReadOnlyList<TemplateLibraryEntry> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return GetAll();

            string q = query.Trim().ToLowerInvariant();
            lock (_lock)
                return _entries
                    .Where(e =>
                        e.Name.ToLowerInvariant().Contains(q) ||
                        e.Category.ToLowerInvariant().Contains(q) ||
                        e.Tags.Any(t => t.ToLowerInvariant().Contains(q)))
                    .ToList()
                    .AsReadOnly();
        }

        // ── Add ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Add an existing .apext file to the library (copies file to library folder).
        /// </summary>
        public TemplateLibraryEntry Add(string sourceFilePath, string? name = null)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"Source file not found: {sourceFilePath}", sourceFilePath);

            (ApextTemplate template, _) = ApextFileFormat.Load(sourceFilePath);

            if (!string.IsNullOrWhiteSpace(name))
                template.Name = name;

            string destFile = BuildDestinationPath(template.Id);
            File.Copy(sourceFilePath, destFile, overwrite: true);

            TemplateLibraryEntry entry = BuildEntry(template, destFile);

            lock (_lock)
            {
                RemoveById(template.Id);
                _entries.Add(entry);
                SaveIndex();
            }

            return entry;
        }

        /// <summary>
        /// Add an in-memory template to the library (saves as new .apext file).
        /// </summary>
        public TemplateLibraryEntry Add(ApextTemplate template, Dictionary<string, byte[]>? assets = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            // Assign new Id if empty
            if (string.IsNullOrWhiteSpace(template.Id))
                template.Id = Guid.NewGuid().ToString();

            string destFile = BuildDestinationPath(template.Id);
            ApextFileFormat.Save(template, destFile, assets);

            TemplateLibraryEntry entry = BuildEntry(template, destFile);

            lock (_lock)
            {
                RemoveById(template.Id);
                _entries.Add(entry);
                SaveIndex();
            }

            return entry;
        }

        // ── Load ──────────────────────────────────────────────────────────────────

        /// <summary>Load the full template (and assets) from the library by Id.</summary>
        public (ApextTemplate Template, Dictionary<string, byte[]> Assets) Load(string templateId)
        {
            TemplateLibraryEntry entry;
            lock (_lock)
            {
                entry = _entries.FirstOrDefault(e => e.Id == templateId)
                        ?? throw new KeyNotFoundException($"Template '{templateId}' not found in library.");
            }

            if (!File.Exists(entry.FilePath))
                throw new FileNotFoundException($"Template file missing from disk: {entry.FilePath}", entry.FilePath);

            return ApextFileFormat.Load(entry.FilePath);
        }

        // ── Delete ────────────────────────────────────────────────────────────────

        /// <summary>Delete a template from the library and disk.</summary>
        public bool Delete(string templateId)
        {
            lock (_lock)
            {
                TemplateLibraryEntry? entry = _entries.FirstOrDefault(e => e.Id == templateId);
                if (entry == null) return false;

                try
                {
                    if (File.Exists(entry.FilePath))
                        File.Delete(entry.FilePath);
                }
                catch { /* best-effort file deletion */ }

                _entries.Remove(entry);
                SaveIndex();
                return true;
            }
        }

        // ── Categories ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns built-in category names plus any custom categories present in the library.
        /// </summary>
        public List<string> GetCategories()
        {
            List<string> builtIn = new()
            {
                "شهادات",
                "بطاقات",
                "فواتير",
                "ملصقات",
                "عام"
            };

            lock (_lock)
            {
                foreach (string cat in _entries.Select(e => e.Category).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!builtIn.Contains(cat, StringComparer.OrdinalIgnoreCase))
                        builtIn.Add(cat);
                }
            }

            return builtIn;
        }

        // ── Refresh ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Scan the library folder for .apext files, rebuild the index from disk.
        /// </summary>
        public void Refresh()
        {
            List<TemplateLibraryEntry> freshEntries = new();

            string[] files = Directory.GetFiles(LibraryPath, $"*{ApextFileFormat.FileExtension}",
                                                SearchOption.TopDirectoryOnly);

            foreach (string file in files)
            {
                try
                {
                    // Load only template.json metadata (lightweight: no assets)
                    (ApextTemplate template, _) = ApextFileFormat.Load(file);
                    TemplateLibraryEntry entry = BuildEntry(template, file);
                    freshEntries.Add(entry);
                }
                catch
                {
                    // Skip corrupted files
                }
            }

            lock (_lock)
            {
                _entries = freshEntries;
                SaveIndex();
            }
        }

        // ── Sample Templates ──────────────────────────────────────────────────────

        /// <summary>
        /// Create 3 built-in sample templates and add them to the library.
        /// </summary>
        public void CreateSampleTemplates()
        {
            // 1. شهادة تقدير — Certificate
            ApextTemplate certificate = ApextFileFormat.CreateNew("شهادة تقدير", "شهادات");
            certificate.Description = "قالب شهادة تقدير قياسي";
            certificate.Author = "Apex PrintingSystem";
            certificate.Tags = new List<string> { "شهادة", "تقدير", "رسمي" };
            TemplatePageDefinition certPage = certificate.Pages[0];
            certPage.WidthMm = 297;   // A4 Landscape
            certPage.HeightMm = 210;
            certPage.Orientation = PageOrientation.Landscape;
            certPage.Slots = new List<TemplateSlotDefinition>
            {
                new()
                {
                    Name = "الاسم",
                    VariableName = "Name",
                    DataType = SlotDataType.Text,
                    X = 50, Y = 80, Width = 197, Height = 20,
                    FontFamily = "Tahoma", FontSize = 24,
                    Bold = true, IsRtl = true,
                    TextAlign = HorizontalAlign.Center,
                    VerticalAlign = VerticalAlign.Middle,
                    Required = true
                },
                new()
                {
                    Name = "المسمى الوظيفي",
                    VariableName = "Title",
                    DataType = SlotDataType.Text,
                    X = 50, Y = 110, Width = 197, Height = 14,
                    FontFamily = "Tahoma", FontSize = 16,
                    IsRtl = true,
                    TextAlign = HorizontalAlign.Center,
                    VerticalAlign = VerticalAlign.Middle
                },
                new()
                {
                    Name = "التاريخ",
                    VariableName = "Date",
                    DataType = SlotDataType.Date,
                    X = 200, Y = 175, Width = 70, Height = 10,
                    FontFamily = "Tahoma", FontSize = 11,
                    IsRtl = true,
                    FormatString = "dd/MM/yyyy",
                    DefaultValue = DateTime.Today.ToString("dd/MM/yyyy"),
                    TextAlign = HorizontalAlign.Center,
                    VerticalAlign = VerticalAlign.Middle
                },
                new()
                {
                    Name = "الرقم التسلسلي",
                    VariableName = "Number",
                    DataType = SlotDataType.Counter,
                    X = 20, Y = 175, Width = 60, Height = 10,
                    FontFamily = "Tahoma", FontSize = 11,
                    FormatString = "000000",
                    DefaultValue = "000001",
                    TextAlign = HorizontalAlign.Left,
                    VerticalAlign = VerticalAlign.Middle,
                    IsRtl = false
                }
            };
            Add(certificate);

            // 2. بطاقة عمل — Business Card
            ApextTemplate businessCard = ApextFileFormat.CreateNew("بطاقة عمل", "بطاقات");
            businessCard.Description = "قالب بطاقة عمل احترافي";
            businessCard.Author = "Apex PrintingSystem";
            businessCard.Tags = new List<string> { "بطاقة", "عمل", "اعمال" };
            TemplatePageDefinition cardPage = businessCard.Pages[0];
            cardPage.WidthMm = 85.6;   // Standard business card
            cardPage.HeightMm = 54;
            cardPage.Orientation = PageOrientation.Landscape;
            cardPage.BackgroundColor = "#F8F8F8";
            cardPage.Slots = new List<TemplateSlotDefinition>
            {
                new()
                {
                    Name = "الاسم",
                    VariableName = "Name",
                    DataType = SlotDataType.Text,
                    X = 5, Y = 8, Width = 75, Height = 12,
                    FontFamily = "Tahoma", FontSize = 14,
                    Bold = true, IsRtl = true,
                    TextAlign = HorizontalAlign.Right,
                    VerticalAlign = VerticalAlign.Middle,
                    Required = true
                },
                new()
                {
                    Name = "المسمى الوظيفي",
                    VariableName = "Title",
                    DataType = SlotDataType.Text,
                    X = 5, Y = 22, Width = 75, Height = 9,
                    FontFamily = "Tahoma", FontSize = 10,
                    IsRtl = true,
                    TextAlign = HorizontalAlign.Right,
                    VerticalAlign = VerticalAlign.Middle
                },
                new()
                {
                    Name = "الهاتف",
                    VariableName = "Phone",
                    DataType = SlotDataType.Text,
                    X = 5, Y = 33, Width = 75, Height = 8,
                    FontFamily = "Tahoma", FontSize = 9,
                    IsRtl = false,
                    TextAlign = HorizontalAlign.Left,
                    VerticalAlign = VerticalAlign.Middle
                },
                new()
                {
                    Name = "البريد الإلكتروني",
                    VariableName = "Email",
                    DataType = SlotDataType.Text,
                    X = 5, Y = 43, Width = 75, Height = 8,
                    FontFamily = "Tahoma", FontSize = 9,
                    IsRtl = false,
                    TextAlign = HorizontalAlign.Left,
                    VerticalAlign = VerticalAlign.Middle
                }
            };
            Add(businessCard);

            // 3. ملصق عنوان — Address Label
            ApextTemplate addressLabel = ApextFileFormat.CreateNew("ملصق عنوان", "ملصقات");
            addressLabel.Description = "قالب ملصق العنوان البريدي";
            addressLabel.Author = "Apex PrintingSystem";
            addressLabel.Tags = new List<string> { "ملصق", "عنوان", "بريد" };
            TemplatePageDefinition labelPage = addressLabel.Pages[0];
            labelPage.WidthMm = 101.6;  // 4-inch label
            labelPage.HeightMm = 63.5;  // 2.5-inch label
            labelPage.Orientation = PageOrientation.Landscape;
            labelPage.Slots = new List<TemplateSlotDefinition>
            {
                new()
                {
                    Name = "الاسم",
                    VariableName = "Name",
                    DataType = SlotDataType.Text,
                    X = 5, Y = 5, Width = 91, Height = 12,
                    FontFamily = "Tahoma", FontSize = 13,
                    Bold = true, IsRtl = true,
                    TextAlign = HorizontalAlign.Right,
                    VerticalAlign = VerticalAlign.Middle,
                    Required = true
                },
                new()
                {
                    Name = "العنوان",
                    VariableName = "Address",
                    DataType = SlotDataType.Text,
                    X = 5, Y = 19, Width = 91, Height = 14,
                    FontFamily = "Tahoma", FontSize = 11,
                    IsRtl = true,
                    TextAlign = HorizontalAlign.Right,
                    VerticalAlign = VerticalAlign.Top
                },
                new()
                {
                    Name = "المدينة",
                    VariableName = "City",
                    DataType = SlotDataType.Text,
                    X = 5, Y = 35, Width = 55, Height = 10,
                    FontFamily = "Tahoma", FontSize = 11,
                    IsRtl = true,
                    TextAlign = HorizontalAlign.Right,
                    VerticalAlign = VerticalAlign.Middle
                },
                new()
                {
                    Name = "الرمز البريدي",
                    VariableName = "PostalCode",
                    DataType = SlotDataType.Text,
                    X = 62, Y = 35, Width = 34, Height = 10,
                    FontFamily = "Tahoma", FontSize = 11,
                    IsRtl = false,
                    TextAlign = HorizontalAlign.Left,
                    VerticalAlign = VerticalAlign.Middle
                }
            };
            Add(addressLabel);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private string BuildDestinationPath(string templateId)
        {
            string safeName = templateId.Replace("-", "").Replace("{", "").Replace("}", "");
            return Path.Combine(LibraryPath, $"{safeName}{ApextFileFormat.FileExtension}");
        }

        private static TemplateLibraryEntry BuildEntry(ApextTemplate template, string filePath)
        {
            return new TemplateLibraryEntry
            {
                Id = template.Id,
                Name = template.Name,
                Category = template.Category,
                FilePath = filePath,
                CreatedAt = template.CreatedAt,
                ModifiedAt = template.ModifiedAt,
                PageCount = template.Pages?.Count ?? 0,
                Tags = template.Tags != null ? new List<string>(template.Tags) : new List<string>(),
                PreviewBase64 = template.PreviewBase64
            };
        }

        private void RemoveById(string id)
        {
            // Must be called inside lock
            TemplateLibraryEntry? existing = _entries.FirstOrDefault(e => e.Id == id);
            if (existing != null) _entries.Remove(existing);
        }

        private void LoadIndex()
        {
            if (!File.Exists(IndexFilePath))
            {
                _entries = new List<TemplateLibraryEntry>();
                return;
            }

            try
            {
                string json = File.ReadAllText(IndexFilePath, System.Text.Encoding.UTF8);
                List<TemplateLibraryEntry>? loaded =
                    JsonSerializer.Deserialize<List<TemplateLibraryEntry>>(json, JsonOpts);
                _entries = loaded ?? new List<TemplateLibraryEntry>();

                // Remove stale entries whose files no longer exist
                _entries.RemoveAll(e => !File.Exists(e.FilePath));
            }
            catch
            {
                _entries = new List<TemplateLibraryEntry>();
            }
        }

        private void SaveIndex()
        {
            // Must be called inside lock
            try
            {
                string json = JsonSerializer.Serialize(_entries, JsonOpts);
                File.WriteAllText(IndexFilePath, json, System.Text.Encoding.UTF8);
            }
            catch { /* best-effort index persistence */ }
        }
    }
}
