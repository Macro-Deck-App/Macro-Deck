using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

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

        static WinEventDelegate dele = null;

        public WindowFocusDetection()
        {
            /*try
            {
                Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
            } catch { }*/
            dele = new WinEventDelegate(WinEventProc);
            IntPtr m_hhook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, dele, 0, 0, WINEVENT_OUTOFCONTEXT);

        }

        private uint GetActiveWindowProcessId(IntPtr hwnd)
        {
            uint processId = 0;
            IntPtr handle = IntPtr.Zero;

            if (GetWindowThreadProcessId(hwnd, out processId) > 0)
            {
                return processId;
            }

            return 0;
        }

        public void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            Task.Run(() =>
            {
                try
                {
                    uint processId = GetActiveWindowProcessId(hwnd);
                    using (Process process = Process.GetProcessById((int)processId))
                    {
                        if (process.ProcessName != this._focusedApplication)
                        {
                            this._focusedApplication = process.ProcessName;
                            if (this.OnWindowFocusChanged != null)
                            {
                                this.OnWindowFocusChanged(this._focusedApplication, EventArgs.Empty);
                            }
                            Variables.VariableManager.SetValue("Focused application", process.ProcessName, Variables.VariableType.String, "Macro Deck", false);
                        }
                    }
                }
                catch { }
            });
        }

        /*private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    if (sender == null) return;
                    AutomationElement focusedElement = sender as AutomationElement;
                    if (focusedElement != null)
                    {
                        int processId = focusedElement.Current.ProcessId;
                        using (Process process = Process.GetProcessById(processId))
                        {
                            if (process.ProcessName != this._focusedApplication)
                            {
                                this._focusedApplication = process.ProcessName;
                                if (this.OnWindowFocusChanged != null)
                                {
                                    this.OnWindowFocusChanged(this._focusedApplication, EventArgs.Empty);
                                }
                                Variables.VariableManager.SetValue("Focused application", process.ProcessName, Variables.VariableType.String, "Macro Deck", false);
                            }
                        }

                    }
                } catch { }
            });
        }*/
    }
}
