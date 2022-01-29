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

        public bool ShowGIFIndicator { get { return this._gifIndicator; } set { this._gifIndicator = value; this.Invalidate(); } }
        private bool _gifIndicator = false;

        public bool ShowKeyboardHotkeyIndicator { get { return this._keyboardHotkeyIndicator; } set { this._keyboardHotkeyIndicator = value; this.Invalidate(); } }
        private bool _keyboardHotkeyIndicator = false;

        public string KeyboardHotkeyIndicatorText = "";



        public RoundedButton()
        {
            this.SizeMode = PictureBoxSizeMode.StretchImage;
            this.MouseEnter += this.OnMouseEnter;
            this.MouseLeave += this.OnMouseLeave;
            this.Padding = Padding.Empty;
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

            pe.Graphics.InterpolationMode = InterpolationMode.High;

            int radius = (int)(((float)this.Radius / 100.0f) * (float)this.Height);

            Color borderColor = Color.FromArgb(35, 35, 35);
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
                        pe.Graphics.DrawRectangle(penBorder, 0, 0, this.Width, this.Height);
                    }
                }
            }

            Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);

            if (this._foregroundImage != null)
            {
                pe.Graphics.DrawImage(this._foregroundImage, rect);
            }

            if (this._gifIndicator)
            {
                Rectangle gifRect = new Rectangle(this.Width - radius / 2 - 27, 2, 25, 14);
                pe.Graphics.DrawImage(Properties.Resources.gif, gifRect);
            }

            if (this._keyboardHotkeyIndicator)
            {
                Rectangle hotkeyIndicatorBackground = new Rectangle(0, this.Height / 2 - 12, this.Width, 24);
                SolidBrush hotkeyIndicatorBackgroundBrush = new SolidBrush(Color.FromArgb(128, 0, 89, 184));
                pe.Graphics.FillRectangle(hotkeyIndicatorBackgroundBrush, hotkeyIndicatorBackground);
                Rectangle keyboardRect = new Rectangle(5, this.Height / 2 - 10, 20, 20);
                pe.Graphics.DrawImage(Properties.Resources.Keyboard, keyboardRect);
                using (var gp = new GraphicsPath())
                using (var sf = new StringFormat
                {

                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                })
                using (var font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point))
                {
                    Rectangle r = new Rectangle(30, this.Height / 2 - 12, this.Width - 35, 24);
                    Pen p = new Pen(Color.Black, 1)
                    {
                        LineJoin = LineJoin.Round
                    };
                    gp.AddString(this.KeyboardHotkeyIndicatorText, font.FontFamily, (int)font.Style, font.Size, r, sf);
                    pe.Graphics.DrawPath(p, gp);
                    pe.Graphics.FillPath(Brushes.White, gp);
                }
            }
        }

    }
}