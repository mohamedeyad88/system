using Apex.Core.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    /// <summary>
    /// Interface for document conversion services.
    /// Converts various document formats to print-ready PDFs.
    /// </summary>
    public interface IDocumentConverter
    {
        /// <summary>
        /// Converts a document to PDF format.
        /// </summary>
        /// <param name="sourcePath">Path to the source document.</param>
        /// <param name="outputPath">Path for the output PDF. If null, auto-generated in temp folder.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Conversion result with success status and output path.</returns>
        Task<ConversionResult> ConvertToPdfAsync(string sourcePath, string? outputPath = null, CancellationToken ct = default);

        /// <summary>
        /// Checks if the given file format is supported for conversion.
        /// </summary>
        bool IsFormatSupported(string extension);

        /// <summary>
        /// Gets list of all supported file extensions.
        /// </summary>
        string[] SupportedExtensions { get; }

        /// <summary>
        /// Estimates the number of pages in a document without full conversion.
        /// </summary>
        Task<int> EstimatePagesAsync(string filePath);
    }
}
