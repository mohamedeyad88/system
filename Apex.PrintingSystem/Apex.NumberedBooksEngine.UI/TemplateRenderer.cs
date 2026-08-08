using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace Apex.NumberedBooksEngine.UI
{
    public static class TemplateRenderer
    {
        public static BitmapSource RenderPreview(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            var format = ext == ".pdf" ? TemplateFormat.Pdf : TemplateFormat.Image;

            using var stream = File.OpenRead(path);
            using var loader = new TemplateLoader();

            // Load using the core loader
            using var skImage = loader.LoadTemplate(stream, format);

            // Convert to WPF BitmapSource
            return SKImageExtensions.ToBitmapSource(skImage);
        }
    }
}
