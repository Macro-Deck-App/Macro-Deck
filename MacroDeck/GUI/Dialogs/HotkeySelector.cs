using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Hotkeys;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class HotkeySelector : DialogForm
{
    public HotkeySelector()
    {
        InitializeComponent();

        lblPressKeysNow.Text = LanguageManager.Strings.PressTheKeysNow;
    }

    public new Keys ModifierKeys;
    public Keys Key;

    private void HotkeySelector_Load(object sender, EventArgs e)
    {
        HotkeyManager.Pause();
        KeyDown += HotkeySelector_KeyDown;
        KeyUp += HotkeySelector_KeyUp;
    }

    private void HotkeySelector_KeyUp(object sender, KeyEventArgs e)
    {
        lblDetectedKeys.Text = string.Empty;
    }

    private void HotkeySelector_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsModifierKey(e.KeyCode))
        {
            KeyUp -= HotkeySelector_KeyUp;
            lblDetectedKeys.Text += ", " + e.KeyCode;
            Key = e.KeyCode;
            DialogResult = DialogResult.OK;
            Close();
        } else
        {
            lblDetectedKeys.Text = e.Modifiers.ToString();
            ModifierKeys = e.Modifiers;
        }
    }


    private bool IsModifierKey(Keys keyCode)
    {
        switch (keyCode)
        {
            case Keys.Control:
            case Keys.ControlKey:
            case Keys.LControlKey:
            case Keys.RControlKey:
            case Keys.Shift:
            case Keys.ShiftKey:
            case Keys.LShiftKey:
            case Keys.RShiftKey:
            case Keys.Alt:
            case Keys.Menu:
            case Keys.LMenu:
            case Keys.RMenu:
                return true;
            default:
                return false;
        }
    }
}