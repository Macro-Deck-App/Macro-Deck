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
        private int BUFFER_SIZE = 1024 * 1024;
        private bool _disposed = false;


        public ActionButton()
        {
            _bufferPtr = Marshal.AllocHGlobal(BUFFER_SIZE);
            Variables.VariableManager.OnVariableChanged += VariableChanged;
        }

        public void UpdateBindingState()
        {
            if (!String.IsNullOrWhiteSpace(this.StateBindingVariable))
            {
                Variables.Variable variable = Variables.VariableManager.Variables.Find(v => v.Name.Equals(this.StateBindingVariable));
                if (variable != null)
                {
                    this.UpdateBindingState(variable);
                }
            }
        }

        public bool IsDisposed
        {
            get
            {
                return this._disposed;
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
            if (this.Actions != null)
            {
                foreach (PluginAction pluginAction in this.Actions)
                {
                    pluginAction.OnActionButtonDelete();
                }
            }
            if (this.EventListeners != null)
            {
                foreach (var eventListeners in this.EventListeners)
                {
                    foreach (PluginAction pluginAction in eventListeners.Actions)
                    {
                        pluginAction.OnActionButtonDelete();
                    }
                }
            }

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
        public event EventHandler IconChanged;
        public long ButtonId { get; set; }
        private bool _state = false;
        public bool State
        {
            get { return _state; }
            set
            {
                if (_state == value) return;
                _state = value;
                MacroDeckServer.UpdateState(this);
                if (StateChanged != null)
                {
                    StateChanged(this, EventArgs.Empty);
                }
            }
        }

        private string _iconOff = "";
        public string IconOff { 
            get
            {
                return this._iconOff;
            }
            set
            {
                this._iconOff = value;
                if (IconChanged != null)
                {
                    IconChanged(this, EventArgs.Empty);
                }
            }
        }
        private string _iconOn = "";
        public string IconOn
        {
            get
            {
                return this._iconOn;
            }
            set
            {
                this._iconOn = value;
                if (IconChanged != null)
                {
                    IconChanged(this, EventArgs.Empty);
                }
            }
        }
        public ButtonLabel LabelOff { get; set; }
        public ButtonLabel LabelOn { get; set; }
        public int Position_X { get; set; }
        public int Position_Y { get; set; }
        public string StateBindingVariable { get; set; } = "";
        public List<PluginAction> Actions { get; set; }
        public List<EventListener> EventListeners { get; set; }
       
    }
}
