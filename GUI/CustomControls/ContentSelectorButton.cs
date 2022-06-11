using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class ContentSelectorButton : PictureBox
    {

        private bool _notification = false;
        private bool _selected = false;

        public void SetNotification(bool notification)
        {
            this._notification = notification;
            this.Invalidate();
        }

        public bool Selected { 
            get { return this._selected; } 
            set
            {
                this._selected = value;
                this.Invalidate();
            }
        }

        public ContentSelectorButton()
        {
            this.BackColor = Color.Transparent;
            this.BackgroundImageLayout = ImageLayout.Stretch;
            this.ForeColor = Color.White;
            this.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            this.Text = "";
            this.Height = 44;
            this.Width = 44;
            this.Margin = new Padding(left: 0, top: 3, right: 0, bottom: 3);
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
            if (_notification)
            {
                pe.Graphics.FillEllipse(Brushes.Red, this.Width - 12, 5, 10, 10);
            }
            if (this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)) && !this._selected)
            {
                pe.Graphics.FillRectangle(Brushes.White, this.Width - 3, 8, 3, this.Height - 16);
            }
            if (this._selected)
            {
                pe.Graphics.FillRectangle(new SolidBrush(Colors.WindowsAccentColor), this.Width - 3, 4, 3, this.Height - 8);
            }
        }
    }
}
