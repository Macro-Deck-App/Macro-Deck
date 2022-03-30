using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Hotkeys;
using SuchByte.MacroDeck.Language;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class HotkeySelector : DialogForm
    {
        public HotkeySelector()
        {
            InitializeComponent();

            this.lblPressKeysNow.Text = LanguageManager.Strings.PressTheKeysNow;
        }

        public new Keys ModifierKeys;
        public Keys Key;

        private void HotkeySelector_Load(object sender, EventArgs e)
        {
            HotkeyManager.Pause();
            this.KeyDown += HotkeySelector_KeyDown;
            this.KeyUp += HotkeySelector_KeyUp;
        }

        private void HotkeySelector_KeyUp(object sender, KeyEventArgs e)
        {
            this.lblDetectedKeys.Text = string.Empty;
        }

        private void HotkeySelector_KeyDown(object sender, KeyEventArgs e)
        {
            if (!IsModifierKey(e.KeyCode))
            {
                this.KeyUp -= HotkeySelector_KeyUp;
                this.lblDetectedKeys.Text += ", " + e.KeyCode.ToString();
                this.Key = e.KeyCode;
                this.DialogResult = DialogResult.OK;
                this.Close();
            } else
            {
                this.lblDetectedKeys.Text = e.Modifiers.ToString();
                this.ModifierKeys = e.Modifiers;
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
}
