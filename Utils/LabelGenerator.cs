using System.Drawing;
using System.Drawing.Drawing2D;
using SuchByte.MacroDeck.ActionButton;

namespace SuchByte.MacroDeck.Utils;

public class LabelGenerator
{
    public static Image GetLabel(Image img, string text, ButtonLabelPosition buttonLabelPosition, Font font, Color textColor, Color shadowColor, SizeF shadowOffset)
    {
        if (img == null) return img;

        var g = Graphics.FromImage(img);

        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center
        };

        if (buttonLabelPosition == ButtonLabelPosition.TOP)
            sf.LineAlignment = StringAlignment.Near;
        else if (buttonLabelPosition == ButtonLabelPosition.CENTER)
            sf.LineAlignment = StringAlignment.Center;
        else
            sf.LineAlignment = StringAlignment.Far;

        var p = new Pen(Color.Black, 2)
        {
            LineJoin = LineJoin.Round
        };

        var b = new SolidBrush(textColor);

        var r = new Rectangle(2, 2, img.Width - 2, img.Height - 2);

        var gp = new GraphicsPath();

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