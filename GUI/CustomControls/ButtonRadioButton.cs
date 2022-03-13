using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class ButtonRadioButton : System.Windows.Forms.RadioButton
    {
        private int borderRadius = 8;

        private Image _icon;
        private bool _hover = false;


        public Image Icon
        {
            get { return this._icon; }
            set
            {
                this._icon = value;
                this.Invalidate();
            }
        }

        public int BorderRadius
        {
            get { return borderRadius; }
            set
            {
                borderRadius = value;
                this.Invalidate();
            }
        }

        public ButtonRadioButton()
        {
            InitializeComponent();
            this.Appearance = Appearance.Normal;
            this.Cursor = Cursors.Hand;
            this.MouseEnter += MouseEnterEvent;
            this.MouseLeave += MouseLeaveEvent;
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

            pe.Graphics.InterpolationMode = InterpolationMode.High;
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rectSurface = this.ClientRectangle;
            Rectangle iconSurface = new Rectangle(rectSurface.X + this.Margin.Left, rectSurface.Y + this.Margin.Top, rectSurface.Height - this.Margin.Top - this.Margin.Bottom, rectSurface.Height - this.Margin.Top - this.Margin.Bottom);

            int smoothSize = 2;
            if (borderRadius > 2)
            {
                using (GraphicsPath pathSurface = GetFigurePath(rectSurface, borderRadius))
                using (Pen penSurface = new Pen(this.Parent.BackColor, smoothSize))
                {
                    if (this.Checked)
                    {
                        pe.Graphics.FillRectangle(new SolidBrush(Colors.WindowsAccentColorDark), rectSurface);
                    } else
                    {
                        if (this._hover)
                        {
                            pe.Graphics.FillRectangle(new SolidBrush(Colors.WindowsAccentColorLight), rectSurface);
                        }
                        else
                        {
                            pe.Graphics.FillRectangle(new SolidBrush(Colors.WindowsAccentColor), rectSurface);
                        }
                    }
                    
                    if (this._icon != null)
                    {
                        pe.Graphics.DrawImage(this._icon, iconSurface);
                    }
                    this.Region = new Region(pathSurface);
                    pe.Graphics.DrawPath(penSurface, pathSurface);

                    TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
                    TextRenderer.DrawText(pe.Graphics, this.Text, Font, ClientRectangle, ForeColor, flags);
                }
            }


        }
    }
}
