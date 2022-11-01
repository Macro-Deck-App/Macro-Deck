using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SuchByte.MacroDeck.Enums;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.Hotkeys;

public class HotkeyManager : NativeWindow
{
    // DLL libraries used to manage hotkeys
    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static Dictionary<ActionButton.ActionButton, int> Hotkeys = new();

    private static Random random = new();

    private static IntPtr formHandle;

    private static bool paused;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams());
        formHandle = Handle;
    }


    public static void AddHotkey(ActionButton.ActionButton actionButton, Keys modifierKeys, Keys key)
    {
        RemoveHotkey(actionButton);
        if (key == Keys.None) return;
        var hotkeyId = random.Next(int.MaxValue);
        Hotkeys[actionButton] = hotkeyId;
        var modifierKeyCode = 0;

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

        RegisterHotKey(formHandle, hotkeyId, modifierKeyCode, (int)key);
        MacroDeckLogger.Info(string.Format("Registered hotkey #{0} ({1}) with modifier(s): {2}", hotkeyId, key.ToString(), modifierKeys.ToString()));
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
        var hotkeyId = Hotkeys[actionButton];
        UnregisterHotKey(formHandle, hotkeyId);
        Hotkeys.Remove(actionButton);
        MacroDeckLogger.Info(string.Format("Unregistered hotkey #{0}", hotkeyId));
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
                        MacroDeckServer.Execute(actionButton, "", ButtonPressType.SHORT);
                    }
                    catch { }
                }
                break;
        }
        base.WndProc(ref m);
    }



}