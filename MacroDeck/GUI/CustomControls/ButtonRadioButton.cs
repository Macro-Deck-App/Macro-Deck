using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class ButtonRadioButton : RadioButton
{
    private int _borderRadius = 8;

    private Image _icon;
    private bool _hover;
    private ContentAlignment _iconAlignment = ContentAlignment.MiddleLeft;

    public ContentAlignment IconAlignment
    {
        get => _iconAlignment;
        set
        {
            _iconAlignment = value;
            Invalidate();
        }
    }

    public Image Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            Invalidate();
        }
    }

    public int BorderRadius
    {
        get => _borderRadius;
        set
        {
            _borderRadius = value;
            Invalidate();
        }
    }

    public ButtonRadioButton()
    {
        InitializeComponent();

        Appearance = Appearance.Normal;
        Cursor = Cursors.Hand;
        MouseEnter += MouseEnterEvent;
        MouseLeave += MouseLeaveEvent;
    }


    private void MouseEnterEvent(object sender, EventArgs e)
    {
        _hover = true;
        Invalidate();
    }

    private void MouseLeaveEvent(object sender, EventArgs e)
    {
        _hover = false;
        Invalidate();
    }

    private GraphicsPath GetFigurePath(Rectangle rect, float radius)
    {
        var path = new GraphicsPath();
        var curveSize = radius * 2F;
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

        pe.Graphics.InterpolationMode = InterpolationMode.High;
        pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rectSurface = ClientRectangle;
        var iconSurface = new Rectangle(rectSurface.X + Margin.Left, rectSurface.Y + Margin.Top, rectSurface.Height - Margin.Top - Margin.Bottom, rectSurface.Height - Margin.Top - Margin.Bottom);
        switch (_iconAlignment)
        {
            case ContentAlignment.TopLeft:
            case ContentAlignment.MiddleLeft:
            case ContentAlignment.BottomLeft:
                iconSurface = new Rectangle(rectSurface.X + Margin.Left, rectSurface.Y + Margin.Top, rectSurface.Height - Margin.Top - Margin.Bottom, rectSurface.Height - Margin.Top - Margin.Bottom);
                break;
            case ContentAlignment.TopCenter:
            case ContentAlignment.MiddleCenter:
            case ContentAlignment.BottomCenter:
                iconSurface = new Rectangle(rectSurface.X + (Width / 2) - ((rectSurface.Height - Margin.Top - Margin.Bottom) / 2), rectSurface.Y + Margin.Top, rectSurface.Height - Margin.Top - Margin.Bottom, rectSurface.Height - Margin.Top - Margin.Bottom);
                break;
        }
            

        var smoothSize = 2;
        using var pathSurface = GetFigurePath(rectSurface, _borderRadius);
        using var penSurface = new Pen(Parent.BackColor, smoothSize);
        if (Checked)
        {
            pe.Graphics.FillRectangle(new SolidBrush(Colors.WindowsAccentColorDark), rectSurface);
        }
        else
        {
            if (_hover)
            {
                pe.Graphics.FillRectangle(new SolidBrush(Colors.WindowsAccentColorLight), rectSurface);
            }
            else
            {
                pe.Graphics.FillRectangle(new SolidBrush(Colors.WindowsAccentColor), rectSurface);
            }
        }

        if (_icon != null)
        {
            pe.Graphics.DrawImage(_icon, iconSurface);
        }
        Region = new Region(pathSurface);
        pe.Graphics.DrawPath(penSurface, pathSurface);

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
        TextRenderer.DrawText(pe.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
    }
}