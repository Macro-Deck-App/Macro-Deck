using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Controls;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public class NotificationButton : ButtonPrimary
    {

        private int _notificationCount = 0;

        public int NotificationCount
        {
            get => _notificationCount;
            set
            {
                _notificationCount = value;
                if (!DesignMode)
                {
                    this.Visible = value > 0;
                }
                this.Invalidate();
            }
        }



        public NotificationButton()
        {
            this.Text = "";
            if (!DesignMode)
            {
                this.Visible = this._notificationCount > 0;
            }
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);

            Rectangle indicatorRectangle = new Rectangle(this.Width - 18 - this.Padding.Right, this.Padding.Top, 18, 18);

            using (SolidBrush indicatorBrush = new SolidBrush(Color.Red))
            {
                pe.Graphics.FillEllipse(indicatorBrush, indicatorRectangle);
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
                TextRenderer.DrawText(pe.Graphics, this._notificationCount.ToString(), Font, indicatorRectangle, ForeColor, flags);

            }
        }

    }
}
