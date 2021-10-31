using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace SuchByte.MacroDeck.WindowsFocus
{
    public class WindowFocusDetection
    {

        public event EventHandler OnWindowFocusChanged;

        private string _focusedApplication = "";

        public WindowFocusDetection()
        {
            try
            {
                Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
            } catch { }
        }

        private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    if (sender == null) return;
                    AutomationElement focusedElement = sender as AutomationElement;
                    if (focusedElement != null)
                    {
                        if (Process.GetCurrentProcess().Id.Equals(focusedElement.Current.ProcessId))
                        {
                            return;
                        }

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
        }
    }
}
