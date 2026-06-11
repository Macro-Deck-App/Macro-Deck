using Newtonsoft.Json;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Hotkeys;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.ActionButton;

public class ActionButton : IDisposable
{
    private bool _disposed;


    public ActionButton()
    {
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
            var variable = VariableManager.ListVariables.FirstOrDefault(v => v.Name == StateBindingVariable);
            if (variable != null)
            {
                UpdateBindingState(variable);
            }
        }
    }

    [JsonIgnore] public bool IsDisposed => _disposed;


    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
        }

        HotkeyManager.RemoveHotkey(this);
        VariableManager.OnVariableChanged -= VariableChanged;
        foreach (var pluginAction in Actions)
        {
            pluginAction?.OnActionButtonDelete();
        }
        
        foreach (var pluginAction in EventListeners.SelectMany(eventListeners => eventListeners.Actions))
        {
            pluginAction.OnActionButtonDelete();
        }

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
        if (string.IsNullOrWhiteSpace(StateBindingVariable))
        {
            return;
        }

        if (sender is Variable variable)
        {
            UpdateBindingState(variable);
        }
    }

    private void UpdateBindingState(Variable variable)
    {
        if (!variable.Name.Equals(StateBindingVariable))
        {
            return;
        }

        _ = bool.TryParse(variable.Value, out var newState);
        if (variable.Value.ToLower().Equals("on"))
        {
            newState = true;
        }

        State = newState;
    }

    public string Guid { get; set; } = System.Guid.CreateVersion7().ToString();

    public event EventHandler? StateChanged;
    public event EventHandler? IconChanged;

    public bool State
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            MacroDeckServer.UpdateState(this);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string IconOff
    {
        get;

        set
        {
            field = value;
            IconChanged?.Invoke(this, EventArgs.Empty);
        }
    } = string.Empty;

    public string IconOn
    {
        get;
        set
        {
            field = value;
            IconChanged?.Invoke(this, EventArgs.Empty);
        }
    } = string.Empty;

    public Color BackColorOff
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            MacroDeckServer.UpdateState(this);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    } = Color.FromArgb(65, 65, 65);

    public Color BackColorOn
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            MacroDeckServer.UpdateState(this);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    } = Color.FromArgb(65, 65, 65);

    public ButtonLabel? LabelOff { get; set; } = new();
    public ButtonLabel? LabelOn { get; set; } = new();
    // ReSharper disable once InconsistentNaming
    public int Position_X { get; set; } = -1;
    // ReSharper disable once InconsistentNaming
    public int Position_Y { get; set; } = -1;
    public string StateBindingVariable { get; set; } = string.Empty;
    public List<PluginAction?> Actions { get; set; } = new();
    public List<PluginAction?> ActionsRelease { get; set; } = new();
    public List<PluginAction?> ActionsLongPress { get; set; } = new();
    public List<PluginAction?> ActionsLongPressRelease { get; set; } = new();
    public List<EventListener> EventListeners { get; set; } = new();
    public Keys ModifierKeyCodes { get; set; } = Keys.None;
    public Keys KeyCode { get; set; } = Keys.None;
}
