using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class DialogForm : System.Windows.Forms.Form
{


    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HT_CAPTION = 0x2;
    [DllImportAttribute("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImportAttribute("user32.dll")]
    public static extern bool ReleaseCapture();

    public bool IgnoreEscapeKey = false;

    public DialogForm()
    {
        InitializeComponent();
        (new DropShadow()).ApplyShadows(this);
        ResizeEnd += OnResizeEnd;
        MouseDown += DialogForm_MouseDown;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (IgnoreEscapeKey) return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnResizeEnd(object sender, EventArgs e)
    {
        btnClose.Location = new Point(Width - btnClose.Width - 2, 2);
    }

    public void SetCloseIconVisible(bool visible)
    {
        btnClose.Visible = visible;
    }
        

    private void Btn_close_Click(object sender, EventArgs e)
    {
        Close();
    }


    private void DialogForm_Load(object sender, EventArgs e)
    {
        btnClose.Location = new Point(Width - btnClose.Width - 2, 2);
    }

    private void DialogForm_MouseDown(object sender, MouseEventArgs e)
    {
        if (PointToClient(Cursor.Position).Y <= 25)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

}