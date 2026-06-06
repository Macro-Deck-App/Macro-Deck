using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

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
                using (var p = new Pen(Parent.BackColor, 1))
                {
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
                if (!Enabled)
                {
                    using var p = new Pen(Parent.BackColor, 5);
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }


                // Remove white drop down button
                using (var brush = new SolidBrush(Parent.BackColor))
                {
                    g.FillRectangle(brush, Width - buttonWidth - 5, 0, buttonWidth + 5, Height);
                }
                // Draw custom drop down button
                using (var brush = new SolidBrush(Enabled ? Color.White : Color.FromArgb(95, 95, 95)))
                {
                    g.FillPolygon(brush, new Point[] { new(Width - 5, Height / 2 - 2), new(Width - 15, Height / 2 - 2), new(Width - 10, Height / 2 + 3) });
                }
            }
            try
            {
                SelectionLength = 0;
            } catch { }
        }
    }

}