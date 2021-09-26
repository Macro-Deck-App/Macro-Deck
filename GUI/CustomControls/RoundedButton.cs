using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public class RoundedButton : PictureBox
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public int Radius { get; set; } = 40;
        public Image ForegroundImage { get { return this._foregroundImage; } set { this._foregroundImage = value; this.Invalidate(); } }
        private Image _foregroundImage = null;

        public bool ShowGIFIndicator{ get { return this._gifIndicator; } set { this._gifIndicator = value; this.Invalidate(); } }
        private bool _gifIndicator = false;


        public RoundedButton()
        {
            this.SizeMode = PictureBoxSizeMode.StretchImage;
            this.MouseEnter += this.OnMouseEnter;
            this.MouseLeave += this.OnMouseLeave;
        }

        private void OnMouseEnter(object sender, EventArgs e)
        {
            this.Invalidate();
            this.Image = this.BackgroundImage;
        }

        private void OnMouseLeave(object sender, EventArgs e)
        {
            this.Invalidate();
            this.Image = null;
        }


        private GraphicsPath GetFigurePath(Rectangle rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float curveSize = radius;
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

            int radius = (int)(((float)this.Radius / 100.0f) * (float)this.Height);

            Color borderColor = Color.FromArgb(50, 50, 50);
            int borderSize = 6;
            int smoothSize = 4;
            Rectangle rectSurface = this.ClientRectangle;

            if (radius > 2) //Rounded button
            {
                using (GraphicsPath pathSurface = GetFigurePath(rectSurface, radius))
                using (Pen penSurface = new Pen(this.Parent.BackColor, smoothSize))
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    this.Region = new Region(pathSurface);
                    pe.Graphics.DrawPath(penSurface, pathSurface);
                    if (borderSize >= 1)
                        pe.Graphics.DrawPath(new Pen(borderColor, borderSize), pathSurface);
                }
            }
            else //Normal button
            {
                pe.Graphics.SmoothingMode = SmoothingMode.None;
                this.Region = new Region(rectSurface);
                if (borderSize >= 1)
                {
                    using (Pen penBorder = new Pen(borderColor, borderSize))
                    {
                        penBorder.Alignment = PenAlignment.Inset;
                        pe.Graphics.DrawRectangle(penBorder, 0, 0, this.Width - 1, this.Height - 1);
                    }
                }
            }

            /*if (radius > 1)
            {
                using (var graphicsPath = GetRoundPath(rect, radius))
                {
                    DoubleBuffered = true;
                    e.Graphics.DrawPath(new Pen(borderColor, 3), graphicsPath);
                    this.Region = new Region(graphicsPath);
                    graphicsPath.CloseFigure();
                }
            } else
            {
                e.Graphics.DrawRectangle(new Pen(Color.Black, 3), rect);
            }*/



            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            if (this._foregroundImage != null)
            {
                pe.Graphics.DrawImage(this._foregroundImage, rect);
            }

            if (this._gifIndicator)
            {
                Rectangle gifRect = new Rectangle(this.Width - radius / 2  - 27, 2, 25, 14);
                pe.Graphics.DrawImage(Properties.Resources.gif, gifRect);
            }
        }

    }
}
