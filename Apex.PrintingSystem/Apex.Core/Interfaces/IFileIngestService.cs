using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public class IngestOptions
    {
        public bool ScanSubfolders { get; set; }
        public bool ExtractArchives { get; set; }
        public string TempFolder { get; set; } = string.Empty;
    }

    public class IngestResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public int EstimatedPages { get; set; }
        public bool IsArchiveMember { get; set; }
        public long SizeBytes { get; set; }
    }

    public interface IFileIngestService
    {
        Task<IList<IngestResult>> IngestFilesAsync(IEnumerable<string> paths, IngestOptions options);
    }
}
