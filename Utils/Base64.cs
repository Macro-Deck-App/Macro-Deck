using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace SuchByte.MacroDeck.Utils
{
    public class Base64
    {
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
