using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public class HorizontalTabControl : TabControl
    {
        public HorizontalTabControl()
        {

            this.SetStyle(ControlStyles.UserPaint |
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
            Bitmap b = new Bitmap(Width, Height);
            Graphics g = Graphics.FromImage(b);

            g.Clear(BackgroundColor);
            g.FillRectangle(new SolidBrush(BackgroundColor), new Rectangle(0, 0, ItemSize.Height + 4, Height));

            g.SmoothingMode = SmoothingMode.HighQuality;

            for (int i = 0; i <= TabCount - 1; i++)
            {
                Rectangle buttonSurface = new Rectangle(new Point(GetTabRect(i).Location.X + 6, GetTabRect(i).Location.Y - 2), new Size(GetTabRect(i).Width - 5, GetTabRect(i).Height - 1));
                
                Rectangle rectSelected = new Rectangle
                {
                    Height = 3,
                    Width = buttonSurface.Width - 8,
                    X = buttonSurface.X + 4,
                    Y = buttonSurface.Y + buttonSurface.Height - 3,
                };
                Rectangle rectHover = new Rectangle
                {
                    Height = 3,
                    Width = buttonSurface.Width - 16,
                    X = buttonSurface.X + 8,
                    Y = buttonSurface.Y + buttonSurface.Height - 3,
                };

                

                using (SolidBrush backgroundBrush = new SolidBrush(this.Parent.BackColor))
                using (SolidBrush selectedBrush = new SolidBrush(Color.FromArgb(0, 123, 255)))
                {
                    g.FillRectangle(backgroundBrush, buttonSurface);
                    if (i == SelectedIndex)
                    {
                        g.FillRectangle(selectedBrush, rectSelected);
                    }
                }


                if (ImageList != null)
                {
                    try
                    {
                        g.DrawImage(ImageList.Images[TabPages[i].ImageIndex],
                            new Point(buttonSurface.Location.X + 8, buttonSurface.Location.Y + 6));
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
}
