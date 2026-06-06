using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        InitializeComponent();
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.ContainerControl |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor
            , true);
    }
}