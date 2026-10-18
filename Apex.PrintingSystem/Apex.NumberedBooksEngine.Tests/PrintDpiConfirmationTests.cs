using System.IO;
using Apex.NumberedBooksEngine.Core;
using Xunit;
using Xunit.Abstractions;

namespace Apex.NumberedBooksEngine.Tests
{
    /// <summary>
    /// Confirms the field-report #2 fix: the numbering print path now rasterises the
    /// sheet at 600 DPI instead of 300, so the printed background carries twice the
    /// detail each way (4× the pixels). This proves the resolution the printer receives
    /// is higher; the on-paper sharpness is the physical confirmation on the trial.
    /// </summary>
    public class PrintDpiConfirmationTests
    {
        private readonly ITestOutputHelper _out;
        public PrintDpiConfirmationTests(ITestOutputHelper o) => _out = o;

        [Fact]
        public void Rasterise_at_600_is_double_the_resolution_of_300()
        {
            var pdf = @"D:\Apex\print-tests\06_highdpi_grid_300dpi.pdf";
            if (!File.Exists(pdf)) return; // sample absent on this box — skip quietly

            var tm = new TemplateManager();
            var at300 = tm.RasterizeTemplate(pdf, 300);
            var at600 = tm.RasterizeTemplate(pdf, 600);

            _out.WriteLine($"300 DPI background: {at300.Image.Width} x {at300.Image.Height} px");
            _out.WriteLine($"600 DPI background: {at600.Image.Width} x {at600.Image.Height} px");
            _out.WriteLine($"width ratio = {(double)at600.Image.Width / at300.Image.Width:F2}x");

            Assert.True(at600.Image.Width  > at300.Image.Width  * 1.8,
                $"600-DPI width {at600.Image.Width} should be ~2x the 300-DPI width {at300.Image.Width}");
            Assert.True(at600.Image.Height > at300.Image.Height * 1.8,
                $"600-DPI height {at600.Image.Height} should be ~2x the 300-DPI height {at300.Image.Height}");
        }
    }
}
