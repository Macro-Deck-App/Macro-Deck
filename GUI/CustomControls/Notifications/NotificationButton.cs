using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.Notifications
{
    public class NotificationButton : ButtonPrimary
    {

        private int _notificationCount;

        public int NotificationCount
        {
            get => _notificationCount;
            set
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => NotificationCount = value));
                    return;
                }
                _notificationCount = value;
                if (!DesignMode)
                {
                    Visible = value > 0;
                }
                Invalidate();
            }
        }



        public NotificationButton()
        {
            Text = "";
            if (!DesignMode)
            {
                Visible = _notificationCount > 0;
            }
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);

            var indicatorRectangle = new Rectangle(Width - 18 - Padding.Right, Padding.Top, 18, 18);

            using (var indicatorBrush = new SolidBrush(Color.Red))
            {
                pe.Graphics.FillEllipse(indicatorBrush, indicatorRectangle);
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
                TextRenderer.DrawText(pe.Graphics, _notificationCount.ToString(), Font, indicatorRectangle, ForeColor, flags);

            }
        }

    }
}
