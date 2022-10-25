using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using SuchByte.MacroDeck.Icons;

namespace SuchByte.MacroDeck.Utils
{
    public static class IconPackPreview
    {

        public static Image GeneratePreviewImage(IconPack iconPack)
        {
            var totalSize = 80;
            var bitmap = new Bitmap(totalSize, totalSize);

            var padding = 2;
            var iconSize = (80 / 2);

            int row = 0, column = 0;

            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(32,32,32));
            }

            var canvas = Graphics.FromImage(bitmap);
            canvas.InterpolationMode = InterpolationMode.Bicubic;
            foreach (var icon in iconPack.Icons.Take(4))
            {
                try
                {
                    var iconRectangle = new Rectangle
                    {
                        Height = iconSize - padding,
                        Width = iconSize - padding,
                        X = column * (iconSize + (column * padding)),
                        Y = row * (iconSize + (row * padding)),
                    };
                    canvas.DrawImage(new Bitmap(icon.IconImage), iconRectangle);
                    column++;
                    if (column >= 2)
                    {
                        column = 0;
                        row++;
                    }
                } catch
                {
                }
            }
            canvas.Save();

            return bitmap;
        }

    }
}
