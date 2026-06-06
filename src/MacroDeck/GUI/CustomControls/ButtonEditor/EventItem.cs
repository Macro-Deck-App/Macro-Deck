using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.ActionButton.Plugin;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor;

public partial class EventItem : UserControl
{

    public event EventHandler EventChanged;
    public event EventHandler OnRemoveClick;

    //public IMacroDeckEvent MacroDeckEvent { get { return _macroDeckEvent; } }
    //public List<PluginAction> Actions { get { return _macroDeckActions; } }

    public EventListener EventListener;

    private ActionButton.ActionButton _actionButton;
    //private IMacroDeckEvent _macroDeckEvent;
    //private List<PluginAction> _macroDeckActions = new List<PluginAction>();

    public EventItem(ActionButton.ActionButton actionButton, EventListener eventListener = null)
    {
        _actionButton = actionButton;
        EventListener = eventListener;
        if (EventListener == null)
        {
            EventListener = new EventListener();
        }

        InitializeComponent();
        menuItemAction.Text = LanguageManager.Strings.Action;
        menuItemCondition.Text = LanguageManager.Strings.Condition;
        menuItemDelay.Text = LanguageManager.Strings.Delay;
        lblTrigger.Text = LanguageManager.Strings.Trigger;
            
    }

    private void EventItem_Load(object sender, EventArgs e)
    {
        LoadEvents();
        RefreshActions();
    }


    private void LoadEvents()
    {
        eventBox.SelectedIndexChanged -= EventBox_SelectedIndexChanged;
        eventBox.Items.Clear();
        foreach (var macroDeckEvent in EventManager.RegisteredEvents)
        {
            //if (this._actionButton.EventActions.ContainsKey(macroDeckEvent.Name)) continue;
            eventBox.Items.Add(macroDeckEvent.Name);
        }
        eventBox.SelectedIndexChanged += EventBox_SelectedIndexChanged;
        eventParameter.SelectedIndexChanged += EventParameter_SelectedIndexChanged;
        if (EventListener != null && !string.IsNullOrWhiteSpace(EventListener.EventToListen))
        {
            if (!eventBox.Items.Contains(EventListener.EventToListen))
            {
                eventBox.Items.Add(EventListener.EventToListen);
            }
            eventBox.Text = EventListener.EventToListen;
            eventParameter.Text = EventListener.Parameter;
        }
    }

    private void EventParameter_SelectedIndexChanged(object sender, EventArgs e)
    {
        EventListener.Parameter = eventParameter.Text;
    }

    private void EventBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        var macroDeckEvent = EventManager.GetEventByName(eventBox.Text);
        if (macroDeckEvent == null) return;
        EventListener.EventToListen = macroDeckEvent.Name;
        eventParameter.Items.AddRange(macroDeckEvent.ParameterSuggestions.ToArray());
        EventChanged?.Invoke(EventListener.EventToListen, EventArgs.Empty);
    }

    private void AddActionItem(PluginAction? action)
    {
        if (action.GetType() == typeof(ConditionAction))
        {
            var conditionItem = new ConditionItem(action);
            actionsList.Controls.Add(conditionItem);
            conditionItem.OnRemoveClick += RemoveClicked;
        }
        else if (action.GetType() == typeof(DelayAction))
        {
            var delayItem = new DelayItem(action);
            actionsList.Controls.Add(delayItem);
            delayItem.OnRemoveClick += RemoveClicked;
            delayItem.OnMoveUpClick += MoveUpClicked;
            delayItem.OnMoveDownClick += MoveDownClicked;
        }
        else
        {
            var actionItem = new ActionItem(action);
            actionsList.Controls.Add(actionItem);
            actionItem.OnRemoveClick += RemoveClicked;
            actionItem.OnEditClick += EditClicked;
            actionItem.OnMoveUpClick += MoveUpClicked;
            actionItem.OnMoveDownClick += MoveDownClicked;
        }
    }
       

    private void RefreshActions()
    {
        SuspendLayout();
        foreach (IActionConditionItem actionItem in actionsList.Controls)
        {
            actionItem.OnRemoveClick -= RemoveClicked;
            actionItem.OnEditClick -= EditClicked;
            actionItem.OnMoveUpClick -= MoveUpClicked;
            actionItem.OnMoveDownClick -= MoveDownClicked;
        }
        actionsList.Controls.Clear();
        foreach (var action in EventListener.Actions)
        {
            AddActionItem(action);
        }
        ResumeLayout();
    }

    private void MoveUpClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        var action = actionItem.Action;
        if (EventListener.Actions.Contains(action))
        {
            var currentIndex = EventListener.Actions.IndexOf(action);
            if (currentIndex == 0) return;
            EventListener.Actions.RemoveAt(currentIndex);
            EventListener.Actions.Insert(currentIndex - 1, action);
            actionsList.Controls.SetChildIndex((Control)actionItem, currentIndex - 1);
        }
    }

    private void MoveDownClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        var action = ((IActionConditionItem)sender).Action;
        if (EventListener.Actions.Contains(action))
        {
            var currentIndex = EventListener.Actions.IndexOf(action);
            if (currentIndex + 1 >= EventListener.Actions.Count) return;
            EventListener.Actions.RemoveAt(currentIndex);
            EventListener.Actions.Insert(currentIndex + 1, action);
            actionsList.Controls.SetChildIndex((Control)actionItem, currentIndex + 1);
        }
    }


    private void RemoveClicked(object sender, EventArgs e)
    {
        SuspendLayout();
        var actionItem = sender as IActionConditionItem;
        if (EventListener.Actions.Contains(actionItem.Action))
        {
            EventListener.Actions.Remove(actionItem.Action);
            actionsList.Controls.Remove((Control)actionItem);
        }
        actionItem.OnRemoveClick -= RemoveClicked;
        ((Control)actionItem).Dispose();
        ResumeLayout();
    }

    private void EditClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        using var configurator = new ActionConfigurator(actionItem.Action);
        if (configurator.ShowDialog() == DialogResult.OK)
        {
            if (configurator.Action.CanConfigure && configurator.Action.Configuration.Length == 0) return;
            if (EventListener.Actions.Contains(actionItem.Action))
            {
                var index = EventListener.Actions.IndexOf(actionItem.Action);
                EventListener.Actions.RemoveAt(index);
                EventListener.Actions.Insert(index, configurator.Action);
            }

            RefreshActions();
        }
    }




    private void BtnAdd_Click(object sender, EventArgs e)
    {
        addItemContextMenu.Show(btnAdd, new Point(0, 0 + btnAdd.Height));
    }

    private void MenuItemAction_Click(object sender, EventArgs e)
    {
        using var actionConfigurator = new ActionConfigurator();
        if (actionConfigurator.ShowDialog() == DialogResult.OK)
        {
            EventListener.Actions.Add(actionConfigurator.Action);
            AddActionItem(actionConfigurator.Action);
        }
    }

    private void MenuItemCondition_Click(object sender, EventArgs e)
    {
        var conditionItem = new ConditionItem();
        EventListener.Actions.Add(conditionItem.Action);
        AddActionItem(conditionItem.Action);
    }

    private void BtnRemove_Click(object sender, EventArgs e)
    {
        OnRemoveClick?.Invoke(this, EventArgs.Empty);
    }

    private void MenuItemDelay_Click(object sender, EventArgs e)
    {
        var delayItem = new DelayItem();
        EventListener.Actions.Add(delayItem.Action);
        AddActionItem(delayItem.Action);
    }
}