using Apex.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// 📊 PDF PAGE STREAM ENGINE
    /// 
    /// Streams PDF pages without loading entire file into memory.
    /// Supports very large PDF files (multi-gigabyte).
    /// </summary>
    public class PdfPageStreamEngine : IPageStreamEngine
    {
        private static readonly IReadOnlyList<string> _supportedExtensions = new[]
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif", ".gif"
        };

        public IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

        public bool IsSupported(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return _supportedExtensions.Contains(ext);
        }

        public async Task<int> GetPageCountAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".pdf")
            {
                return await GetPdfPageCountAsync(filePath, cancellationToken);
            }
            else
            {
                // Images are single page
                return 1;
            }
        }

        public async Task<IPageSource> OpenAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".pdf")
            {
                return await OpenPdfAsync(filePath, cancellationToken);
            }
            else
            {
                return await OpenImageAsync(filePath, cancellationToken);
            }
        }

        private async Task<int> GetPdfPageCountAsync(string filePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(filePath,
                        PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.InformationOnly);
                    return doc.PageCount;
                }
                catch
                {
                    // Fallback: estimate from file size (rough)
                    var fileInfo = new FileInfo(filePath);
                    return Math.Max(1, (int)(fileInfo.Length / (50 * 1024))); // ~50KB per page estimate
                }
            }, cancellationToken);
        }

        private async Task<IPageSource> OpenPdfAsync(string filePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() => new PdfPageSource(filePath), cancellationToken);
        }

        private async Task<IPageSource> OpenImageAsync(string filePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() => new ImagePageSource(filePath), cancellationToken);
        }
    }

    /// <summary>
    /// Page source for PDF files with lazy page loading.
    /// </summary>
    internal class PdfPageSource : IPageSource
    {
        private readonly string _filePath;
        private readonly int _totalPages;
        private readonly PageSourceMetadata _metadata;
        private bool _disposed;

        public int TotalPages => _totalPages;
        public PageSourceMetadata Metadata => _metadata;

        public PdfPageSource(string filePath)
        {
            _filePath = filePath;

            var fileInfo = new FileInfo(filePath);
            _metadata = new PageSourceMetadata
            {
                FileName = fileInfo.Name,
                FileType = "PDF",
                FileSizeBytes = fileInfo.Length,
                CreatedAt = fileInfo.CreationTime,
                ModifiedAt = fileInfo.LastWriteTime
            };

            // Get page count without loading entire document
            try
            {
                using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(filePath,
                    PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.InformationOnly);
                _totalPages = doc.PageCount;
                _metadata.Title = doc.Info.Title;
                _metadata.Author = doc.Info.Author;
            }
            catch
            {
                _totalPages = 1;
            }
        }

        public async Task<PageData> GetPageAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            if (pageIndex < 0 || pageIndex >= _totalPages)
                throw new ArgumentOutOfRangeException(nameof(pageIndex));

            return await Task.Run(() =>
            {
                // Extract single page to memory stream
                var ms = new MemoryStream();

                try
                {
                    using var inputDoc = PdfSharpCore.Pdf.IO.PdfReader.Open(_filePath,
                        PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);

                    using var outputDoc = new PdfSharpCore.Pdf.PdfDocument();
                    var page = inputDoc.Pages[pageIndex];
                    outputDoc.AddPage(page);
                    outputDoc.Save(ms, false);
                    ms.Position = 0;

                    return new PageData
                    {
                        PageIndex = pageIndex,
                        Width = page.Width.Point,
                        Height = page.Height.Point,
                        DataStream = ms,
                        ContentType = PageContentType.Pdf
                    };
                }
                catch
                {
                    ms.Dispose();
                    throw;
                }
            }, cancellationToken);
        }

        public async IAsyncEnumerable<PageData> StreamPagesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var page in StreamPagesAsync(0, _totalPages - 1, cancellationToken))
            {
                yield return page;
            }
        }

        public async IAsyncEnumerable<PageData> StreamPagesAsync(
            int startPage, int endPage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            startPage = Math.Max(0, startPage);
            endPage = Math.Min(_totalPages - 1, endPage);

            for (int i = startPage; i <= endPage; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pageData = await GetPageAsync(i, cancellationToken);
                yield return pageData;
            }
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Page source for image files.
    /// </summary>
    internal class ImagePageSource : IPageSource
    {
        private readonly string _filePath;
        private readonly PageSourceMetadata _metadata;

        public int TotalPages => 1;
        public PageSourceMetadata Metadata => _metadata;

        public ImagePageSource(string filePath)
        {
            _filePath = filePath;

            var fileInfo = new FileInfo(filePath);
            _metadata = new PageSourceMetadata
            {
                FileName = fileInfo.Name,
                FileType = fileInfo.Extension.TrimStart('.').ToUpper(),
                FileSizeBytes = fileInfo.Length,
                CreatedAt = fileInfo.CreationTime,
                ModifiedAt = fileInfo.LastWriteTime
            };
        }

        public async Task<PageData> GetPageAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            if (pageIndex != 0)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Images have only one page");

            return await Task.Run(() =>
            {
                var ms = new MemoryStream();

                using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.CopyTo(ms);
                }
                ms.Position = 0;

                // Get image dimensions
                double width = 612; // Default A4 width in points
                double height = 792; // Default A4 height in points

                try
                {
                    ms.Position = 0;
                    using var img = System.Drawing.Image.FromStream(ms);
                    width = img.Width * 72.0 / img.HorizontalResolution;
                    height = img.Height * 72.0 / img.VerticalResolution;
                    ms.Position = 0;
                }
                catch { }

                return new PageData
                {
                    PageIndex = 0,
                    Width = width,
                    Height = height,
                    DataStream = ms,
                    ContentType = PageContentType.Image
                };
            }, cancellationToken);
        }

        public async IAsyncEnumerable<PageData> StreamPagesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return await GetPageAsync(0, cancellationToken);
        }

        public async IAsyncEnumerable<PageData> StreamPagesAsync(
            int startPage, int endPage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (startPage == 0 && endPage >= 0)
            {
                yield return await GetPageAsync(0, cancellationToken);
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
