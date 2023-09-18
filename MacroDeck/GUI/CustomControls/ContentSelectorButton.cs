using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class ContentSelectorButton : PictureBox
{

    private bool _notification;
    private bool _selected;

    public void SetNotification(bool notification)
    {
        _notification = notification;
        Invalidate();
    }

    public bool Selected { 
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    public ContentSelectorButton()
    {
        BackColor = Color.Transparent;
        BackgroundImageLayout = ImageLayout.Stretch;
        ForeColor = Color.White;
        Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
        Text = "";
        Height = 44;
        Width = 44;
        Margin = new Padding(left: 0, top: 3, right: 0, bottom: 3);
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
        if (_notification)
        {
            pe.Graphics.FillEllipse(Brushes.Red, Width - 12, 5, 10, 10);
        }
        if (ClientRectangle.Contains(PointToClient(Cursor.Position)) && !_selected)
        {
            pe.Graphics.FillRectangle(Brushes.White, Width - 3, 8, 3, Height - 16);
        }
        if (_selected)
        {
            pe.Graphics.FillRectangle(new SolidBrush(Colors.AccentColor), Width - 3, 4, 3, Height - 8);
        }
    }
}