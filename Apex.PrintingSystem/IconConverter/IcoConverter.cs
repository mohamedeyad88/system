using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

public class IcoConverter
{
    public static void Convert(string pngPath, string icoPath)
    {
        using (FileStream stream = new FileStream(icoPath, FileMode.Create))
        {
            // Header
            stream.WriteByte(0); stream.WriteByte(0); // Reserved
            stream.WriteByte(1); stream.WriteByte(0); // Type (1=ICO)
            stream.WriteByte(1); stream.WriteByte(0); // Count (1 image)

            using (Bitmap bitmap = new Bitmap(pngPath))
            {
                int width = bitmap.Width;
                int height = bitmap.Height;
                if (width > 255) width = 0;
                if (height > 255) height = 0;

                stream.WriteByte((byte)width);
                stream.WriteByte((byte)height);
                stream.WriteByte(0); // Color count
                stream.WriteByte(0); // Reserved
                stream.WriteByte(1); stream.WriteByte(0); // Planes
                stream.WriteByte(32); stream.WriteByte(0); // Bit count

                using (MemoryStream memoryStream = new MemoryStream())
                {
                    bitmap.Save(memoryStream, ImageFormat.Png);
                    byte[] pngData = memoryStream.ToArray();
                    int size = pngData.Length;

                    stream.WriteByte((byte)(size & 0xFF));
                    stream.WriteByte((byte)((size >> 8) & 0xFF));
                    stream.WriteByte((byte)((size >> 16) & 0xFF));
                    stream.WriteByte((byte)((size >> 24) & 0xFF));

                    stream.WriteByte(22); stream.WriteByte(0); stream.WriteByte(0); stream.WriteByte(0); // Offset (22 bytes for header)

                    stream.Write(pngData, 0, pngData.Length);
                }
            }
        }
    }
}
