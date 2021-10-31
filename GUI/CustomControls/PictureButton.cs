using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public class PictureButton : PictureBox
    {

        public Image HoverImage { 
            get { return this._hoverImage; }
            set { 
                this._hoverImage = value;
                this.Invalidate();
            }
        
        }

        private Image _hoverImage = null;

        private bool _hover = false;

        public PictureButton()
        {
            this.BackColor = Color.Transparent;
            this.BackgroundImageLayout = ImageLayout.Stretch;
            this.Cursor = Cursors.Hand;
            this.MouseEnter += MouseEnterEvent;
            this.MouseLeave += MouseLeaveEvent;
            this.MouseUp += MouseUpEvent;
        }

        private void MouseUpEvent(object sender, EventArgs e)
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

        protected override void OnPaint(PaintEventArgs pe)
        {
            if (this._hover)
            {
                if (this._hoverImage != null)
                {
                    pe.Graphics.DrawImage(this._hoverImage, new Rectangle(0, 0, this.Width, this.Height));
                }
            } else
            {
                base.OnPaint(pe);
            }
        }

    }
}
