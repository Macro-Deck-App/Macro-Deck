using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SuchByte.MacroDeck.Utils
{
    public class Base64
    {
        public static string GetBase64ByteArray(Bitmap originalBitmap, Size size, ContentAlignment contentAlignment = ContentAlignment.MiddleCenter)
        {
            var section = new Rectangle(new Point(0, (size.Width / 2) - (size.Height / 2)), new Size(size.Width, size.Height));
            switch (contentAlignment)
            {
                case ContentAlignment.TopLeft:
                case ContentAlignment.TopCenter:
                case ContentAlignment.TopRight:
                    section = new Rectangle(new Point(0, 0), new Size(size.Width, size.Height));
                    break;
                case ContentAlignment.MiddleLeft:
                case ContentAlignment.MiddleCenter:
                case ContentAlignment.MiddleRight:
                    section = new Rectangle(new Point(0, (size.Width / 2) - (size.Height / 2)), new Size(size.Width, size.Height));
                    break;
                case ContentAlignment.BottomLeft:
                case ContentAlignment.BottomCenter:
                case ContentAlignment.BottomRight:
                    section = new Rectangle(new Point(0, size.Width - size.Height), new Size(size.Width, size.Height));
                    break;
            }
            try
            {
                using (var scaledBitmap = new Bitmap(originalBitmap, new Size(size.Width, size.Width)))
                {
                    using (var bitmap = new Bitmap(section.Width, section.Height))
                    {
                        using (var g = Graphics.FromImage(bitmap))
                        {
                            g.DrawImage(scaledBitmap, 0, 0, section, GraphicsUnit.Pixel);
                        }

                        var totalPixels = bitmap.Width * bitmap.Height;
                        var currentPixels = 0;
                        var colorByte = 0;
                        var bitInBlock = 7;

                        var result = new byte[totalPixels / 8];

                        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                        var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadWrite, bitmap.PixelFormat);

                        var ptr = bitmapData.Scan0;
                        var bytes = bitmapData.Stride * bitmap.Height;
                        var rgbValues = new byte[bytes];

                        Marshal.Copy(ptr, rgbValues, 0, bytes);

                        var pixelByte = 0;

                        for (var y = 0; y < bitmap.Height; y++)
                        {
                            for (var x = 0; x < bitmap.Width; x++)
                            {
                                pixelByte = (y * bitmap.Width + x) * 4;

                                if (rgbValues[pixelByte + 2] < 128 ||   // Red
                                            rgbValues[pixelByte + 1] < 128 ||   // Green
                                            rgbValues[pixelByte + 0] < 128) // Blue
                                {
                                    colorByte &= ~(1 << bitInBlock);    // White/remove
                                }
                                else
                                {
                                    colorByte |= 1 << bitInBlock;       // Black/set
                                }

                                if (bitInBlock == 0)
                                {
                                    result[currentPixels] = (byte)colorByte;
                                    currentPixels++;
                                    bitInBlock = 7;
                                    colorByte = 0;
                                }
                                else
                                {
                                    bitInBlock--;
                                }
                            }
                        }
                        return Convert.ToBase64String(result);
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public static Image GetImageFromBase64(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return null;
            try
            {
                var whiteSpace = new HashSet<char> { '\t', '\n', '\r', ' ' };
                var length = base64.Count(c => !whiteSpace.Contains(c));
                if (length % 4 != 0)
                    base64 += new string('=', 4 - length % 4);
                var imageBytes = Convert.FromBase64String(base64);

                var ms = new MemoryStream(imageBytes);
                Image image = image = Image.FromStream(ms, true);

                return image;
            } catch
            {
                return null;
            }
            
        }

        public static string GetBase64FromImage(Image image)
        {
            if (image == null) return "";
            try
            {
                using (var ms = new MemoryStream())
                {
                    var format = image.RawFormat;
                    switch (format.ToString().ToLower())
                    {
                        case "gif":
                            break;
                        default:
                            image = new Bitmap(image); // Generating a new bitmap if the file format is not a gif because otherwise it causes a GDI+ error in some cases
                            format = ImageFormat.Png;
                            break;
                    }
                    image.Save(ms, format);
                    image.Dispose();

                    return Convert.ToBase64String(ms.ToArray());
                }
            } catch
            {
                return "";
            }
           
        }

    }
}
