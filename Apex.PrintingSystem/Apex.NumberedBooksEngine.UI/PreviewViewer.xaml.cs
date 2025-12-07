using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Apex.NumberedBooksEngine.UI
{
    public partial class PreviewViewer : UserControl
    {
        public PreviewViewer()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the preview pages to display.
        /// </summary>
        public void SetPages(List<SKImage> pages)
        {
            var bitmapSources = new List<BitmapSource>();
            
            foreach (var page in pages)
            {
                bitmapSources.Add(SKImageExtensions.ToBitmapSource(page));
            }

            PagesContainer.ItemsSource = bitmapSources;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Find and close parent window if this is in a window
            var window = Window.GetWindow(this);
            window?.Close();
        }
    }
}
