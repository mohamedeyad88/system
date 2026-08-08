using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Apex.NumberedBooksEngine.Tests
{
    /// <summary>
    /// Performance benchmarks for the NumberedBooksEngine.
    /// These tests measure throughput, memory usage, and scaling behavior.
    /// </summary>
    public class PerformanceBenchmarks : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Composer _composer;
        private readonly SKImage _testTemplate;

        public PerformanceBenchmarks(ITestOutputHelper output)
        {
            _output = output;
            _composer = new Composer();

            // Create test template (1000x1000 white image)
            using var surface = SKSurface.Create(new SKImageInfo(1000, 1000));
            surface.Canvas.Clear(SKColors.White);
            _testTemplate = surface.Snapshot();
        }

        public void Dispose()
        {
            _testTemplate?.Dispose();
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Benchmark_LinearStrategy_PageGeneration(int totalNumbers)
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 4, totalNumbers: totalNumbers);
            var strategy = new LinearNumberingStrategy();

            var sw = Stopwatch.StartNew();

            // Act
            int pageCount = 0;
            foreach (var pageNumbers in strategy.GeneratePageNumbers(options))
            {
                pageCount++;
            }

            sw.Stop();

            // Report
            _output.WriteLine($"LinearStrategy: {totalNumbers} numbers -> {pageCount} pages in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Throughput: {totalNumbers / Math.Max(sw.Elapsed.TotalSeconds, 0.001):N0} numbers/sec");

            // Assert - should complete in reasonable time
            Assert.True(sw.ElapsedMilliseconds < 1000, $"Generation took too long: {sw.ElapsedMilliseconds}ms");
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        public void Benchmark_Composer_PageRendering(int totalNumbers)
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 4, totalNumbers: totalNumbers);
            var copyTypes = new[] { CopyType.Original };

            GC.Collect();
            var memBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            // Act
            int pageCount = 0;
            foreach (var (page, _) in _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes))
            {
                page.Dispose(); // Important: dispose to avoid memory buildup
                pageCount++;
            }

            sw.Stop();
            var memAfter = GC.GetTotalMemory(false);

            // Report
            _output.WriteLine($"Composer Rendering: {pageCount} pages in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Pages/sec: {pageCount / Math.Max(sw.Elapsed.TotalSeconds, 0.001):N0}");
            _output.WriteLine($"  Memory delta: {(memAfter - memBefore) / 1024.0 / 1024.0:N2} MB");

            // Assert
            Assert.True(pageCount > 0);
        }

        [Fact]
        public void Benchmark_MultiCopy_Overhead()
        {
            // Measure overhead of multi-copy vs single-copy
            var options = CreateTestOptions(slotsCount: 4, totalNumbers: 500);

            // Single copy
            var sw1 = Stopwatch.StartNew();
            int singleCount = 0;
            foreach (var (page, _) in _composer.GenerateMultiCopyPages(_testTemplate, options, new[] { CopyType.Original }))
            {
                page.Dispose();
                singleCount++;
            }
            sw1.Stop();

            // Three copies
            var sw3 = Stopwatch.StartNew();
            int tripleCount = 0;
            foreach (var (page, _) in _composer.GenerateMultiCopyPages(_testTemplate, options,
                new[] { CopyType.Original, CopyType.Copy1, CopyType.Copy2 }))
            {
                page.Dispose();
                tripleCount++;
            }
            sw3.Stop();

            // Report
            _output.WriteLine($"Single copy: {singleCount} pages in {sw1.ElapsedMilliseconds}ms");
            _output.WriteLine($"Triple copy: {tripleCount} pages in {sw3.ElapsedMilliseconds}ms");
            _output.WriteLine($"Overhead ratio: {(double)sw3.ElapsedMilliseconds / sw1.ElapsedMilliseconds:N2}x");

            // Assert - triple copy should take ~3x time (not more due to overhead)
            Assert.True(tripleCount == singleCount * 3);
        }

        [Fact]
        public void Benchmark_MemoryEfficiency_LargeJob()
        {
            // Test that memory stays bounded during large job
            var options = CreateTestOptions(slotsCount: 4, totalNumbers: 2000);
            var copyTypes = new[] { CopyType.Original };

            GC.Collect();
            var memStart = GC.GetTotalMemory(true);
            long peakMemory = memStart;

            int pageCount = 0;
            foreach (var (page, _) in _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes))
            {
                page.Dispose();
                pageCount++;

                // Sample memory every 100 pages
                if (pageCount % 100 == 0)
                {
                    var currentMem = GC.GetTotalMemory(false);
                    if (currentMem > peakMemory) peakMemory = currentMem;
                }
            }

            GC.Collect();
            var memEnd = GC.GetTotalMemory(true);

            // Report
            _output.WriteLine($"Pages generated: {pageCount}");
            _output.WriteLine($"Start memory: {memStart / 1024.0 / 1024.0:N2} MB");
            _output.WriteLine($"Peak memory: {peakMemory / 1024.0 / 1024.0:N2} MB");
            _output.WriteLine($"End memory: {memEnd / 1024.0 / 1024.0:N2} MB");
            _output.WriteLine($"Memory growth: {(memEnd - memStart) / 1024.0 / 1024.0:N2} MB");

            // Assert - memory shouldn't grow excessively (less than 100MB growth for 2000 pages)
            var growth = (memEnd - memStart) / 1024.0 / 1024.0;
            Assert.True(growth < 100, $"Memory grew by {growth:N2}MB, expected < 100MB");
        }

        [Fact]
        public void Benchmark_StrategyComparison()
        {
            // Compare Linear vs Cutting strategy performance
            var options = CreateTestOptions(slotsCount: 4, totalNumbers: 5000);

            var linear = new LinearNumberingStrategy();
            var cutting = new CuttingStrategy();

            // Linear
            var swLinear = Stopwatch.StartNew();
            int linearPages = linear.GeneratePageNumbers(options).Count();
            swLinear.Stop();

            // Cutting
            var swCutting = Stopwatch.StartNew();
            int cuttingPages = cutting.GeneratePageNumbers(options).Count();
            swCutting.Stop();

            // Report
            _output.WriteLine($"Linear: {linearPages} pages in {swLinear.ElapsedMilliseconds}ms");
            _output.WriteLine($"Cutting: {cuttingPages} pages in {swCutting.ElapsedMilliseconds}ms");

            // Both should produce same page count
            Assert.Equal(linearPages, cuttingPages);
        }

        [Fact]
        public void Benchmark_OverlayDataGeneration()
        {
            // Benchmark overlay data generation (used for streaming print)
            var options = CreateTestOptions(slotsCount: 8, totalNumbers: 1000);

            var sw = Stopwatch.StartNew();
            int overlayCount = 0;

            foreach (var pageData in _composer.GenerateOverlayData(options))
            {
                overlayCount += pageData.Overlays.Count;
            }

            sw.Stop();

            _output.WriteLine($"Generated {overlayCount} overlays in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Overlays/sec: {overlayCount / Math.Max(sw.Elapsed.TotalSeconds, 0.001):N0}");
        }

        #region Helper Methods

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
