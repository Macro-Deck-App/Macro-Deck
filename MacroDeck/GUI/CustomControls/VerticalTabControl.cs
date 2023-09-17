using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public class VerticalTabControl : TabControl
{
    private List<int> _notificationIndexes = new();

    public Color SelectedTabColor { get; set; } = Colors.AccentColorDark;


    public VerticalTabControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
            ControlStyles.DoubleBuffer, true);
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(200, 44);
        Alignment = TabAlignment.Left;
        SelectedIndex = 0;
    }

    public void SetNotification(int tabIndex, bool value)
    {
        if (value)
        {
            if (!_notificationIndexes.Contains(tabIndex))
                _notificationIndexes.Add(tabIndex);
        } else
        {
            if (!_notificationIndexes.Contains(tabIndex))
                _notificationIndexes.Remove(tabIndex);
        }
        Invalidate();
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
        var b = new Bitmap(Width, Height);
        var g = Graphics.FromImage(b);

        g.Clear(Color.FromArgb(45, 45, 45));
        g.FillRectangle(new SolidBrush(Color.FromArgb(45, 45, 45)), new Rectangle(0, 0, ItemSize.Height + 4, Height));
        g.DrawLine(new Pen(Color.FromArgb(45, 45, 45)), new Point(ItemSize.Height + 3, 0), new Point(ItemSize.Height + 3, 999));
        g.DrawLine(new Pen(Color.FromArgb(45, 45, 45)), new Point(0, Size.Height - 1), new Point(Width + 3, Size.Height - 1));


        for (var i = 0; i <= TabCount - 1; i++)
        {
            var buttonRectangle = new Rectangle(new Point(GetTabRect(i).Location.X - 2, GetTabRect(i).Location.Y - 2), new Size(GetTabRect(i).Width + 3, GetTabRect(i).Height - 1));

            switch (i == SelectedIndex)
            {
                case true:

                    using (var pathBorderSmooth = GetFigurePath(buttonRectangle, 8))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.FillPath(new SolidBrush(SelectedTabColor), pathBorderSmooth);
                    }

                    break;
                case false:
                    g.FillRectangle(new SolidBrush(Parent.BackColor), buttonRectangle);
                    break;
            }

            if (_notificationIndexes.Contains(i))
            {
                g.FillEllipse(Brushes.Red, buttonRectangle.X + buttonRectangle.Width - 22, buttonRectangle.Y + 6, 10, 10);
            }

                

            if (ImageList != null)
            {
                try
                {
                    g.InterpolationMode = InterpolationMode.High;
                    g.DrawImage(ImageList.Images[i], new Rectangle(buttonRectangle.Location.X + 8, buttonRectangle.Location.Y + 22 - (24 / 2), 24, 24));
                    g.DrawString("  " + TabPages[i].Text, Font, Brushes.White, buttonRectangle, new StringFormat
                    {
                        LineAlignment = StringAlignment.Center,
                        Alignment = StringAlignment.Center
                    });
                }
                catch (Exception)
                {
                    g.DrawString(TabPages[i].Text, Font, Brushes.White, buttonRectangle, new StringFormat
                    {
                        LineAlignment = StringAlignment.Center,
                        Alignment = StringAlignment.Center
                    });
                }
            }
            else
            {
                g.DrawString(TabPages[i].Text, Font, Brushes.White, buttonRectangle, new StringFormat
                {
                    LineAlignment = StringAlignment.Center,
                    Alignment = StringAlignment.Center
                });
            }
        }

        e.Graphics.DrawImage(b, new Point(0, 0));
        g.Dispose();
        b.Dispose();
    }
}