using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class TabRadioButton : RadioButton
{
    public TabRadioButton()
    {
        InitializeComponent();
        Appearance = Appearance.Normal;
        Cursor = Cursors.Hand;
        MouseEnter += MouseEnterEvent;
        MouseLeave += MouseLeaveEvent;
    }


    private void MouseEnterEvent(object sender, EventArgs e)
    {
        Invalidate();
    }

    private void MouseLeaveEvent(object sender, EventArgs e)
    {
        Invalidate();
    }


    protected override void OnPaint(PaintEventArgs pe)
    {
        base.OnPaint(pe);

        var rectSurface = ClientRectangle;
        var rectSelected = new Rectangle
        {
            Height = 3,
            Width = rectSurface.Width - 8,
            X = (rectSurface.Width / 2) - (Width / 2) + 4,
            Y = rectSurface.Height - 3,
        };
        var rectHover = new Rectangle
        {
            Height = 3,
            Width = rectSurface.Width - 16,
            X = (rectSurface.Width / 2) - (Width / 2) + 8,
            Y = rectSurface.Height - 3,
        };
        using (var backgroundBrush = new SolidBrush(Parent.BackColor))
        using (var selectedBrush = new SolidBrush(!DesignMode ? Colors.WindowsAccentColor : Colors.DefaultAccentColor))
        using (var hoverBrush = new SolidBrush(Color.White))
        {
            pe.Graphics.FillRectangle(backgroundBrush, rectSurface);
            if (ClientRectangle.Contains(PointToClient(Cursor.Position)) && !Checked)
            {
                pe.Graphics.FillRectangle(hoverBrush, rectHover);
            }
            if (Checked)
            {
                pe.Graphics.FillRectangle(selectedBrush, rectSelected);
            }
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
        TextRenderer.DrawText(pe.Graphics, Text, Font, ClientRectangle, ForeColor, flags);

    }
}