using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    [Obsolete("Use RoundedComboBox instead.")]
    public class ComboBox : System.Windows.Forms.ComboBox
    {

        private int _borderRadius = 8;
        private bool _regionSet = false;
        private bool _hover = false;

        public int BorderRadius
        {
            get { return _borderRadius; }
            set
            {
                _borderRadius = value;
                this.Invalidate();
            }
        }

        

        public ComboBox()
        {
            SetStyle(
                     ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint,
                     true);

            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(65, 65, 65);
            this.ForeColor = Color.White;
            this.Cursor = Cursors.Hand;
            this.MouseEnter += MouseEnterEvent;
            this.MouseLeave += MouseLeaveEvent;
            this.SelectedIndexChanged += SelectedIndexEvent;
        }

        private void SelectedIndexEvent(object sender, EventArgs e)
        {
            this._hover = false;
            this.Invalidate();
        }

        private void MouseEnterEvent(object sender, EventArgs e)
        {
            this._hover = true;
            this.Invalidate();
        }

        private void MouseLeaveEvent(object sender, EventArgs e)
        {
            this._hover = false;
            this.Invalidate();
        }

        private GraphicsPath GetFigurePath(Rectangle rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float curveSize = radius * 2F;
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
            
            Rectangle rectSurface = this.ClientRectangle;
            Rectangle rectText = new Rectangle
            {
                Height = this.Height,
                Width = this.Width,
                X = 10,
            };
            int smoothSize = 2;
            if (this._borderRadius > 2)
            {
                using (GraphicsPath pathSurface = GetFigurePath(rectSurface, this._borderRadius))
                using (Pen penSurface = new Pen(this.Parent.BackColor, smoothSize))
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (!this._regionSet)
                    {
                        this.Region = new Region(pathSurface);
                        this._regionSet = true; // Preventing future region changes because the event raises on every region change
                    }
                    pe.Graphics.DrawPath(penSurface, pathSurface);
                }
            }


            StringFormat stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center
            };

            using (SolidBrush brush = new SolidBrush(this.Enabled ? (this._hover ? Color.White : Color.Silver) : Color.FromArgb(95,95,95)))
            {
                pe.Graphics.DrawString(this.Text, this.Font, brush, rectText, stringFormat);
                if (this.Enabled)
                {
                    pe.Graphics.FillPolygon(brush, new Point[] { new Point(this.Width - 10, this.Height / 2 - 2), new Point(this.Width - 20, this.Height / 2 - 2), new Point(this.Width - 15, this.Height / 2 + 3) });
                }
            }

        }

    }
}
