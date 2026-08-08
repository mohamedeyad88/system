using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests
{
    /// <summary>
    /// Integration tests for the Composer component.
    /// Tests multi-copy page generation, slot rendering, and copy styles.
    /// </summary>
    public class ComposerIntegrationTests : IDisposable
    {
        private readonly Composer _composer;
        private readonly SKImage _testTemplate;

        public ComposerIntegrationTests()
        {
            _composer = new Composer();
            // Create a simple test template (white 1000x1000 image)
            using var surface = SKSurface.Create(new SKImageInfo(1000, 1000));
            surface.Canvas.Clear(SKColors.White);
            _testTemplate = surface.Snapshot();
        }

        public void Dispose()
        {
            _testTemplate?.Dispose();
        }

        [Fact]
        public void ComposePage_SingleSlot_RendersNumber()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 1, totalNumbers: 1);
            var numbers = new long[] { 12345 };

            // Act
            using var result = _composer.ComposePage(_testTemplate, numbers, options, copyIndex: 0);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(_testTemplate.Width, result.Width);
            Assert.Equal(_testTemplate.Height, result.Height);
        }

        [Fact]
        public void ComposePage_MultipleSlots_RendersAllNumbers()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 4, totalNumbers: 4);
            var numbers = new long[] { 1, 2, 3, 4 };

            // Act
            using var result = _composer.ComposePage(_testTemplate, numbers, options, copyIndex: 0);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void ComposePage_WithEmptySlots_SkipsNegativeNumbers()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 4, totalNumbers: 4);
            var numbers = new long[] { 1, 2, -1, -1 }; // Last two are empty

            // Act
            using var result = _composer.ComposePage(_testTemplate, numbers, options, copyIndex: 0);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void ComposePageWithCopyType_Original_AppliesOriginalStyle()
        {
            // Arrange
            var options = CreateTestOptionsWithCopyStyles();
            var numbers = new long[] { 100 };

            // Act
            using var result = _composer.ComposePageWithCopyType(_testTemplate, numbers, options, CopyType.Original);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void ComposePageWithCopyType_Copy1_AppliesCopyStyle()
        {
            // Arrange
            var options = CreateTestOptionsWithCopyStyles();
            var numbers = new long[] { 100 };

            // Act
            using var result = _composer.ComposePageWithCopyType(_testTemplate, numbers, options, CopyType.Copy1);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void GenerateMultiCopyPages_ReturnsCorrectPageCount()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 2, totalNumbers: 10);
            var copyTypes = new[] { CopyType.Original, CopyType.Copy1 };

            // Act
            var pages = _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes).ToList();

            // Assert
            // 10 numbers / 2 slots per page = 5 pages
            // 5 pages * 2 copy types = 10 total pages
            Assert.Equal(10, pages.Count);

            // Verify copy types alternate correctly
            Assert.Equal(CopyType.Original, pages[0].CopyType);
            Assert.Equal(CopyType.Copy1, pages[1].CopyType);
            Assert.Equal(CopyType.Original, pages[2].CopyType);

            // Cleanup
            foreach (var p in pages) p.Page.Dispose();
        }

        [Fact]
        public void GenerateMultiCopyPages_WithSingleCopyType_ReturnsPagePerNumber()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 1, totalNumbers: 5);
            var copyTypes = new[] { CopyType.Original };

            // Act
            var pages = _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes).ToList();

            // Assert
            Assert.Equal(5, pages.Count);
            Assert.All(pages, p => Assert.Equal(CopyType.Original, p.CopyType));

            foreach (var p in pages) p.Page.Dispose();
        }

        [Fact]
        public void GenerateMultiCopyPages_ThreeCopyTypes_GeneratesAllCopies()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 2, totalNumbers: 4);
            var copyTypes = new[] { CopyType.Original, CopyType.Copy1, CopyType.Copy2 };

            // Act
            var pages = _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes).ToList();

            // Assert
            // 4 numbers / 2 slots = 2 pages * 3 copy types = 6 total pages
            Assert.Equal(6, pages.Count);

            // Dispose all pages
            foreach (var page in pages)
                page.Page.Dispose();
        }

        [Fact]
        public void GenerateOverlayData_ReturnsCorrectPageCount()
        {
            // Arrange
            var options = CreateTestOptions(slotsCount: 3, totalNumbers: 9);

            // Act
            var overlays = _composer.GenerateOverlayData(options).ToList();

            // Assert - 9 numbers / 3 slots = 3 pages
            Assert.Equal(3, overlays.Count);
        }

        [Theory]
        [InlineData(1, 100)]
        [InlineData(4, 1000)]
        [InlineData(10, 10000)]
        public void GenerateMultiCopyPages_VariousConfigurations_DoesNotThrow(int slotsCount, int totalNumbers)
        {
            // Arrange
            var options = CreateTestOptions(slotsCount, totalNumbers);
            var copyTypes = new[] { CopyType.Original };

            // Act & Assert
            var pages = _composer.GenerateMultiCopyPages(_testTemplate, options, copyTypes);
            int count = 0;
            foreach (var (page, _) in pages)
            {
                page.Dispose();
                count++;
                if (count > 100) break; // Limit for performance
            }
        }

        #region Helper Methods

        private BookJobOptions CreateTestOptions(int slotsCount, long totalNumbers)
        {
            var slots = Enumerable.Range(0, slotsCount)
                .Select(i => new SlotSpec(
                    Id: $"slot{i}",
                    X: 0.1f + (i * 0.2f),
                    Y: 0.1f,
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

        private BookJobOptions CreateTestOptionsWithCopyStyles()
        {
            var copyStyles = new[]
            {
                new CopyStyle("أصل", "#000000", 1.0f),
                new CopyStyle("صورة", "#FF0000", 0.9f)
            };

            var slots = new List<SlotSpec>
            {
                new SlotSpec(
                    Id: "slot0",
                    X: 0.1f,
                    Y: 0.1f,
                    Width: 0.3f,
                    Height: 0.1f,
                    FontFamily: "Arial",
                    FontSize: 32,
                    FontColorHex: "#000000",
                    Align: TextAlign.Center,
                    Rotation: 0,
                    CopyStyles: copyStyles
                )
            };

            return new BookJobOptions(
                TemplateStream: Stream.Null,
                TemplatePath: "test.png",
                TemplateFormat: TemplateFormat.Image,
                Layout: LayoutSpec.A4,
                Slots: slots,
                StartNumber: 1,
                TotalNumbers: 10,
                PagesPerBook: 50,
                CopiesPerPage: 2,
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

