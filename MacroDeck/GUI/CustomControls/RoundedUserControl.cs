using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class RoundedUserControl : UserControl
{
    private int borderRadius = 8;

    public RoundedUserControl()
    {
        InitializeComponent();
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }



    private GraphicsPath GetFigurePath(Rectangle rect, int radius)
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graph = e.Graphics;

        if (borderRadius > 1)
        {
            var rectBorderSmooth = ClientRectangle;
            var smoothSize = 2;
            using var pathBorderSmooth = GetFigurePath(rectBorderSmooth, borderRadius);
            using var penBorderSmooth = new Pen(Parent.BackColor, smoothSize);
            Region = new Region(pathBorderSmooth);
            graph.SmoothingMode = SmoothingMode.AntiAlias;
            graph.DrawPath(penBorderSmooth, pathBorderSmooth);
        }
        else
        {
            Region = new Region(ClientRectangle);
        }
    }
}