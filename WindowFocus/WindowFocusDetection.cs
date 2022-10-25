using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.WindowsFocus
{
    public class WindowFocusDetection
    {
        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);


        public event EventHandler OnWindowFocusChanged;
        private string _focusedApplication = "";

        static WinEventDelegate dele;

        public WindowFocusDetection()
        {
            dele = WinEventProc;
            var m_hhook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, dele, 0, 0, WINEVENT_OUTOFCONTEXT);

        }

        private uint GetActiveWindowProcessId(IntPtr hwnd)
        {
            uint processId = 0;
            var handle = IntPtr.Zero;

            if (GetWindowThreadProcessId(hwnd, out processId) > 0)
            {
                return processId;
            }

            return 0;
        }

        public void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                var processId = GetActiveWindowProcessId(hwnd);
                using (var process = Process.GetProcessById((int)processId))
                {
                    if (process.ProcessName != _focusedApplication)
                    {
                        _focusedApplication = process.ProcessName;
                        OnWindowFocusChanged?.Invoke(_focusedApplication, EventArgs.Empty);
                        VariableManager.SetValue("focused_application", process.ProcessName, VariableType.String, "Macro Deck");
                    }
                }
            }
            catch { }
        }
    }
}
