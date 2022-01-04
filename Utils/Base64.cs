using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SuchByte.MacroDeck.Utils
{
    public class Base64
    {
        public static string GetBase64ByteArray(Bitmap originalBitmap, Size size, ContentAlignment contentAlignment = ContentAlignment.MiddleCenter)
        {
            Rectangle section = new Rectangle(new Point(0, (size.Width / 2) - (size.Height / 2)), new Size(size.Width, size.Height));
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

                        int totalPixels = bitmap.Width * bitmap.Height;
                        int currentPixels = 0;
                        int colorByte = 0;
                        int bitInBlock = 7;

                        byte[] result = new byte[totalPixels / 8];

                        Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                        BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadWrite, bitmap.PixelFormat);

                        IntPtr ptr = bitmapData.Scan0;
                        int bytes = bitmapData.Stride * bitmap.Height;
                        byte[] rgbValues = new byte[bytes];

                        Marshal.Copy(ptr, rgbValues, 0, bytes);

                        int pixelByte = 0;

                        for (int y = 0; y < bitmap.Height; y++)
                        {
                            for (int x = 0; x < bitmap.Width; x++)
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
            if (base64 == null || base64.Length < 1) return null;

            HashSet<char> whiteSpace = new HashSet<char> { '\t', '\n', '\r', ' ' };
            int length = base64.Count(c => !whiteSpace.Contains(c));
            if (length % 4 != 0)
                base64 += new string('=', 4 - length % 4);
            byte[] imageBytes = Convert.FromBase64String(base64);
            
            MemoryStream ms = new MemoryStream(imageBytes);
            Image image = Image.FromStream(ms, true);

            return image;
        }

        //public static Bitmap GetBitmapFromBase64(string base64)
        //{
        //    using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(base64)))
        //    {
        //        Bitmap bitmap = new Bitmap(ms);
        //        ms.Close();
        //        ms.Dispose();
        //        return bitmap;
        //    }
        //}


        public static string GetBase64FromImage(Image image)
        {
            if (image == null) return "";
            using (MemoryStream ms= new MemoryStream())
            {
                image.Save(ms, image.RawFormat);
                byte[] imageBytes = ms.ToArray();
                string base64String = Convert.ToBase64String(imageBytes);

                ms.Dispose();

                return base64String;
            }
        }

        public static string GetBase64FromBitmap(Bitmap bitmap)
        {
            if (bitmap == null) return "";
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                byte[] imageBytes = ms.ToArray();
                string base64String = Convert.ToBase64String(imageBytes);

                ms.Dispose();

                return base64String;
            }
        }
    }
}
