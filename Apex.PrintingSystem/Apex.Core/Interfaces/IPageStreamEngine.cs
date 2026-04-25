using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    /// <summary>
    /// 📊 PAGE STREAM ENGINE
    /// 
    /// Converts any supported file into a stream of pages.
    /// Uses lazy loading to prevent full file loading into memory.
    /// Supports very large files (multi-gigabyte).
    /// </summary>
    public interface IPageStreamEngine
    {
        /// <summary>
        /// Open a file and prepare for streaming.
        /// Does NOT load entire file into memory.
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A page source that can be iterated</returns>
        Task<IPageSource> OpenAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get total page count without loading all pages.
        /// </summary>
        Task<int> GetPageCountAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if file format is supported.
        /// </summary>
        bool IsSupported(string filePath);

        /// <summary>
        /// Supported file extensions.
        /// </summary>
        IReadOnlyList<string> SupportedExtensions { get; }
    }

    /// <summary>
    /// Represents a source of pages that can be streamed.
    /// </summary>
    public interface IPageSource : IAsyncDisposable
    {
        /// <summary>
        /// Total number of pages.
        /// </summary>
        int TotalPages { get; }

        /// <summary>
        /// File metadata.
        /// </summary>
        PageSourceMetadata Metadata { get; }

        /// <summary>
        /// Get a specific page by index (0-based).
        /// Uses lazy loading - only loads when requested.
        /// </summary>
        Task<PageData> GetPageAsync(int pageIndex, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stream pages one by one. Memory efficient for large files.
        /// </summary>
        IAsyncEnumerable<PageData> StreamPagesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stream pages in a specific range.
        /// </summary>
        IAsyncEnumerable<PageData> StreamPagesAsync(int startPage, int endPage, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a single page ready for printing.
    /// </summary>
    public class PageData : IDisposable
    {
        /// <summary>
        /// Page index (0-based).
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// Page number (1-based, for display).
        /// </summary>
        public int PageNumber => PageIndex + 1;

        /// <summary>
        /// Page width in points (1/72 inch).
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// Page height in points.
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// Raw page data stream (PDF/Image bytes).
        /// Disposed after printing to free memory.
        /// </summary>
        public Stream? DataStream { get; set; }

        /// <summary>
        /// Page content type.
        /// </summary>
        public PageContentType ContentType { get; set; }

        /// <summary>
        /// Whether page has been successfully printed.
        /// </summary>
        public bool IsPrinted { get; set; }

        public void Dispose()
        {
            DataStream?.Dispose();
            DataStream = null;
        }
    }

    /// <summary>
    /// Page content types.
    /// </summary>
    public enum PageContentType
    {
        Pdf,
        Image,
        Text,
        Vector,
        Mixed
    }

    /// <summary>
    /// Metadata about the page source.
    /// </summary>
    public class PageSourceMetadata
    {
        public string FileName { get; set; } = "";
        public string FileType { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public bool IsEncrypted { get; set; }
        public bool RequiresPassword { get; set; }
    }
}
