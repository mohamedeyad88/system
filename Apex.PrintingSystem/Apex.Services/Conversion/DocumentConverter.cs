using Apex.Core.Interfaces;
using Apex.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Conversion
{
    /// <summary>
    /// Converts various document formats to print-ready PDFs.
    /// Supports: TXT, CSV, RTF, PDF, PNG, JPG, BMP, TIFF, HTML, DOCX, XLSX, PPTX, ODT, ODS, ODP
    /// </summary>
    public class DocumentConverter : IDocumentConverter
    {
        private static readonly string[] _supportedExtensions = new[]
        {
            // Text-based
            ".txt", ".csv", ".rtf",
            // PDF (passthrough)
            ".pdf",
            // Images
            ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif", ".gif",
            // Web
            ".html", ".htm",
            // Office (requires LibreOffice)
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            // OpenDocument
            ".odt", ".ods", ".odp"
        };

        private static readonly string[] _officeExtensions = new[]
        {
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf"
        };

        private readonly string _tempFolder;
        private readonly string? _libreOfficePath;

        public string[] SupportedExtensions => _supportedExtensions;

        public DocumentConverter(string? libreOfficePath = null)
        {
            _tempFolder = Path.Combine(Path.GetTempPath(), "ApexConversion");
            Directory.CreateDirectory(_tempFolder);
            
            // Try to find LibreOffice
            _libreOfficePath = libreOfficePath ?? FindLibreOffice();
        }

        public bool IsFormatSupported(string extension)
        {
            var ext = extension.StartsWith(".") ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
            return _supportedExtensions.Contains(ext);
        }

        public async Task<ConversionResult> ConvertToPdfAsync(string sourcePath, string? outputPath = null, CancellationToken ct = default)
        {
            if (!File.Exists(sourcePath))
            {
                return ConversionResult.Failed(sourcePath, "Source file not found.");
            }

            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            
            if (!IsFormatSupported(extension))
            {
                return ConversionResult.Failed(sourcePath, $"Unsupported format: {extension}");
            }

            outputPath ??= Path.Combine(_tempFolder, $"{Guid.NewGuid():N}.pdf");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                ConversionResult result;

                // PDF passthrough
                if (extension == ".pdf")
                {
                    File.Copy(sourcePath, outputPath, true);
                    var pages = await EstimatePagesAsync(sourcePath);
                    result = ConversionResult.Succeeded(sourcePath, outputPath, pages);
                    result.Status = ConversionStatus.Skipped;
                }
                // Images
                else if (IsImageFormat(extension))
                {
                    result = await ConvertImageToPdfAsync(sourcePath, outputPath, ct);
                }
                // Plain text
                else if (extension == ".txt" || extension == ".csv")
                {
                    result = await ConvertTextToPdfAsync(sourcePath, outputPath, ct);
                }
                // HTML
                else if (extension == ".html" || extension == ".htm")
                {
                    result = await ConvertHtmlToPdfAsync(sourcePath, outputPath, ct);
                }
                // Office formats (LibreOffice)
                else if (_officeExtensions.Contains(extension))
                {
                    result = await ConvertOfficeVialibreOfficeAsync(sourcePath, outputPath, ct);
                }
                else
                {
                    result = ConversionResult.Failed(sourcePath, $"No converter for {extension}");
                }

                stopwatch.Stop();
                result.ConversionTime = stopwatch.Elapsed;
                
                if (result.Success && File.Exists(result.OutputPath))
                {
                    result.FileSizeBytes = new FileInfo(result.OutputPath).Length;
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                return ConversionResult.Failed(sourcePath, "Conversion cancelled.");
            }
            catch (Exception ex)
            {
                return ConversionResult.Failed(sourcePath, $"Conversion error: {ex.Message}");
            }
        }

        public async Task<int> EstimatePagesAsync(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                if (extension == ".pdf")
                {
                    return await Task.Run(() => EstimatePdfPages(filePath));
                }
                else if (IsImageFormat(extension))
                {
                    return 1; // One page per image
                }
                else if (extension == ".txt" || extension == ".csv")
                {
                    var lineCount = File.ReadLines(filePath).Count();
                    return Math.Max(1, lineCount / 50); // ~50 lines per page
                }
                else
                {
                    // Rough estimate based on file size
                    var fileSize = new FileInfo(filePath).Length;
                    return Math.Max(1, (int)(fileSize / 50000)); // ~50KB per page
                }
            }
            catch
            {
                return 1;
            }
        }

        #region Format-specific converters

        private async Task<ConversionResult> ConvertImageToPdfAsync(string sourcePath, string outputPath, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                using var document = new PdfDocument();
                var page = document.AddPage();

                using var image = XImage.FromFile(sourcePath);
                
                // Scale image to fit page
                page.Width = XUnit.FromPoint(image.PixelWidth * 72 / image.HorizontalResolution);
                page.Height = XUnit.FromPoint(image.PixelHeight * 72 / image.VerticalResolution);

                // Limit to A4 max
                if (page.Width > XUnit.FromMillimeter(210))
                {
                    var scale = XUnit.FromMillimeter(210).Point / page.Width.Point;
                    page.Width = XUnit.FromMillimeter(210);
                    page.Height = XUnit.FromPoint(page.Height.Point * scale);
                }

                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(image, 0, 0, page.Width, page.Height);

                document.Save(outputPath);
                return ConversionResult.Succeeded(sourcePath, outputPath, 1);
            }, ct);
        }

        private async Task<ConversionResult> ConvertTextToPdfAsync(string sourcePath, string outputPath, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                using var document = new PdfDocument();
                var font = new XFont("Consolas", 10, XFontStyle.Regular);
                var lines = File.ReadAllLines(sourcePath);
                
                const int linesPerPage = 50;
                const double margin = 40;
                const double lineHeight = 14;

                int pageCount = 0;
                PdfPage? page = null;
                XGraphics? gfx = null;
                double y = 0;

                foreach (var line in lines)
                {
                    ct.ThrowIfCancellationRequested();

                    if (page == null || y > 800)
                    {
                        page = document.AddPage();
                        page.Size = PdfSharpCore.PageSize.A4;
                        gfx = XGraphics.FromPdfPage(page);
                        y = margin;
                        pageCount++;
                    }

                    gfx!.DrawString(line, font, XBrushes.Black, margin, y);
                    y += lineHeight;
                }

                document.Save(outputPath);
                return ConversionResult.Succeeded(sourcePath, outputPath, pageCount);
            }, ct);
        }

        private async Task<ConversionResult> ConvertHtmlToPdfAsync(string sourcePath, string outputPath, CancellationToken ct)
        {
            // For HTML, we'll try LibreOffice if available, otherwise create simple text PDF
            if (_libreOfficePath != null)
            {
                return await ConvertOfficeVialibreOfficeAsync(sourcePath, outputPath, ct);
            }

            // Fallback: extract text and convert
            var htmlContent = await File.ReadAllTextAsync(sourcePath, ct);
            var textContent = System.Text.RegularExpressions.Regex.Replace(htmlContent, "<[^>]+>", " ");
            textContent = System.Net.WebUtility.HtmlDecode(textContent);

            var tempTextFile = Path.Combine(_tempFolder, $"{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(tempTextFile, textContent, ct);

            try
            {
                return await ConvertTextToPdfAsync(tempTextFile, outputPath, ct);
            }
            finally
            {
                try { File.Delete(tempTextFile); } catch { }
            }
        }

        private async Task<ConversionResult> ConvertOfficeVialibreOfficeAsync(string sourcePath, string outputPath, CancellationToken ct)
        {
            if (_libreOfficePath == null)
            {
                return ConversionResult.Failed(sourcePath, 
                    "LibreOffice not found. Please install LibreOffice for Office format support.");
            }

            var outputDir = Path.GetDirectoryName(outputPath)!;
            var expectedOutput = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(sourcePath) + ".pdf");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _libreOfficePath,
                    Arguments = $"--headless --convert-to pdf --outdir \"{outputDir}\" \"{sourcePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return ConversionResult.Failed(sourcePath, "Failed to start LibreOffice.");
                }

                await process.WaitForExitAsync(ct);

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    return ConversionResult.Failed(sourcePath, $"LibreOffice error: {error}");
                }

                // Rename to expected output path if different
                if (expectedOutput != outputPath && File.Exists(expectedOutput))
                {
                    File.Move(expectedOutput, outputPath, true);
                }

                if (!File.Exists(outputPath))
                {
                    return ConversionResult.Failed(sourcePath, "Conversion produced no output.");
                }

                var pages = await EstimatePagesAsync(outputPath);
                return ConversionResult.Succeeded(sourcePath, outputPath, pages);
            }
            catch (Exception ex)
            {
                return ConversionResult.Failed(sourcePath, $"LibreOffice conversion failed: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private static bool IsImageFormat(string extension)
        {
            return extension switch
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".tif" or ".gif" => true,
                _ => false
            };
        }

        private static int EstimatePdfPages(string pdfPath)
        {
            try
            {
                using var document = PdfSharpCore.Pdf.IO.PdfReader.Open(pdfPath, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.InformationOnly);
                return document.PageCount;
            }
            catch
            {
                return 1;
            }
        }

        private static string? FindLibreOffice()
        {
            var possiblePaths = new[]
            {
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
                "/usr/bin/libreoffice",
                "/usr/bin/soffice"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            // Try to find in PATH
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "soffice",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadLine();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
            }
            catch { }

            return null;
        }

        #endregion
    }
}
