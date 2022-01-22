using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class TabRadioButton : System.Windows.Forms.RadioButton
    {
        public TabRadioButton()
        {
            InitializeComponent();
            this.Appearance = Appearance.Normal;
            this.Cursor = Cursors.Hand;
            this.MouseEnter += MouseEnterEvent;
            this.MouseLeave += MouseLeaveEvent;
        }


        private void MouseEnterEvent(object sender, EventArgs e)
        {
            this.Invalidate();
        }

        private void MouseLeaveEvent(object sender, EventArgs e)
        {
            this.Invalidate();
        }


        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);

            Rectangle rectSurface = this.ClientRectangle;
            Rectangle rectSelected = new Rectangle
            {
                Height = 3,
                Width = rectSurface.Width - 8,
                X = (rectSurface.Width / 2) - (Width / 2) + 4,
                Y = rectSurface.Height - 3,
            };
            Rectangle rectHover = new Rectangle
            {
                Height = 3,
                Width = rectSurface.Width - 16,
                X = (rectSurface.Width / 2) - (Width / 2) + 8,
                Y = rectSurface.Height - 3,
            };
            using (SolidBrush backgroundBrush = new SolidBrush(this.Parent.BackColor))
            using (SolidBrush selectedBrush = new SolidBrush(Colors.WindowsAccentColor))
            using (SolidBrush hoverBrush = new SolidBrush(Color.White))
            {
                pe.Graphics.FillRectangle(backgroundBrush, rectSurface);
                if (this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)) && !this.Checked)
                {
                    pe.Graphics.FillRectangle(hoverBrush, rectHover);
                }
                if (this.Checked)
                {
                    pe.Graphics.FillRectangle(selectedBrush, rectSelected);
                }
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
            TextRenderer.DrawText(pe.Graphics, this.Text, Font, ClientRectangle, ForeColor, flags);

        }
    }
}
