namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class Form : System.Windows.Forms.Form
{
    public event EventHandler FormWindowStateChanged;

    public Form()
    {
        InitializeComponent();
    }

    protected override void WndProc(ref Message m)
    {
        var originalState = WindowState;
        base.WndProc(ref m);
        if (WindowState == originalState)
        {
            return;
        }

        FormWindowStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
