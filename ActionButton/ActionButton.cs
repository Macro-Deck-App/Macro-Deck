using SuchByte.MacroDeck.Plugins;
using Newtonsoft.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.JSON;
using System.ComponentModel;
using System.Runtime.InteropServices;
using SuchByte.MacroDeck.Server;
using System.Diagnostics;
using System.Drawing;

namespace SuchByte.MacroDeck.ActionButton
{
    public class ActionButton : IDisposable
    {
        private IntPtr _bufferPtr;
        private int BUFFER_SIZE = 1024 * 1024; // 1 MB
        private bool _disposed = false;


        public ActionButton()
        {
            _bufferPtr = Marshal.AllocHGlobal(BUFFER_SIZE);
            Variables.VariableManager.OnVariableChanged += VariableChanged;
        }

        public void UpdateBindingState()
        {
            if (!this.StateBindingVariable.Equals(""))
            {
                Variables.Variable variable = Variables.VariableManager.Variables.Find(v => v.Name.Equals(this.StateBindingVariable));
                if (variable != null)
                {
                    this.UpdateBindingState(variable);
                }
            }
        }



        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
            }

            Variables.VariableManager.OnVariableChanged -= VariableChanged;
            // Free any unmanaged objects here.
            Marshal.FreeHGlobal(_bufferPtr);
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ActionButton()
        {
            Dispose(false);
        }

        private void VariableChanged(object sender, EventArgs e)
        {
            if (this.StateBindingVariable.Equals("")) return;
            Variables.Variable variable = sender as Variables.Variable;
            this.UpdateBindingState(variable);
        }

        private void UpdateBindingState(Variables.Variable variable)
        {
            if (variable != null && variable.Name.Equals(this.StateBindingVariable))
            {
                bool newState = false;
                Boolean.TryParse(variable.Value, out newState);
                if (variable.Value.ToString().ToLower().Equals("on")) newState = true;
                this.State = newState;
            }
        }


        public event EventHandler StateChanged;
        public long ButtonId { get; set; }
        private bool _state = false;
        public bool State
        {
            get { return _state; }
            set
            {
                _state = value;
                MacroDeckServer.UpdateState(this);
                if (StateChanged != null)
                {
                    StateChanged(this, EventArgs.Empty);
                }
            }
        }

        public string IconOff { get; set; }
        public string IconOn { get; set; }
        public ButtonLabel LabelOff { get; set; }
        public ButtonLabel LabelOn { get; set; }
        public int Position_X { get; set; }
        public int Position_Y { get; set; }
        public string StateBindingVariable { get; set; } = "";
        public List<PluginAction> Actions { get; set; }
        public Dictionary<string, List<PluginAction>> EventActions { get; set; }


       
    }
}
