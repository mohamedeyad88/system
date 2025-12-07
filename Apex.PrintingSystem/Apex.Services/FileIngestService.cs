using Apex.Core.Interfaces;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class FileIngestService : IFileIngestService
    {
        private static readonly string[] SupportedArchives = { ".zip", ".rar", ".7z", ".tar", ".gz" };
        private static readonly string[] SupportedDocuments = { ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".txt", ".png", ".jpg", ".jpeg", ".tiff" };

        public async Task<IList<IngestResult>> IngestFilesAsync(IEnumerable<string> paths, IngestOptions options)
        {
            var results = new List<IngestResult>();

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    await ProcessFileAsync(path, results, options);
                }
                else if (Directory.Exists(path))
                {
                    await ProcessDirectoryAsync(path, results, options);
                }
            }

            return results;
        }

        private async Task ProcessFileAsync(string path, List<IngestResult> results, IngestOptions options, bool isArchiveMember = false)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (options.ExtractArchives && SupportedArchives.Contains(ext))
            {
                await ExtractAndProcessArchiveAsync(path, results, options);
            }
            else if (SupportedDocuments.Contains(ext))
            {
                results.Add(new IngestResult
                {
                    FilePath = path,
                    OriginalName = Path.GetFileName(path),
                    EstimatedPages = EstimatePages(path), // Placeholder for now
                    IsArchiveMember = isArchiveMember,
                    SizeBytes = new FileInfo(path).Length
                });
            }
        }

        private async Task ProcessDirectoryAsync(string dirPath, List<IngestResult> results, IngestOptions options)
        {
            var searchOption = options.ScanSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(dirPath, "*.*", searchOption);

            foreach (var file in files)
            {
                await ProcessFileAsync(file, results, options);
            }
        }

        private async Task ExtractAndProcessArchiveAsync(string archivePath, List<IngestResult> results, IngestOptions options)
        {
            if (string.IsNullOrEmpty(options.TempFolder))
            {
                options.TempFolder = Path.Combine(Path.GetTempPath(), "ApexPrintManager", "Extracted");
            }

            var extractDir = Path.Combine(options.TempFolder, Path.GetFileNameWithoutExtension(archivePath) + "_" + Guid.NewGuid().ToString().Substring(0, 8));
            Directory.CreateDirectory(extractDir);

            await Task.Run(() =>
            {
                try
                {
                    using var archive = ArchiveFactory.Open(archivePath);
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        entry.WriteToDirectory(extractDir, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Log error (simulated)
                    Console.WriteLine($"Failed to extract {archivePath}: {ex.Message}");
                }
            });

            // Process extracted files
            await ProcessDirectoryAsync(extractDir, results, new IngestOptions 
            { 
                ScanSubfolders = true, 
                ExtractArchives = options.ExtractArchives, // Recursive extraction? Maybe limit depth.
                TempFolder = options.TempFolder 
            });
        }

        private int EstimatePages(string path)
        {
            // Simple estimation based on file type/size or just default 1
            // Real implementation would use PDF library or Office interop
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".pdf") return 1; // Needs PDF parser
            if (ext == ".docx") return 1;
            return 1;
        }
    }
}
