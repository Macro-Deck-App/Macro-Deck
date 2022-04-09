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
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Hotkeys;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.Dialogs;

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

        public void UpdateHotkey()
        {
            if (this.KeyCode != Keys.None)
            {
                HotkeyManager.AddHotkey(this, this.ModifierKeyCodes, this.KeyCode);
            }
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

        [JsonIgnore]
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

            HotkeyManager.RemoveHotkey(this);
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
            if (string.IsNullOrWhiteSpace(this.StateBindingVariable)) return;
            Variables.Variable variable = sender as Variables.Variable;
            this.UpdateBindingState(variable);
        }

        private void UpdateBindingState(Variables.Variable variable)
        {
            if (variable != null && variable.Name.Equals(this.StateBindingVariable))
            {
                Boolean.TryParse(variable.Value, out bool newState);
                if (variable.Value.ToString().ToLower().Equals("on")) newState = true;
                this.State = newState;
            }
        }

        public string Guid { get; set; } = System.Guid.NewGuid().ToString();

        public event EventHandler StateChanged;
        public event EventHandler IconChanged;

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

        private string _iconOff = string.Empty;
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
        private string _iconOn = string.Empty;
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

        public ButtonLabel LabelOff { get; set; } = new ButtonLabel();
        public ButtonLabel LabelOn { get; set; } = new ButtonLabel();
        public int Position_X { get; set; } = -1;
        public int Position_Y { get; set; } = -1;
        public string StateBindingVariable { get; set; } = string.Empty;
        public List<PluginAction> Actions { get; set; } = new List<PluginAction>();
        public List<PluginAction> ActionsRelease { get; set; } = new List<PluginAction>();
        public List<PluginAction> ActionsLongPress { get; set; } = new List<PluginAction>();
        public List<PluginAction> ActionsLongPressRelease { get; set; } = new List<PluginAction>();
        public List<EventListener> EventListeners { get; set; } = new List<EventListener>();
        public Keys ModifierKeyCodes { get; set; } = Keys.None;
        public Keys KeyCode { get; set; } = Keys.None;
        
    }
}
