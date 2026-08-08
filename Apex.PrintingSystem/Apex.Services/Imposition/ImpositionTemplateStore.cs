using Apex.Core.Models.Imposition;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apex.Services.Imposition
{
    /// <summary>
    /// Persists <see cref="ImpositionTemplate"/> presets as JSON files in a per-user folder,
    /// and supports exporting/importing a single template to/from an arbitrary path so that
    /// job settings can be shared between machines.
    /// </summary>
    public class ImpositionTemplateStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _baseDir;

        /// <summary>
        /// Creates a store. <paramref name="baseDirectory"/> defaults to
        /// %LocalAppData%/ApexPrintingSystem/ImpositionTemplates (overridable for tests).
        /// </summary>
        public ImpositionTemplateStore(string? baseDirectory = null)
        {
            _baseDir = baseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ApexPrintingSystem", "ImpositionTemplates");
            Directory.CreateDirectory(_baseDir);
        }

        // ── Library operations ──────────────────────────────────────────────────

        /// <summary>Saves (creates or overwrites) a template by its Id. Returns the saved template.</summary>
        public ImpositionTemplate Save(ImpositionTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(template.Name))
                throw new ArgumentException("اسم القالب مطلوب.", nameof(template));

            template.ModifiedAt = DateTime.Now;
            File.WriteAllText(PathFor(template.Id), JsonSerializer.Serialize(template, JsonOpts));
            return template;
        }

        /// <summary>Returns all saved templates, newest first; skips any corrupt files.</summary>
        public IReadOnlyList<ImpositionTemplate> GetAll()
        {
            if (!Directory.Exists(_baseDir)) return Array.Empty<ImpositionTemplate>();

            var list = new List<ImpositionTemplate>();
            foreach (var file in Directory.EnumerateFiles(_baseDir, "*.json"))
            {
                var t = TryRead(file);
                if (t != null) list.Add(t);
            }
            return list.OrderByDescending(t => t.ModifiedAt).ToList();
        }

        public ImpositionTemplate? Load(string id)
        {
            var path = PathFor(id);
            return File.Exists(path) ? TryRead(path) : null;
        }

        public bool Delete(string id)
        {
            var path = PathFor(id);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        // ── Share between machines ──────────────────────────────────────────────

        /// <summary>Writes a template to an arbitrary file path (for sharing).</summary>
        public void ExportToFile(ImpositionTemplate template, string filePath)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("مسار التصدير مطلوب.", nameof(filePath));
            File.WriteAllText(filePath, JsonSerializer.Serialize(template, JsonOpts));
        }

        /// <summary>
        /// Reads a template from a file and saves it into the library under a fresh Id
        /// (so imports never overwrite existing presets). Returns the imported template.
        /// </summary>
        public ImpositionTemplate ImportFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("ملف القالب غير موجود.", filePath);

            var template = JsonSerializer.Deserialize<ImpositionTemplate>(
                File.ReadAllText(filePath), JsonOpts)
                ?? throw new InvalidDataException("ملف القالب غير صالح.");

            template.Id = Guid.NewGuid().ToString("N");
            template.CreatedAt = DateTime.Now;
            return Save(template);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private string PathFor(string id) => Path.Combine(_baseDir, $"{id}.json");

        private static ImpositionTemplate? TryRead(string path)
        {
            try
            {
                return JsonSerializer.Deserialize<ImpositionTemplate>(File.ReadAllText(path), JsonOpts);
            }
            catch
            {
                return null;
            }
        }
    }
}
