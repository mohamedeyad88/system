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

                // 2. Job Creation
                // We use the JobService to create the record.
                // Assuming JobService has a CreatePrintJob method that takes a path.
                var job = _distributionService.CreatePrintJob(finalPath, copies);
                job.TargetPrinterName = printerName;
                job.OriginalFilePath = filePath; // Keep track of original
                
                // 3. Queueing
                // We distribute it (Routing Engine will pick it up, or if printerName is set, it goes there)
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
                // Quick open to check validity
                using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(pdfPath, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.InformationOnly);
                return doc.PageCount > 0;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, $"PDF Validation Failed for {pdfPath}: {ex.Message}", "UniversalPipeline", "Validate");
                return false;
            }
        }
    }
}
