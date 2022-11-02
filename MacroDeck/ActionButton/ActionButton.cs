using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Newtonsoft.Json;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Hotkeys;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.ActionButton;

public class ActionButton : IDisposable
{

    private IntPtr _bufferPtr;
    private int BUFFER_SIZE = 1024 * 1024;
    private bool _disposed;


    public ActionButton()
    {
        _bufferPtr = Marshal.AllocHGlobal(BUFFER_SIZE);
        VariableManager.OnVariableChanged += VariableChanged;
    }

    public void UpdateHotkey()
    {
        if (KeyCode != Keys.None)
        {
            HotkeyManager.AddHotkey(this, ModifierKeyCodes, KeyCode);
        }
    }

    public void UpdateBindingState()
    {
        if (!string.IsNullOrWhiteSpace(StateBindingVariable))
        {
            var variable = VariableManager.ListVariables.ToList().Find(v => v.Name.Equals(StateBindingVariable));
            if (variable != null)
            {
                UpdateBindingState(variable);
            }
        }
    }

    [JsonIgnore]
    public bool IsDisposed => _disposed;


    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
        }

        HotkeyManager.RemoveHotkey(this);
        VariableManager.OnVariableChanged -= VariableChanged;
        if (Actions != null)
        {
            foreach (var pluginAction in Actions)
            {
                pluginAction.OnActionButtonDelete();
            }
        }
        if (EventListeners != null)
        {
            foreach (var eventListeners in EventListeners)
            {
                foreach (var pluginAction in eventListeners.Actions)
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
        if (string.IsNullOrWhiteSpace(StateBindingVariable)) return;
        var variable = sender as Variable;
        UpdateBindingState(variable);
    }

    private void UpdateBindingState(Variable variable)
    {
        if (variable != null && variable.Name.Equals(StateBindingVariable))
        {
            bool.TryParse(variable.Value, out var newState);
            if (variable.Value.ToLower().Equals("on")) newState = true;
            State = newState;
        }
    }

    public string Guid { get; set; } = System.Guid.NewGuid().ToString();

    public event EventHandler StateChanged;
    public event EventHandler IconChanged;

    private bool _state;
    private string _iconOff = string.Empty;
    private string _iconOn = string.Empty;
    private Color _backgroundColorOff = Color.FromArgb(35, 35, 35);
    private Color _backgroundColorOn = Color.FromArgb(35, 35, 35);


    public bool State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            MacroDeckServer.UpdateState(this);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string IconOff { 
        get => _iconOff;

        set
        {
            _iconOff = value;
            IconChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string IconOn
    {
        get => _iconOn;
        set
        {
            _iconOn = value;
            IconChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Color BackColorOff
    {
        get => _backgroundColorOff;
        set
        {
            if (_backgroundColorOff == value) return;
            _backgroundColorOff = value;
            MacroDeckServer.UpdateState(this);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Color BackColorOn
    {
        get => _backgroundColorOn;
        set
        {
            if (_backgroundColorOn == value) return;
            _backgroundColorOn = value;
            MacroDeckServer.UpdateState(this);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ButtonLabel LabelOff { get; set; } = new();
    public ButtonLabel LabelOn { get; set; } = new();
    public int Position_X { get; set; } = -1;
    public int Position_Y { get; set; } = -1;
    public string StateBindingVariable { get; set; } = string.Empty;
    public List<PluginAction> Actions { get; set; } = new();
    public List<PluginAction> ActionsRelease { get; set; } = new();
    public List<PluginAction> ActionsLongPress { get; set; } = new();
    public List<PluginAction> ActionsLongPressRelease { get; set; } = new();
    public List<EventListener> EventListeners { get; set; } = new();
    public Keys ModifierKeyCodes { get; set; } = Keys.None;
    public Keys KeyCode { get; set; } = Keys.None;
        
}