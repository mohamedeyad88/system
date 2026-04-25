using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using SkiaSharp;

namespace Apex.Services.Printing.ConsistentRIP
{
    /// <summary>
    /// OUTPUT GENERATOR - Creates execution-only data for printers.
    /// 
    /// CRITICAL:
    /// - Generate print-ready output in printer's native language
    /// - Output is EXECUTION-ONLY (no interpretation needed)
    /// - Printers must NOT:
    ///   * Re-scale
    ///   * Re-color
    ///   * Re-sharpen
    ///   * Re-interpret
    /// 
    /// SUPPORTED OUTPUT FORMATS:
    /// - RAW (raw bitmap data)
    /// - PCL (HP Printer Command Language)
    /// - PostScript (Adobe PostScript Level 2/3)
    /// - ESC/P (Epson)
    /// 
    /// DESIGN:
    /// Output contains EVERYTHING the printer needs:
    /// - Pre-rasterized bitmaps
    /// - Pre-processed colors
    /// - Pre-applied halftones
    /// - Print commands only (no content decisions)
    /// </summary>
    public class OutputGenerator
    {
        public OutputGenerator()
        {
            Debug.WriteLine("[OutputGen] ✓ Output Generator initialized");
        }
        
        /// <summary>
        /// Generates printer-specific output from rasterized document.
        /// </summary>
        public RIPOutput GenerateOutput(
            RasterizedDocument document,
            PrinterProfile printer,
            RIPOptions options)
        {
            Debug.WriteLine($"[OutputGen] Generating {printer.PrinterLanguage} output for {printer.PrinterName}");
            
            return printer.PrinterLanguage switch
            {
                PrinterLanguage.RAW => GenerateRawOutput(document, printer),
                PrinterLanguage.PCL => GeneratePCLOutput(document, printer, options),
                PrinterLanguage.PostScript => GeneratePostScriptOutput(document, printer, options),
                PrinterLanguage.ESC_P => GenerateESCPOutput(document, printer),
                _ => throw new NotSupportedException($"Printer language {printer.PrinterLanguage} not supported")
            };
        }
        
        /// <summary>
        /// Generates RAW bitmap output.
        /// Pure pixel data - printer just executes.
        /// </summary>
        private RIPOutput GenerateRawOutput(RasterizedDocument document, PrinterProfile printer)
        {
            using var ms = new MemoryStream();
            
            foreach (var page in document.Pages)
            {
                // Write bitmap as raw RGBA data
                var pixels = page.Bitmap.Bytes;
                ms.Write(pixels, 0, pixels.Length);
            }
            
            Debug.WriteLine($"[OutputGen] ✓ Generated RAW output: {ms.Length / 1024}KB");
            
            return new RIPOutput
            {
                Data = ms.ToArray(),
                PageCount = document.PageCount,
                OutputLanguage = PrinterLanguage.RAW,
                Success = true
            };
        }
        
        /// <summary>
        /// Generates HP PCL (Printer Command Language) output.
        /// Industry-standard for HP and compatible printers.
        /// </summary>
        private RIPOutput GeneratePCLOutput(RasterizedDocument document, PrinterProfile printer, RIPOptions options)
        {
            var pcl = new StringBuilder();
            
            // PCL Reset
            pcl.Append("\x1B" + "E");
            
            foreach (var page in document.Pages)
            {
                // Start page
                pcl.Append($"\x1B&l0O");  // Portrait
                pcl.Append($"\x1B&l26A"); // A4 paper
                
                // Set resolution
                pcl.Append($"\x1B*t{document.DPI}R");
                
                // Start raster graphics
                pcl.Append($"\x1B*r{page.WidthPixels}S");  // Source width
                pcl.Append($"\x1B*r{page.HeightPixels}T"); // Source height
                pcl.Append($"\x1B*r1A");                    // Start graphics
                
                // Send bitmap data row by row
                for (int y = 0; y < page.HeightPixels; y++)
                {
                    var rowData = ExtractRow(page.Bitmap, y);
                    
                    // Transfer raster data
                    pcl.Append($"\x1B*b{rowData.Length}W");
                    pcl.Append(Encoding.Latin1.GetString(rowData));
                }
                
                // End graphics
                pcl.Append("\x1B*rC");
                
                // Eject page
                pcl.Append("\x1B&l0H");
            }
            
            // PCL End
            pcl.Append("\x1B" + "E");
            
            var output = Encoding.Latin1.GetBytes(pcl.ToString());
            
            Debug.WriteLine($"[OutputGen] ✓ Generated PCL output: {output.Length / 1024}KB");
            
            return new RIPOutput
            {
                Data = output,
                PageCount = document.PageCount,
                OutputLanguage = PrinterLanguage.PCL,
                Success = true
            };
        }
        
        /// <summary>
        /// Generates PostScript output.
        /// Industry-standard for high-quality printing.
        /// </summary>
        private RIPOutput GeneratePostScriptOutput(RasterizedDocument document, PrinterProfile printer, RIPOptions options)
        {
            var ps = new StringBuilder();
            
            // PostScript header
            ps.AppendLine("%!PS-Adobe-3.0");
            ps.AppendLine("%%Creator: Apex Consistent RIP Engine");
            ps.AppendLine($"%%Pages: {document.PageCount}");
            ps.AppendLine("%%EndComments");
            
            int pageNum = 1;
            foreach (var page in document.Pages)
            {
                ps.AppendLine($"%%Page: {pageNum} {pageNum}");
                ps.AppendLine("gsave");
                
                // Set page dimensions
                float widthInPoints = page.WidthPixels * 72f / document.DPI;
                float heightInPoints = page.HeightPixels * 72f / document.DPI;
                
                ps.AppendLine($"0 0 translate");
                ps.AppendLine($"{widthInPoints} {heightInPoints} scale");
                
                // Define image
                ps.AppendLine($"/picstr {page.WidthPixels} string def");
                ps.AppendLine($"{page.WidthPixels} {page.HeightPixels} 8");
                ps.AppendLine($"[{page.WidthPixels} 0 0 -{page.HeightPixels} 0 {page.HeightPixels}]");
                ps.AppendLine("{currentfile picstr readhexstring pop}");
                ps.AppendLine("image");
                
                // Write bitmap data as hex
                var hexData = BitmapToHex(page.Bitmap);
                ps.AppendLine(hexData);
                
                ps.AppendLine("grestore");
                ps.AppendLine("showpage");
                
                pageNum++;
            }
            
            ps.AppendLine("%%EOF");
            
            var output = Encoding.ASCII.GetBytes(ps.ToString());
            
            Debug.WriteLine($"[OutputGen] ✓ Generated PostScript output: {output.Length / 1024}KB");
            
            return new RIPOutput
            {
                Data = output,
                PageCount = document.PageCount,
                OutputLanguage = PrinterLanguage.PostScript,
                Success = true
            };
        }
        
        /// <summary>
        /// Generates ESC/P output (Epson printers).
        /// </summary>
        private RIPOutput GenerateESCPOutput(RasterizedDocument document, PrinterProfile printer)
        {
            var escp = new MemoryStream();
            
            foreach (var page in document.Pages)
            {
                // ESC @ - Initialize printer
                escp.Write(new byte[] { 0x1B, 0x40 }, 0, 2);
                
                // Set graphics mode
                // ESC * m nL nH d1...dk
                // This would contain detailed ESC/P commands
                
                // For now, send as raw bitmap
                var pixels = page.Bitmap.Bytes;
                escp.Write(pixels, 0, pixels.Length);
                
                // Form feed
                escp.WriteByte(0x0C);
            }
            
            var output = escp.ToArray();
            
            Debug.WriteLine($"[OutputGen] ✓ Generated ESC/P output: {output.Length / 1024}KB");
            
            return new RIPOutput
            {
                Data = output,
                PageCount = document.PageCount,
                OutputLanguage = PrinterLanguage.ESC_P,
                Success = true
            };
        }
        
        /// <summary>
        /// Extracts a single row from bitmap for PCL.
        /// </summary>
        private byte[] ExtractRow(SKBitmap bitmap, int row)
        {
            var rowData = new byte[bitmap.Width * 3]; // RGB
            
            unsafe
            {
                var pixels = (SKColor*)bitmap.GetPixels();
                int rowStart = row * bitmap.Width;
                
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = pixels[rowStart + x];
                    rowData[x * 3 + 0] = pixel.Red;
                    rowData[x * 3 + 1] = pixel.Green;
                    rowData[x * 3 + 2] = pixel.Blue;
                }
            }
            
            return rowData;
        }
        
        /// <summary>
        /// Converts bitmap to hex string for PostScript.
        /// </summary>
        private string BitmapToHex(SKBitmap bitmap)
        {
            var sb = new StringBuilder();
            
            unsafe
            {
                var pixels = (SKColor*)bitmap.GetPixels();
                
                for (int i = 0; i < bitmap.Width * bitmap.Height; i++)
                {
                    var pixel = pixels[i];
                    
                    // Convert to grayscale for PS Level 2
                    byte gray = (byte)((pixel.Red + pixel.Green + pixel.Blue) / 3);
                    
                    sb.Append(gray.ToString("X2"));
                    
                    // Line break every 40 bytes for readability
                    if (i % 40 == 39)
                        sb.AppendLine();
                }
            }
            
            return sb.ToString();
        }
    }
}
