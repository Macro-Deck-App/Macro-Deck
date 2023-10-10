using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.ActionButton.Plugin;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor;

public partial class ActionSelectorOnPress : RoundedUserControl
{
    private List<PluginAction?> _pluginActions;

    private GUI.ButtonEditor _buttonEditor;

    public ActionSelectorOnPress(List<PluginAction?> pluginActions, GUI.ButtonEditor buttonEditor)
    {
        _pluginActions = pluginActions;
        _buttonEditor = buttonEditor;
        InitializeComponent();
        Dock = DockStyle.Fill;
        menuItemAction.Text = LanguageManager.Strings.Action;
        menuItemCondition.Text = LanguageManager.Strings.Condition;
        menuItemDelay.Text = LanguageManager.Strings.Delay;
    }

    private void AddActionItem(PluginAction? action)
    {
        if (action.GetType() == typeof(ConditionAction))
        {
            var conditionItem = new ConditionItem(action);
            actionsOnPress.Controls.Add(conditionItem);
            conditionItem.OnRemoveClick += RemoveClicked;
            conditionItem.OnMoveUpClick += MoveUpClicked;
            conditionItem.OnMoveDownClick += MoveDownClicked;
        }
        else if (action.GetType() == typeof(DelayAction))
        {
            var delayItem = new DelayItem(action);
            actionsOnPress.Controls.Add(delayItem);
            delayItem.OnRemoveClick += RemoveClicked;
            delayItem.OnMoveUpClick += MoveUpClicked;
            delayItem.OnMoveDownClick += MoveDownClicked;
        }
        else
        {
            var actionItem = new ActionItem(action);
            actionsOnPress.Controls.Add(actionItem);
            actionItem.OnRemoveClick += RemoveClicked;
            actionItem.OnEditClick += EditClicked;
            actionItem.OnMoveUpClick += MoveUpClicked;
            actionItem.OnMoveDownClick += MoveDownClicked;
        }
    }

    public void RefreshActions()
    {
        SuspendLayout();
        foreach (IActionConditionItem actionItem in actionsOnPress.Controls)
        {
            actionItem.OnRemoveClick -= RemoveClicked;
            actionItem.OnEditClick -= EditClicked;
            actionItem.OnMoveUpClick -= MoveUpClicked;
            actionItem.OnMoveDownClick -= MoveDownClicked;

        }
        actionsOnPress.Controls.Clear();
        foreach (var action in _pluginActions)
        {
            AddActionItem(action);
        }
        ResumeLayout();
    }

    private void MoveUpClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        var action = actionItem.Action;
        var currentIndex = _pluginActions.IndexOf(action);
        if (currentIndex == 0) return;
        _pluginActions.RemoveAt(currentIndex);
        _pluginActions.Insert(currentIndex - 1, action);
        actionsOnPress.Controls.SetChildIndex((Control)actionItem, currentIndex - 1);
    }

    private void MoveDownClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        var action = actionItem.Action;
        var currentIndex = _pluginActions.IndexOf(action);
        if (currentIndex + 1 >= _pluginActions.Count) return;
        _pluginActions.RemoveAt(currentIndex);
        _pluginActions.Insert(currentIndex + 1, action);
        actionsOnPress.Controls.SetChildIndex((Control)actionItem, currentIndex + 1);
    }

    private void RemoveClicked(object sender, EventArgs e)
    {
        SuspendLayout();
        var actionItem = sender as IActionConditionItem;
        _pluginActions.Remove(actionItem.Action);
        actionItem.OnRemoveClick -= RemoveClicked;
        actionsOnPress.Controls.Remove((Control)actionItem);
        ((Control)actionItem).Dispose();
        ResumeLayout();
    }

    private void EditClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        using var configurator = new ActionConfigurator(actionItem.Action);
        if (configurator.ShowDialog() == DialogResult.OK)
        {
            SuspendLayout();
            if (configurator.Action.CanConfigure && configurator.Action.Configuration.Length == 0) return;
            var index = _pluginActions.IndexOf(actionItem.Action);
            _pluginActions.RemoveAt(index);
            _pluginActions.Insert(index, configurator.Action);
            RefreshActions();
            ResumeLayout();
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
            _pluginActions.Add(actionConfigurator.Action);
            AddActionItem(actionConfigurator.Action);
            if (!string.IsNullOrWhiteSpace(actionConfigurator.Action.BindableVariable))
            {
                using var msgBox = new MessageBox();
                if (msgBox.ShowDialog(LanguageManager.Strings.BindableVariable, string.Format(LanguageManager.Strings.BindableVariableInfo, actionConfigurator.Action.BindableVariable), MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _buttonEditor.SetBindableVariable(actionConfigurator.Action.BindableVariable);
                }
            }
        }
    }

    private void MenuItemCondition_Click(object sender, EventArgs e)
    {
        var conditionItem = new ConditionItem();
        _pluginActions.Add(conditionItem.Action);
        AddActionItem(conditionItem.Action);
    }

    private void ActionSelectorOnPress_Load(object sender, EventArgs e)
    {

    }

    private void DelayToolStripMenuItem_Click(object sender, EventArgs e)
    {
        var delayItem = new DelayItem();
        _pluginActions.Add(delayItem.Action);
        AddActionItem(delayItem.Action);
    }
}