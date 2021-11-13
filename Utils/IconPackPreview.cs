using SuchByte.MacroDeck.Icons;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using Icon = SuchByte.MacroDeck.Icons.Icon;

namespace SuchByte.MacroDeck.Utils
{
    public static class IconPackPreview
    {

        public static System.Drawing.Image GeneratePreviewImage(IconPack iconPack)
        {
            int totalSize = 80;
            Bitmap bitmap = new Bitmap(totalSize, totalSize);

            int padding = 2;
            int iconSize = (80 / 2);

            int row = 0, column = 0;

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(32,32,32));
            }

            var canvas = Graphics.FromImage(bitmap);
            canvas.InterpolationMode = InterpolationMode.Bicubic;
            foreach (Icon icon in iconPack.Icons.Take(4))
            {
                Rectangle iconRectangle = new Rectangle
                {
                    Height = iconSize - padding,
                    Width = iconSize - padding,
                    X = column * (iconSize + (column * padding)),
                    Y = row * (iconSize + (row * padding)),
                };
                canvas.DrawImage(icon.IconImage, iconRectangle);
                column++;
                if (column >= 2)
                {
                    column = 0;
                    row++;
                }
            }
            canvas.Save();

            return bitmap;
        }

    }
}
