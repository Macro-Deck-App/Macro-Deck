using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public class PictureButton : PictureBox
{

    public Image HoverImage { 
        get => _hoverImage;
        set { 
            _hoverImage = value;
            Invalidate();
        }
        
    }

    private Image _hoverImage;

    private bool _hover;

    public PictureButton()
    {
        BackColor = Color.Transparent;
        BackgroundImageLayout = ImageLayout.Stretch;
        Cursor = Cursors.Hand;
        MouseEnter += MouseEnterEvent;
        MouseLeave += MouseLeaveEvent;
        MouseUp += MouseUpEvent;
    }

    private void MouseUpEvent(object sender, EventArgs e)
    {
        _hover = false;
        Invalidate();
    }

    private void MouseEnterEvent(object sender, EventArgs e)
    {
        _hover = true;
        Invalidate();
    }

    private void MouseLeaveEvent(object sender, EventArgs e)
    {
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        if (_hover)
        {
            if (_hoverImage != null)
            {
                pe.Graphics.DrawImage(_hoverImage, new Rectangle(0, 0, Width, Height));
            }
        } else
        {
            base.OnPaint(pe);
        }
    }

}