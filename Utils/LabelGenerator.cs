using SuchByte.MacroDeck.ActionButton;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;

namespace SuchByte.MacroDeck.Utils
{
    public class LabelGenerator
    {
        public static Bitmap GetLabel(Bitmap img, String text, ButtonLabelPosition buttonLabelPosition, Font font, Color textColor, Color shadowColor, SizeF shadowOffset)
        {
            if (img == null) return img;

            Graphics g = Graphics.FromImage(img);

            StringFormat sf = new StringFormat
            {
                Alignment = StringAlignment.Center
            };

            if (buttonLabelPosition == ButtonLabelPosition.TOP)
                sf.LineAlignment = StringAlignment.Near;
            else if (buttonLabelPosition == ButtonLabelPosition.CENTER)
                sf.LineAlignment = StringAlignment.Center;
            else
                sf.LineAlignment = StringAlignment.Far;

            Pen p = new Pen(Color.Black, 2)
            {
                LineJoin = LineJoin.Round
            };

            SolidBrush b = new SolidBrush(textColor);

            Rectangle r = new Rectangle(2, 2, img.Width - 2, img.Height - 2);

            GraphicsPath gp = new GraphicsPath();

            gp.AddString(text, font.FontFamily, (int)font.Style, font.Size * 5, r, sf);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            g.DrawPath(p, gp);
            g.FillPath(b, gp);

            gp.Dispose();
            b.Dispose();
            b.Dispose();
            font.Dispose();
            sf.Dispose();
            g.Dispose();

            return img;
        }
    }
}
