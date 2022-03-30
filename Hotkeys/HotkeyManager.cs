using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Input;

namespace SuchByte.MacroDeck.Hotkeys
{
    public class HotkeyManager : NativeWindow
    {
        // DLL libraries used to manage hotkeys
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public static Dictionary<ActionButton.ActionButton, int> Hotkeys = new Dictionary<ActionButton.ActionButton, int>();

        private static Random random = new Random();

        private static IntPtr formHandle;

        private static bool paused = false;

        public HotkeyManager()
        {
            this.CreateHandle(new CreateParams());
            formHandle = this.Handle;
        }


        public static void AddHotkey(ActionButton.ActionButton actionButton, Keys modifierKeys, Keys key)
        {
            RemoveHotkey(actionButton);
            if (key == Keys.None) return;
            int hotkeyId = random.Next(Int32.MaxValue);
            Hotkeys[actionButton] = hotkeyId;
            int modifierKeyCode = 0;

            if ((modifierKeys & Keys.Control) == Keys.Control)
            {
                modifierKeyCode += (int)ModifierKeyCode.CTRL;
            }
            if ((modifierKeys & Keys.Shift) == Keys.Shift)
            {
                modifierKeyCode += (int)ModifierKeyCode.SHIFT;
            }
            if ((modifierKeys & Keys.Alt) == Keys.Alt)
            {
                modifierKeyCode += (int)ModifierKeyCode.ALT;
            }

            RegisterHotKey(formHandle, hotkeyId, (int)modifierKeyCode, (int)key);
            MacroDeckLogger.Info(String.Format("Registered hotkey #{0} ({1}) with modifier(s): {2}", hotkeyId, key.ToString(), modifierKeys.ToString()));
        }

        public static void Pause()
        {
            paused = true;
        }

        public static void Resume()
        {
            paused = false;
        }

        public static void RemoveHotkey(ActionButton.ActionButton actionButton)
        {
            if (!Hotkeys.ContainsKey(actionButton)) return;
            int hotkeyId = Hotkeys[actionButton];
            UnregisterHotKey(formHandle, hotkeyId);
            Hotkeys.Remove(actionButton);
            MacroDeckLogger.Info(String.Format("Unregistered hotkey #{0}", hotkeyId));
        }


        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0312:
                    if (paused) break;
                    var hotkeyId = m.WParam.ToInt32();
                    var actionButton = Hotkeys.FirstOrDefault(x => x.Value.Equals(hotkeyId)).Key;
                    if (actionButton != null)
                    {
                        try
                        {
                            MacroDeckServer.ExecutePress(actionButton, "");
                        }
                        catch { }
                    }
                    break;
            }
            base.WndProc(ref m);
        }



    }
}
