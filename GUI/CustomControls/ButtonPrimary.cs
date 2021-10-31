using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class ButtonPrimary : Button
    {
        //public static Color border = Colors.AccentColor;

        private bool _hover = false;

        private int borderRadius = 8;
        private int progress = 0;
        private Color progressColor = Color.FromArgb(0, 46, 94);
        private Color hoverColor = Color.FromArgb(0, 89, 184);
        private string text = "";
        private Image _icon;

        public Image Icon
        {
            get { return this._icon; }
            set
            {
                this._icon = value;
                this.Invalidate();
            }
        }

        public Color HoverColor
        {
            get { return hoverColor; }
            set
            {
                hoverColor = value;
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

        public int Progress
        {
            get { return progress; }
            set
            {
                progress = value;
                this.Invalidate();
            }
        }

        public Color ProgressColor
        {
            get { return progressColor; }
            set
            {
                progressColor = value;
                this.Invalidate();
            }
        }

        public override string Text
        {
            get { return text; }
            set
            {
                text = value;
                this.Invalidate();
            }
        }
        


        public ButtonPrimary()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.ForeColor = Color.White;
            this.BackColor = Color.FromArgb(0, 123, 255);
            this.FlatAppearance.BorderSize = 0;
            this.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            this.Cursor = Cursors.Hand;
            this.Size = new Size(150, 40);
            this.Resize += new EventHandler(Button_Resize);
            this.DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            MouseEnter += ButtonPrimary_MouseEnter;
            MouseLeave += ButtonPrimary_MouseLeave;
            MouseUp += ButtonPrimary_MouseUp;
        }

        private void ButtonPrimary_MouseUp(object sender, MouseEventArgs e)
        {
            this._hover = false;
            this.Invalidate();
        }

        private void ButtonPrimary_MouseLeave(object sender, EventArgs e)
        {
            this._hover = false;
            this.Invalidate();
        }

        private void ButtonPrimary_MouseEnter(object sender, EventArgs e)
        {
            this._hover = true;
            this.Invalidate();
        }

        private void Button_Resize(object sender, EventArgs e)
        {
            if (borderRadius > this.Height)
                borderRadius = this.Height;
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
            Rectangle rectProgressSurface = new Rectangle
            {
                Height = this.Height,
                Width = (int)((float)(Width / 100.0f) * progress)
            };
            int smoothSize = 2;
            if (borderRadius > 2)
            {
                using (GraphicsPath pathSurface = GetFigurePath(rectSurface, borderRadius))
                using (SolidBrush progressBrush = new SolidBrush(this.progressColor))
                using (Pen penSurface = new Pen(this.Parent.BackColor, smoothSize))
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (this._hover)
                    {
                        pe.Graphics.FillRectangle(new SolidBrush(this.hoverColor), rectSurface);
                    } else
                    {
                        pe.Graphics.FillRectangle(new SolidBrush(this.BackColor), rectSurface);
                    }
                    pe.Graphics.FillRectangle(progressBrush, rectProgressSurface);
                    if (this._icon != null)
                    {
                        pe.Graphics.DrawImage(this._icon, rectSurface);
                    }
                    this.Region = new Region(pathSurface);
                    pe.Graphics.DrawPath(penSurface, pathSurface);
                    TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
                    if (progress > 0)
                    {
                        TextRenderer.DrawText(pe.Graphics, String.Format("{0}%", progress), Font, ClientRectangle, ForeColor, flags);
                    } else
                    {
                        TextRenderer.DrawText(pe.Graphics, text, Font, ClientRectangle, ForeColor, flags);
                    }
                    
                }
            }

           
        }
    }
}
