using Apex.Core.Utilities;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests
{
    public class CoordinateTransformTests
    {
        [Theory]
        [InlineData(100, 1000, 0.1)]
        [InlineData(500, 1000, 0.5)]
        [InlineData(0, 1000, 0)]
        [InlineData(1000, 1000, 1.0)]
        public void NormalizeX_ConvertsCorrectly(double uiX, double templateWidth, double expected)
        {
            var result = CoordinateTransform.NormalizeX(uiX, templateWidth);
            Assert.Equal(expected, result, precision: 6);
        }

        [Theory]
        [InlineData(0.5, 2000, 1000)]
        [InlineData(0.0, 2000, 0)]
        [InlineData(1.0, 2000, 2000)]
        [InlineData(0.25, 1200, 300)]
        public void ToPrinterX_ConvertsCorrectly(double normalizedX, int printWidth, int expected)
        {
            var result = CoordinateTransform.ToPrinterX(normalizedX, printWidth);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(12, 300, 96, 37.5)] // 12pt at 300dpi vs 96dpi preview
        [InlineData(24, 600, 96, 150)]
        [InlineData(10, 96, 96, 10)]    // Same DPI = no change
        public void FontSizeForPrint_ScalesCorrectly(double uiSize, double printDpi, double previewDpi, double expected)
        {
            var result = CoordinateTransform.FontSizeForPrint(uiSize, printDpi, previewDpi);
            Assert.Equal(expected, result, precision: 2);
        }

        [Theory]
        [InlineData(96, 96, 1.0)]
        [InlineData(300, 300, 1.0)]
        [InlineData(254, 100, 2.54)] // 254 pixels at 100 DPI = 2.54 inches
        public void PixelsToInches_ConvertsCorrectly(double pixels, double dpi, double expectedInches)
        {
            var result = CoordinateTransform.PixelsToInches(pixels, dpi);
            Assert.Equal(expectedInches, result, precision: 4);
        }

        [Theory]
        [InlineData(25.4, 100, 100)]  // 1 inch = 25.4mm at 100 DPI = 100 pixels
        [InlineData(50.8, 300, 600)]  // 2 inches = 50.8mm at 300 DPI = 600 pixels
        public void MmToPixels_ConvertsCorrectly(double mm, double dpi, double expectedPixels)
        {
            var result = CoordinateTransform.MmToPixels(mm, dpi);
            Assert.Equal(expectedPixels, result, precision: 2);
        }

        [Theory]
        [InlineData(-0.5, 0)]
        [InlineData(0.5, 0.5)]
        [InlineData(1.5, 1.0)]
        public void ClampNormalized_ClampsToValidRange(double input, double expected)
        {
            var result = CoordinateTransform.ClampNormalized(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0.1, 0.1, 0.1, 0.1, true)]    // Small slot, fits
        [InlineData(0.9, 0.9, 0.2, 0.2, false)]   // Extends beyond bounds
        [InlineData(0, 0, 1.0, 1.0, true)]        // Full page slot
        [InlineData(-0.1, 0.1, 0.1, 0.1, false)]  // Negative X
        public void IsWithinBounds_ValidatesCorrectly(double x, double y, double w, double h, bool expected)
        {
            var result = CoordinateTransform.IsWithinBounds(x, y, w, h);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void RoundTripConversion_PreservesPosition()
        {
            // Arrange
            double uiX = 150;
            double templateUiWidth = 600;
            int templatePrintWidth = 2400; // 4x scale

            // Act: UI -> Normalized -> Printer -> back to UI scale
            double normalized = CoordinateTransform.NormalizeX(uiX, templateUiWidth);
            int printerX = CoordinateTransform.ToPrinterX(normalized, templatePrintWidth);
            double backToUi = CoordinateTransform.ToUiX(normalized, templateUiWidth);

            // Assert
            Assert.Equal(0.25, normalized, precision: 6);
            Assert.Equal(600, printerX); // 0.25 * 2400
            Assert.Equal(uiX, backToUi, precision: 6);
        }
    }
}
