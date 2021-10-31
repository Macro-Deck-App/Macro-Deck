using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    internal class BorderlessComboBox : System.Windows.Forms.ComboBox
    {
        private const int WM_PAINT = 0xF;
        private int buttonWidth = SystemInformation.HorizontalScrollBarArrowWidth;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
            {
                using (var g = Graphics.FromHwnd(Handle))
                {                    
                    // Remove white border
                    using (var p = new Pen(this.Parent.BackColor, 1))
                    {
                        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                    }
                    if (!this.Enabled)
                    {
                        using (var p = new Pen(this.Parent.BackColor, 5))
                        {
                            g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                        }
                    }


                    // Remove white drop down button
                    using (SolidBrush brush = new SolidBrush(this.Parent.BackColor))
                    {
                        g.FillRectangle(brush, Width - buttonWidth - 5, 0, buttonWidth + 5, Height);
                    }
                    // Draw custom drop down button
                    using (SolidBrush brush = new SolidBrush(this.Enabled ? Color.White : Color.FromArgb(95, 95, 95)))
                    {
                        g.FillPolygon(brush, new Point[] { new Point(this.Width - 5, this.Height / 2 - 2), new Point(this.Width - 15, this.Height / 2 - 2), new Point(this.Width - 10, this.Height / 2 + 3) });
                    }
                }
                try
                {
                    this.SelectionLength = 0;
                } catch { }
            }
        }

    }
}
