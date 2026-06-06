using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public class HorizontalTabControl : TabControl
{
    public HorizontalTabControl()
    {

        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.DoubleBuffer |
                 ControlStyles.OptimizedDoubleBuffer, true);
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(136, 32);
        Alignment = TabAlignment.Top;
        SelectedIndex = 0;

    }


    public Color BackgroundColor { get; set; } = Color.FromArgb(45, 45, 45);

    protected override void OnPaint(PaintEventArgs e)
    {
        var b = new Bitmap(Width, Height);
        var g = Graphics.FromImage(b);

        g.Clear(BackgroundColor);
        g.FillRectangle(new SolidBrush(BackgroundColor), new Rectangle(0, 0, ItemSize.Height + 4, Height));

        g.SmoothingMode = SmoothingMode.HighQuality;

        for (var i = 0; i <= TabCount - 1; i++)
        {
            var buttonSurface = new Rectangle(new Point(GetTabRect(i).Location.X + 6, GetTabRect(i).Location.Y - 2), new Size(GetTabRect(i).Width - 5, GetTabRect(i).Height - 1));
                
            using (var backgroundBrush = new SolidBrush(Parent.BackColor))
            using (var selectedBrush = new SolidBrush(Colors.AccentColorDark))
            {
                switch (i == SelectedIndex)
                {
                    case true:
                        g.FillRectangle(selectedBrush, buttonSurface);
                        break;
                    case false:
                        g.FillRectangle(backgroundBrush, buttonSurface);
                        break;
                }
            }


            if (ImageList != null)
            {
                try
                {
                    g.DrawImage(ImageList.Images[TabPages[i].ImageIndex], new Point(buttonSurface.Location.X + 8, buttonSurface.Location.Y + 6));
                    g.DrawString("  " + TabPages[i].Text, Font, Brushes.White, buttonSurface, new StringFormat
                    {
                        LineAlignment = StringAlignment.Center,
                        Alignment = StringAlignment.Center
                    });
                }
                catch (Exception)
                {
                    g.DrawString(TabPages[i].Text, new Font(Font.FontFamily, Font.Size, FontStyle.Regular),
                        Brushes.White, buttonSurface, new StringFormat
                        {
                            LineAlignment = StringAlignment.Center,
                            Alignment = StringAlignment.Center
                        });
                }
            }
            else
            {
                g.DrawString(TabPages[i].Text, new Font(Font.FontFamily, Font.Size, FontStyle.Regular),
                    Brushes.White, buttonSurface, new StringFormat
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