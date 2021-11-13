using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public class VerticalTabControl : TabControl
    {
        private List<int> _notificationIndexes = new List<int>();

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
                if (!this._notificationIndexes.Contains(tabIndex))
                    this._notificationIndexes.Add(tabIndex);
            } else
            {
                if (!this._notificationIndexes.Contains(tabIndex))
                    this._notificationIndexes.Remove(tabIndex);
            }
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Bitmap b = new Bitmap(Width, Height);
            Graphics g = Graphics.FromImage(b);

            g.Clear(Color.FromArgb(45, 45, 45));
            g.FillRectangle(new SolidBrush(Color.FromArgb(45, 45, 45)),
                new Rectangle(0, 0, ItemSize.Height + 4, Height));
            g.DrawLine(new Pen(Color.FromArgb(45, 45, 45)), new Point(ItemSize.Height + 3, 0),
                new Point(ItemSize.Height + 3, 999));
            g.DrawLine(new Pen(Color.FromArgb(45, 45, 45)), new Point(0, Size.Height - 1),
                new Point(Width + 3, Size.Height - 1));


            for (int i = 0; i <= TabCount - 1; i++)
            {
                Rectangle x2 = new Rectangle(new Point(GetTabRect(i).Location.X - 2, GetTabRect(i).Location.Y - 2), new Size(GetTabRect(i).Width + 3, GetTabRect(i).Height - 1));

                if (i == SelectedIndex)
                {
                    g.FillRectangle(new SolidBrush(this.Parent.BackColor), x2);
                    g.DrawRectangle(new Pen(Color.FromArgb(0, 123, 255)), x2); // Umrandung

                    g.SmoothingMode = SmoothingMode.HighQuality;
                    Point[] p =
                    {
                        new Point(ItemSize.Height - 3, GetTabRect(i).Location.Y + 20),
                        new Point(ItemSize.Height + 4, GetTabRect(i).Location.Y + 14),
                        new Point(ItemSize.Height + 4, GetTabRect(i).Location.Y + 27)
                    };
                    g.FillPolygon(SystemBrushes.Control, p);
                    g.DrawPolygon(new Pen(Color.FromArgb(170, 187, 204)), p);

                }
                else
                {
                    g.FillRectangle(new SolidBrush(this.Parent.BackColor), x2);
                }

                if (this._notificationIndexes.Contains(i))
                {
                    g.FillEllipse(Brushes.Red, x2.X + x2.Width - 22, x2.Y + 6, 10, 10);
                }

                

                if (ImageList != null)
                {
                    try
                    {
                        g.DrawImage(ImageList.Images[i], new Point(x2.Location.X + 8, x2.Location.Y + 22 - (ImageList.ImageSize.Height / 2)));

                        g.DrawString("  " + TabPages[i].Text, Font, Brushes.White, x2, new StringFormat
                        {
                            LineAlignment = StringAlignment.Center,
                            Alignment = StringAlignment.Center
                        });
                    }
                    catch (Exception)
                    {
                        g.DrawString(TabPages[i].Text, Font, Brushes.White, x2, new StringFormat
                        {
                            LineAlignment = StringAlignment.Center,
                            Alignment = StringAlignment.Center
                        });
                    }
                }
                else
                {
                    g.DrawString(TabPages[i].Text, Font, Brushes.White, x2, new StringFormat
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
