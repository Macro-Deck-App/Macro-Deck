using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace SuchByte.MacroDeck.Utils
{
    public class CombineBitmaps
    {
        public static Bitmap CombineAll(List<Bitmap> bitmaps)
        {
            Bitmap combined = new Bitmap(350, 350);

            using (Graphics g = Graphics.FromImage(combined))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach(Bitmap bitmap in bitmaps)
                {
                    g.DrawImage(bitmap, Point.Empty);
                }
            }

            return combined;
        }


        public static Bitmap Combine(Bitmap backgroundBitmap, Bitmap iconBitmap)
        {
            int px = 350;
            if (backgroundBitmap == null) backgroundBitmap = new Bitmap(px, px);
            if (iconBitmap == null) iconBitmap = new Bitmap(px, px);
            Image background = new Bitmap(backgroundBitmap.Width, backgroundBitmap.Height, PixelFormat.Format32bppArgb);
            Image icon = new Bitmap(background.Width, background.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(background))
            {
                g.Clear(Color.Transparent);
            }
            using (var g = Graphics.FromImage(icon))
            {
                g.Clear(Color.Transparent);
            }


            var bitmap = new Bitmap(px, px);
            var canvas = Graphics.FromImage(bitmap);
            canvas.InterpolationMode = InterpolationMode.Bicubic;
            canvas.DrawImage(background,
                             new Rectangle(0,
                                           0,
                                           background.Width,
                                           background.Height),
                             new Rectangle(0,
                                           0,
                                           background.Width,
                                           background.Height),
                             GraphicsUnit.Pixel);
            canvas.DrawImage(icon,
                             (bitmap.Width / 2) - (icon.Width / 2),
                             (bitmap.Height / 2) - (icon.Height / 2));


            canvas.Save();

            return bitmap;
        }
    }
}
