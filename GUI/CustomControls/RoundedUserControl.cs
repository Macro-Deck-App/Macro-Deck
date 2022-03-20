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
    public partial class RoundedUserControl : UserControl
    {
        private int borderRadius = 8;

        public RoundedUserControl()
        {
            InitializeComponent();
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }



        private GraphicsPath GetFigurePath(Rectangle rect, int radius)
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graph = e.Graphics;

            if (borderRadius > 1)
            {
                var rectBorderSmooth = this.ClientRectangle;
                int smoothSize = 2;
                using (GraphicsPath pathBorderSmooth = GetFigurePath(rectBorderSmooth, borderRadius))
                using (Pen penBorderSmooth = new Pen(this.Parent.BackColor, smoothSize))
                {

                    this.Region = new Region(pathBorderSmooth);
                    graph.SmoothingMode = SmoothingMode.AntiAlias;
                    graph.DrawPath(penBorderSmooth, pathBorderSmooth);
                }
            }
            else
            {
                this.Region = new Region(this.ClientRectangle);
            }
        }
    }
}
