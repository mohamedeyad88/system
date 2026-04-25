using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    public class UniversalPrintPipeline : IUniversalPrintPipeline
    {
        private readonly IDocumentConverter _converter;
        private readonly IJobDistributionService _distributionService;
        private readonly ILoggerService _logger;

        public UniversalPrintPipeline(
            IDocumentConverter converter,
            IJobDistributionService distributionService, // OR PrintJobManager
            ILoggerService logger)
        {
            _converter = converter;
            _distributionService = distributionService;
            _logger = logger;
        }

        public bool IsFileSupported(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            var ext = Path.GetExtension(filePath);
            return _converter.IsFormatSupported(ext);
        }

        public async Task<PrintJob> ProcessAndQueueJobAsync(string filePath, string printerName, int copies = 1)
        {
            var settings = new PrintJobSettings { Copies = copies };
            return await ProcessAndQueueJobAsync(filePath, printerName, settings);
        }

        public async Task<PrintJob> ProcessAndQueueJobAsync(string filePath, string printerName, PrintJobSettings settings)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found", filePath);

            if (!IsFileSupported(filePath))
                throw new NotSupportedException($"File format '{Path.GetExtension(filePath)}' is not supported.");

            _logger.Log(LogLevel.Info, $"Pipeline: Processing file '{Path.GetFileName(filePath)}'", "UniversalPipeline", "Process");

            string finalPath = filePath;
            bool isTempFile = false;

            try
            {
                // 1. Conversion (if needed)
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext != ".pdf")
                {
                    _logger.Log(LogLevel.Info, $"Pipeline: Converting {ext} to PDF...", "UniversalPipeline", "Process");
                    var conversionResult = await _converter.ConvertToPdfAsync(filePath);
                    
                    if (!conversionResult.Success)
                    {
                        throw new InvalidOperationException($"Conversion failed: {conversionResult.ErrorMessage}");
                    }

                    finalPath = conversionResult.OutputPath;
                    isTempFile = true; 
                }
                
                // 1.1 Validation / Basic Pre-processing
                if (!ValidatePdf(finalPath))
                {
                    throw new InvalidDataException("The PDF file is corrupt or invalid.");
                }

                // 2. Job Creation with settings
                var job = _distributionService.CreatePrintJob(finalPath, settings.Copies);
                job.TargetPrinterName = printerName;
                job.OriginalFilePath = filePath;
                
                // Apply print settings
                job.Duplex = settings.Duplex;
                job.Color = settings.Color;
                job.PageRange = settings.PageRange;
                job.PaperSize = settings.PaperSize;
                job.Orientation = settings.Orientation;
                job.Quality = settings.Quality;
                
                // 3. Queueing
                await _distributionService.DistributeJobAsync(job);

                _logger.Log(LogLevel.Info, $"Pipeline: Job {job.Id} queued successfully.", "UniversalPipeline", "Process");
                
                return job;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Pipeline Error: {ex.Message}", "UniversalPipeline", "Process", ex);
                throw;
            }
        }

        private bool ValidatePdf(string pdfPath)
        {
            try
            {
                // First check: File exists and has content
                var fileInfo = new FileInfo(pdfPath);
                if (!fileInfo.Exists || fileInfo.Length < 100)
                {
                    _logger.Log(LogLevel.Warning, $"PDF file too small or doesn't exist: {pdfPath}", "UniversalPipeline", "Validate");
                    return false;
                }

                // Second check: Read first bytes to verify PDF header
                using (var fs = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] header = new byte[5];
                    fs.Read(header, 0, 5);
                    string headerStr = System.Text.Encoding.ASCII.GetString(header);
                    if (!headerStr.StartsWith("%PDF"))
                    {
                        _logger.Log(LogLevel.Warning, $"File does not have PDF header: {pdfPath}", "UniversalPipeline", "Validate");
                        return false;
                    }
                }

                // Third check: Try to open with PdfSharp (may fail for some PDFs)
                try
                {
                    using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(pdfPath, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.InformationOnly);
                    return doc.PageCount > 0;
                }
                catch
                {
                    // PdfSharp couldn't open it, but the header is valid
                    // We'll assume it's valid and let the printer handle it
                    _logger.Log(LogLevel.Warning, $"PdfSharp couldn't parse PDF, but header is valid. Proceeding anyway: {pdfPath}", "UniversalPipeline", "Validate");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, $"PDF Validation Failed for {pdfPath}: {ex.Message}", "UniversalPipeline", "Validate");
                return false;
            }
        }
    }
}
