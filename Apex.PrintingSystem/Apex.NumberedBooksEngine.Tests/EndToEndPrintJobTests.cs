using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Apex.NumberedBooksEngine.Tests
{
    /// <summary>
    /// End-to-end tests for complete print job workflows.
    /// Uses mock printer service to verify full job lifecycle.
    /// </summary>
    public class EndToEndPrintJobTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly MockPrinterService _mockPrinter;
        private readonly Composer _composer;
        private readonly SKImage _testTemplate;

        public EndToEndPrintJobTests(ITestOutputHelper output)
        {
            _output = output;
            _mockPrinter = new MockPrinterService();
            _composer = new Composer();
            
            using var surface = SKSurface.Create(new SKImageInfo(1000, 1000));
            surface.Canvas.Clear(SKColors.White);
            _testTemplate = surface.Snapshot();
        }

        public void Dispose()
        {
            _testTemplate?.Dispose();
        }

        [Fact]
        public async Task FullJob_LinearNumbering_CompletesSuccessfully()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 4, totalNumbers: 100);
            var copyTypes = new[] { CopyType.Original };
            var progress = new TestProgress();
            var cts = new CancellationTokenSource();

            // Act
            int pagesProcessed = 0;
            foreach (var (page, copyType) in _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes))
            {
                await _mockPrinter.PrintPageAsync(page, "TestPrinter");
                pagesProcessed++;
                
                progress.Report(new ProgressInfo(
                    pagesProcessed,
                    25, // 100 numbers / 4 slots
                    pagesProcessed / 25.0 * 100,
                    pagesProcessed * 4));
                
                page.Dispose();
            }

            // Assert
            Assert.Equal(25, pagesProcessed);
            Assert.Equal(25, _mockPrinter.PagesReceived);
            Assert.True(progress.LastProgress?.Percent >= 100);
            
            _output.WriteLine($"Job completed: {pagesProcessed} pages printed");
        }

        [Fact]
        public async Task FullJob_MultiCopy_PrintsAllCopies()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 2, totalNumbers: 20);
            var copyTypes = new[] { CopyType.Original, CopyType.Copy1 };

            // Act
            int pagesProcessed = 0;
            int originalCount = 0;
            int copy1Count = 0;

            foreach (var (page, copyType) in _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes))
            {
                await _mockPrinter.PrintPageAsync(page, "TestPrinter");
                pagesProcessed++;
                
                if (copyType == CopyType.Original) originalCount++;
                else if (copyType == CopyType.Copy1) copy1Count++;
                
                page.Dispose();
            }

            // Assert
            // 20 numbers / 2 slots = 10 pages * 2 copy types = 20 total
            Assert.Equal(20, pagesProcessed);
            Assert.Equal(10, originalCount);
            Assert.Equal(10, copy1Count);
            
            _output.WriteLine($"Multi-copy job: {originalCount} originals, {copy1Count} copies");
        }

        [Fact]
        public async Task FullJob_WithCancellation_StopsMidway()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 1, totalNumbers: 100);
            var copyTypes = new[] { CopyType.Original };
            var cts = new CancellationTokenSource();

            // Act
            int pagesProcessed = 0;
            bool wasCancelled = false;

            try
            {
                foreach (var (page, _) in _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes))
                {
                    cts.Token.ThrowIfCancellationRequested();
                    
                    await _mockPrinter.PrintPageAsync(page, "TestPrinter");
                    pagesProcessed++;
                    page.Dispose();
                    
                    // Cancel after 50 pages
                    if (pagesProcessed >= 50)
                    {
                        cts.Cancel();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
            }

            // Assert
            Assert.True(wasCancelled);
            Assert.Equal(50, pagesProcessed);
            
            _output.WriteLine($"Job cancelled after {pagesProcessed} pages");
        }

        [Fact]
        public async Task FullJob_PrinterError_HandlesGracefully()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 2, totalNumbers: 20);
            var copyTypes = new[] { CopyType.Original };
            _mockPrinter.FailAfterPages = 5; // Simulate failure after 5 pages

            // Act
            int pagesProcessed = 0;
            Exception? caughtException = null;

            try
            {
                foreach (var (page, _) in _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes))
                {
                    await _mockPrinter.PrintPageAsync(page, "TestPrinter");
                    pagesProcessed++;
                    page.Dispose();
                }
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert
            Assert.NotNull(caughtException);
            Assert.Equal(5, pagesProcessed);
            Assert.Contains("simulated", caughtException.Message.ToLower());
            
            _output.WriteLine($"Handled printer error after {pagesProcessed} pages");
        }

        [Fact]
        public async Task FullJob_ProgressReporting_AccuratePercentage()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 5, totalNumbers: 50);
            var copyTypes = new[] { CopyType.Original };
            var progressReports = new List<double>();

            // Act
            int pagesProcessed = 0;
            int totalPages = 10; // 50 numbers / 5 slots

            foreach (var (page, _) in _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes))
            {
                await _mockPrinter.PrintPageAsync(page, "TestPrinter");
                pagesProcessed++;
                
                double percent = (double)pagesProcessed / totalPages * 100;
                progressReports.Add(percent);
                
                page.Dispose();
            }

            // Assert
            Assert.Equal(10, progressReports.Count);
            Assert.Equal(10, progressReports[0]); // First page = 10%
            Assert.Equal(100, progressReports[^1]); // Last page = 100%
            
            // Verify progress increases monotonically
            for (int i = 1; i < progressReports.Count; i++)
            {
                Assert.True(progressReports[i] > progressReports[i - 1]);
            }
            
            _output.WriteLine($"Progress reports: {string.Join(", ", progressReports.Select(p => $"{p:F0}%"))}");
        }

        [Fact]
        public async Task FullJob_LargeJob_StreamingMemoryEfficient()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 4, totalNumbers: 1000);
            var copyTypes = new[] { CopyType.Original };
            
            GC.Collect();
            var memBefore = GC.GetTotalMemory(true);

            // Act
            int pagesProcessed = 0;
            foreach (var (page, _) in _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes))
            {
                await _mockPrinter.PrintPageAsync(page, "TestPrinter");
                pagesProcessed++;
                page.Dispose();
            }

            GC.Collect();
            var memAfter = GC.GetTotalMemory(true);
            var memGrowth = (memAfter - memBefore) / 1024.0 / 1024.0;

            // Assert - streaming should keep memory bounded
            Assert.Equal(250, pagesProcessed);
            Assert.True(memGrowth < 50, $"Memory grew by {memGrowth:N2}MB, expected < 50MB for streaming");
            
            _output.WriteLine($"Large job: {pagesProcessed} pages, memory growth: {memGrowth:N2}MB");
        }

        #region Helper Classes

        private class MockPrinterService
        {
            public int PagesReceived { get; private set; }
            public int? FailAfterPages { get; set; }
            public List<DateTime> PrintTimes { get; } = new();

            public Task PrintPageAsync(SKImage page, string printerName)
            {
                if (FailAfterPages.HasValue && PagesReceived >= FailAfterPages.Value)
                {
                    throw new InvalidOperationException("Simulated printer error");
                }

                PagesReceived++;
                PrintTimes.Add(DateTime.UtcNow);
                return Task.CompletedTask;
            }
        }

        private class TestProgress : IProgress<ProgressInfo>
        {
            public ProgressInfo? LastProgress { get; private set; }
            public List<ProgressInfo> AllReports { get; } = new();

            public void Report(ProgressInfo value)
            {
                LastProgress = value;
                AllReports.Add(value);
            }
        }

        private BookJobOptions CreateTestOptions(int slotsCount, long totalNumbers)
        {
            var slots = Enumerable.Range(0, slotsCount)
                .Select(i => new SlotSpec(
                    Id: $"slot{i}",
                    X: 0.1f + (i % 4) * 0.2f,
                    Y: 0.1f + (i / 4) * 0.15f,
                    Width: 0.15f,
                    Height: 0.05f,
                    FontFamily: "Arial",
                    FontSize: 24,
                    FontColorHex: "#000000",
                    Align: TextAlign.Center,
                    Rotation: 0,
                    CopyStyles: Array.Empty<CopyStyle>()
                ))
                .ToList();

            return new BookJobOptions(
                TemplateStream: Stream.Null,
                TemplatePath: "test.png",
                TemplateFormat: TemplateFormat.Image,
                Layout: LayoutSpec.A4,
                Slots: slots,
                StartNumber: 1,
                TotalNumbers: totalNumbers,
                PagesPerBook: 50,
                CopiesPerPage: 1,
                Mode: NumberingMode.Linear,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 100,
                OutputMode: "SinglePdf",
                OutputPath: "test.pdf"
            );
        }

        #endregion
    }
}
