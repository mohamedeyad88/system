using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SkiaSharp;
using System;
using System.IO;

namespace Apex.NumberedBooksEngine.Core
{
    public class PdfStreamer : IDisposable
    {
        private readonly PdfDocument _document;
        private readonly Stream _outputStream;

        public PdfStreamer(Stream outputStream)
        {
            _outputStream = outputStream;
            _document = new PdfDocument();
        }

        public void AddPage(SKImage pageImage)
        {
            var page = _document.AddPage();
            
            // Set page size to match image
            page.Width = pageImage.Width * 72 / 96.0; // Convert px (96dpi) to points (72dpi) approx
            page.Height = pageImage.Height * 72 / 96.0;

            using var gfx = XGraphics.FromPdfPage(page);
            
            // Convert SKImage to byte array for PdfSharp
            using var data = pageImage.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            
            var xImage = XImage.FromStream(() => ms);
            gfx.DrawImage(xImage, 0, 0, page.Width, page.Height);
        }

        public void Save()
        {
            _document.Save(_outputStream);
        }

        public void Dispose()
        {
            _document.Close();
            _document.Dispose();
        }
    }
}
