using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public class RoundedButton : PictureBox
{
    public int Row { get; set; }
    public int Column { get; set; }
    public int Radius { get; set; } = 40;
    public Image ForegroundImage { get => _foregroundImage;
        set { _foregroundImage = value; Invalidate(); } }
    private Image _foregroundImage;

    public bool ShowGIFIndicator { get => _gifIndicator;
        set { _gifIndicator = value; Invalidate(); } }
    private bool _gifIndicator;

    public bool ShowKeyboardHotkeyIndicator { get => _keyboardHotkeyIndicator;
        set { _keyboardHotkeyIndicator = value; Invalidate(); } }
    private bool _keyboardHotkeyIndicator;

    public string KeyboardHotkeyIndicatorText = "";



    public RoundedButton()
    {
        SizeMode = PictureBoxSizeMode.StretchImage;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        Padding = Padding.Empty;
    }

    private void OnMouseEnter(object sender, EventArgs e)
    {
        Invalidate();
        try
        {
            Image = BackgroundImage;
        } catch { }
    }

    private void OnMouseLeave(object sender, EventArgs e)
    {
        Invalidate();
        Image = null;
    }


    private GraphicsPath GetFigurePath(Rectangle rect, float radius)
    {
        var path = new GraphicsPath();
        var curveSize = radius;
        path.StartFigure();
        path.AddArc(rect.X, rect.Y, curveSize, curveSize, 180, 90);
        path.AddArc(rect.Right - curveSize, rect.Y, curveSize, curveSize, 270, 90);
        path.AddArc(rect.Right - curveSize, rect.Bottom - curveSize, curveSize, curveSize, 0, 90);
        path.AddArc(rect.X, rect.Bottom - curveSize, curveSize, curveSize, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        base.OnPaint(pe);

        pe.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        var radius = (int)((Radius / 100.0f) * Height);

        var borderColor = Color.FromArgb(35, 35, 35);
        var borderSize = 6;
        var smoothSize = 4;
        var rectSurface = ClientRectangle;

        if (radius > 2) //Rounded button
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pathSurface = GetFigurePath(rectSurface, radius);
            using var penSurface = new Pen(Parent.BackColor, smoothSize);
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Region = new Region(pathSurface);
            pe.Graphics.DrawPath(penSurface, pathSurface);
            if (borderSize >= 1)
                pe.Graphics.DrawPath(new Pen(borderColor, borderSize), pathSurface);
        }
        else //Normal button
        {
            pe.Graphics.SmoothingMode = SmoothingMode.None;
            Region = new Region(rectSurface);
            if (borderSize >= 1)
            {
                using var penBorder = new Pen(borderColor, borderSize);
                pe.Graphics.DrawRectangle(penBorder, 0, 0, Width, Height);
            }
        }

        var rect = new Rectangle(0, 0, Width, Height);

        if (_foregroundImage != null)
        {
            pe.Graphics.DrawImage(_foregroundImage, rect);
        }

        if (_gifIndicator)
        {
            var gifRect = new Rectangle(Width - radius / 2 - 27, 2, 25, 14);
            pe.Graphics.DrawImage(Resources.gif, gifRect);
        }

        if (_keyboardHotkeyIndicator)
        {
            var hotkeyIndicatorBackground = new Rectangle(0, Height / 2 - 12, Width, 24);
            var hotkeyIndicatorBackgroundBrush = new SolidBrush(Color.FromArgb(128, 0, 89, 184));
            pe.Graphics.FillRectangle(hotkeyIndicatorBackgroundBrush, hotkeyIndicatorBackground);
            var keyboardRect = new Rectangle(5, Height / 2 - 10, 20, 20);
            pe.Graphics.DrawImage(Resources.Keyboard, keyboardRect);
            using var gp = new GraphicsPath();
            using var sf = new StringFormat
            {

                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
            };
            using var font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            var r = new Rectangle(30, Height / 2 - 12, Width - 35, 24);
            var p = new Pen(Color.Black, 1)
            {
                LineJoin = LineJoin.Round
            };
            gp.AddString(KeyboardHotkeyIndicatorText, font.FontFamily, (int)font.Style, font.Size, r, sf);
            pe.Graphics.DrawPath(p, gp);
            pe.Graphics.FillPath(Brushes.White, gp);
        }
    }

}