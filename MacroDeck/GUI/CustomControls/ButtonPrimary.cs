using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class ButtonPrimary : Button
{
    //public static Color border = Colors.AccentColor;

    private bool _hover;

    private int borderRadius = 8;
    private int progress;
    private Color backColor;
    private Color progressColor = Colors.DefaultAccentColorDark;
    private Color hoverColor;
    private string text = "";
    private Image _icon;
    private bool currentlyAnimating;
    private Bitmap spinnerBitmap = Resources.Spinner;
    public bool UseWindowsAccentColor { get; set; } = true;


    public Image Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            Invalidate();
        }
    }

    public bool Spinner = false;


    //This method begins the animation.
    public void AnimateImage()
    {
        if (!currentlyAnimating)
        {

            //Begin the animation only once.
            ImageAnimator.Animate(spinnerBitmap, OnFrameChanged);
            currentlyAnimating = true;
        }
    }

    private void OnFrameChanged(object o, EventArgs e)
    {
        Invalidate();
    }

    public new Color BackColor
    {
        get {
            if (DesignMode)
            {
                return backColor;
            }
            switch (UseWindowsAccentColor)
            {
                case true:
                    return Colors.WindowsAccentColor;
                case false:
                    return backColor;
            }
        }
        set
        {
            backColor = value;
            Invalidate();
        }
    }

    public Color HoverColor
    {
        get
        {
            if (DesignMode)
            {
                return hoverColor;
            }
            switch (UseWindowsAccentColor)
            {
                case true:
                    return Colors.WindowsAccentColorLight;
                case false:
                    return hoverColor;
            }
        }
        set
        {
            hoverColor = value;
            Invalidate();
        }
    }

    public Color ProgressColor
    {
        get
        {
            if (DesignMode)
            {
                return progressColor;
            }
            switch (UseWindowsAccentColor)
            {
                case true:
                    return Colors.WindowsAccentColorDark;
                case false:
                    return progressColor;
            }
        }
        set
        {
            progressColor = value;
            Invalidate();
        }
    }


    public int BorderRadius
    {
        get => borderRadius;
        set
        {
            borderRadius = value;
            Invalidate();
        }
    }

    public int Progress
    {
        get => progress;
        set
        {
            progress = value;
            Invalidate();
        }
    }
    public override string Text
    {
        get => text;
        set
        {
            text = value;
            Invalidate();
        }
    }
        


    public ButtonPrimary()
    {
        FlatStyle = FlatStyle.Flat;
        ForeColor = Color.White;
        BackColor = Color.FromArgb(0, 123, 255);
        FlatAppearance.BorderSize = 0;
        Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
        Cursor = Cursors.Hand;
        Size = new Size(150, 40);
        Resize += Button_Resize;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.Selectable, false);
        MouseEnter += ButtonPrimary_MouseEnter;
        MouseLeave += ButtonPrimary_MouseLeave;
        MouseUp += ButtonPrimary_MouseUp;
    }

    private void ButtonPrimary_MouseUp(object sender, MouseEventArgs e)
    {
        _hover = false;
        Invalidate();
    }

    private void ButtonPrimary_MouseLeave(object sender, EventArgs e)
    {
        _hover = false;
        Invalidate();
    }

    private void ButtonPrimary_MouseEnter(object sender, EventArgs e)
    {
        _hover = true;
        Invalidate();
    }

    private void Button_Resize(object sender, EventArgs e)
    {
        if (borderRadius > Height)
            borderRadius = Height;
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

        var rectSurface = ClientRectangle;
        var rectProgressSurface = new Rectangle
        {
            Height = Height,
            Width = (int)(Width / 100.0f * progress)
        };
        var smoothSize = 2;
        if (borderRadius > 2)
        {
            using var pathSurface = GetFigurePath(rectSurface, borderRadius);
            using var progressBrush = new SolidBrush(ProgressColor);
            using var penSurface = new Pen(Parent.BackColor, smoothSize);
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (_hover)
            {
                pe.Graphics.FillRectangle(new SolidBrush(HoverColor), rectSurface);
            } else
            {
                pe.Graphics.FillRectangle(new SolidBrush(BackColor), rectSurface);
            }
            pe.Graphics.FillRectangle(progressBrush, rectProgressSurface);
            if (_icon != null)
            {
                pe.Graphics.DrawImage(_icon, rectSurface);
            }
            Region = new Region(pathSurface);
            pe.Graphics.DrawPath(penSurface, pathSurface);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
            if (progress > 0)
            {
                TextRenderer.DrawText(pe.Graphics, string.Format("{0}%", progress), Font, ClientRectangle, ForeColor, flags);
            } else
            {
                TextRenderer.DrawText(pe.Graphics, text, Font, ClientRectangle, ForeColor, flags);
            }
            if (Spinner)
            {
                AnimateImage();
                ImageAnimator.UpdateFrames();
                float spinnerSize = Height - 8;
                pe.Graphics.DrawImage(spinnerBitmap, 5, 4, spinnerSize, spinnerSize);
            }
        }

           
    }
}