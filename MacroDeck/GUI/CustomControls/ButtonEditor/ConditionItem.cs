using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.ActionButton.Plugin;
using SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class ConditionItem : UserControl, IActionConditionItem
{
    public PluginAction Action { get; set; } = new ConditionAction();

    public event EventHandler OnRemoveClick;
    public event EventHandler OnEditClick;
    public event EventHandler OnMoveUpClick;
    public event EventHandler OnMoveDownClick;

    string[] boolSuggestions = {
        "True",
        "False"
    };

    string[] stateSuggestions = {
        "On",
        "Off"
    };

    public ConditionItem(PluginAction macroDeckAction = null)
    {
        InitializeComponent();
        menuItemAction.Text = LanguageManager.Strings.Action;
        menuItemDelay.Text = LanguageManager.Strings.Delay;
        lblIf.Text = LanguageManager.Strings.If;
        lblElse.Text = LanguageManager.Strings.Else;
        if (macroDeckAction != null)
        {
            Action = macroDeckAction;
        }

        typeBox.Items.AddRange(new object[] {
            "Variable",
            "Button_State",
            "Template"
        });

        methodBox.Items.AddRange(new object[] {
            "Equals",
            "Not",
            "Bigger",
            "Smaller"
        });
    }


    private void BtnAddAction_Click(object sender, EventArgs e)
    {
        addItemContextMenu.Show(btnAddAction, new Point(0, 0 + btnAddAction.Height));
            
    }

    private void BtnAddActionElse_Click(object sender, EventArgs e)
    {
        addItemContextMenu.Show(btnAddActionElse, new Point(0, 0 + btnAddActionElse.Height));
    }

    private void AddActionItem(PluginAction action, FlowLayoutPanel container)
    {
        IActionConditionItem actionItem = null;
        if (action.GetType() != typeof(DelayAction))
        {
            actionItem = new ActionItem(action);
        }
        else
        {
            actionItem = new DelayItem(action);
        }
        container.Controls.Add((Control)actionItem);
        actionItem.OnRemoveClick += RemoveClicked;
        actionItem.OnEditClick += EditClicked;
        actionItem.OnMoveUpClick += MoveUpClicked;
        actionItem.OnMoveDownClick += MoveDownClicked;
    }

    private void RefreshActions()
    {
        typeBox.SelectedIndexChanged -= TypeBox_SelectedIndexChanged;
        source.SelectedIndexChanged -= Source_SelectedIndexChanged;
        methodBox.SelectedIndexChanged -= MethodBox_SelectedIndexChanged;
        valueToCompare.TextChanged -= ValueToCompare_TextChanged;

        source.Items.Clear();

        switch (((ConditionAction)Action).ConditionType)
        {
            case ConditionType.Variable:
                source.Visible = true;
                methodBox.Visible = true;
                valueToCompare.Visible = true;
                template.Visible = false;
                btnOpenTemplateEditor.Visible = false;
                foreach (var variable in VariableManager.ListVariables)
                {
                    source.Items.Add(variable.Name);
                }
                break;
            case ConditionType.Button_State:
                source.Visible = false;
                methodBox.Visible = true;
                valueToCompare.Visible = true;
                template.Visible = false;
                btnOpenTemplateEditor.Visible = false;
                break;
            case ConditionType.Template:
                source.Visible = false;
                methodBox.Visible = false;
                valueToCompare.Visible = false;
                template.Visible = true;
                btnOpenTemplateEditor.Visible = true;
                template.Text = ((ConditionAction)Action).ConditionValue1Source;
                break;
        }

        typeBox.Text = ((ConditionAction)Action).ConditionType.ToString();
        source.Text = ((ConditionAction)Action).ConditionValue1Source;
        methodBox.Text = ((ConditionAction)Action).ConditionMethod.ToString();
        if (!valueToCompare.Items.Contains(((ConditionAction)Action).ConditionValue2))
        {
            valueToCompare.Items.Add(((ConditionAction)Action).ConditionValue2);
        }
        valueToCompare.Text = ((ConditionAction)Action).ConditionValue2;

        typeBox.SelectedIndexChanged += TypeBox_SelectedIndexChanged;
        source.SelectedIndexChanged += Source_SelectedIndexChanged;
        methodBox.SelectedIndexChanged += MethodBox_SelectedIndexChanged;
        valueToCompare.TextChanged += ValueToCompare_TextChanged;


        foreach (IActionConditionItem actionItem in actionsList.Controls)
        {
            actionItem.OnRemoveClick -= RemoveClicked;
            actionItem.OnEditClick -= EditClicked;
            actionItem.OnMoveUpClick -= MoveUpClicked;
            actionItem.OnMoveDownClick -= MoveDownClicked;
        }
        foreach (IActionConditionItem actionItem in elseActionsList.Controls)
        {
            actionItem.OnRemoveClick -= RemoveClicked;
            actionItem.OnEditClick -= EditClicked;
            actionItem.OnMoveUpClick -= MoveUpClicked;
            actionItem.OnMoveDownClick -= MoveDownClicked;
        }
        actionsList.Controls.Clear();
        elseActionsList.Controls.Clear();
        foreach (var action in ((ConditionAction)Action).Actions)
        {
            AddActionItem(action, actionsList);
        }
        foreach (var action in ((ConditionAction)Action).ActionsElse)
        {
            AddActionItem(action, elseActionsList);
        }

        Source_SelectedIndexChanged(null, EventArgs.Empty);
    }

    private void MoveUpClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        var action = actionItem.Action;
        if (((ConditionAction)Action).Actions.Contains(action))
        {
            var currentIndex = ((ConditionAction)Action).Actions.IndexOf(action);
            if (currentIndex == 0) return;
            ((ConditionAction)Action).Actions.RemoveAt(currentIndex);
            ((ConditionAction)Action).Actions.Insert(currentIndex - 1, action);
            actionsList.Controls.SetChildIndex((Control)actionItem, currentIndex - 1);
        } else if (((ConditionAction)Action).ActionsElse.Contains(action))
        {
            var currentIndex = ((ConditionAction)Action).ActionsElse.IndexOf(action);
            if (currentIndex == 0) return;
            ((ConditionAction)Action).ActionsElse.RemoveAt(currentIndex);
            ((ConditionAction)Action).ActionsElse.Insert(currentIndex - 1, action);
            elseActionsList.Controls.SetChildIndex((Control)actionItem, currentIndex - 1);
        }

        //this.RefreshActions();
    }

    private void MoveDownClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        var action = actionItem.Action;
        if (((ConditionAction)Action).Actions.Contains(action))
        {
            var currentIndex = ((ConditionAction)Action).Actions.IndexOf(action);
            if (currentIndex + 1 >= ((ConditionAction)Action).Actions.Count) return;
            ((ConditionAction)Action).Actions.RemoveAt(currentIndex);
            ((ConditionAction)Action).Actions.Insert(currentIndex + 1, action);
            actionsList.Controls.SetChildIndex((Control)actionItem, currentIndex + 1);
        }
        else if (((ConditionAction)Action).ActionsElse.Contains(action))
        {
            var currentIndex = ((ConditionAction)Action).ActionsElse.IndexOf(action);
            if (currentIndex + 1 >= ((ConditionAction)Action).Actions.Count) return;
            ((ConditionAction)Action).ActionsElse.RemoveAt(currentIndex);
            ((ConditionAction)Action).ActionsElse.Insert(currentIndex + 1, action);
            elseActionsList.Controls.SetChildIndex((Control)actionItem, currentIndex + 1);
        }
        //this.RefreshActions();
    }


    private void RemoveClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        if (((ConditionAction)Action).Actions.Contains(actionItem.Action))
        {
            ((ConditionAction)Action).Actions.Remove(actionItem.Action);
            actionsList.Controls.Remove((Control)actionItem);
        }
        else if (((ConditionAction)Action).ActionsElse.Contains(actionItem.Action))
        {
            ((ConditionAction)Action).ActionsElse.Remove(actionItem.Action);
            elseActionsList.Controls.Remove((Control)actionItem);
        }
        actionItem.OnRemoveClick -= RemoveClicked;
        ((Control)actionItem).Dispose();

        //RefreshActions();
    }

    private void EditClicked(object sender, EventArgs e)
    {
        var actionItem = sender as IActionConditionItem;
        using var configurator = new ActionConfigurator(actionItem.Action);
        if (configurator.ShowDialog() == DialogResult.OK)
        {
            if (configurator.Action.CanConfigure && configurator.Action.Configuration.Length == 0) return;
            if (((ConditionAction)Action).Actions.Contains(actionItem.Action))
            {
                var index = ((ConditionAction)Action).Actions.IndexOf(actionItem.Action);
                ((ConditionAction)Action).Actions.RemoveAt(index);
                ((ConditionAction)Action).Actions.Insert(index, configurator.Action);
            }
            else if (((ConditionAction)Action).ActionsElse.Contains(actionItem.Action))
            {
                var index = ((ConditionAction)Action).ActionsElse.IndexOf(actionItem.Action);
                ((ConditionAction)Action).ActionsElse.RemoveAt(index);
                ((ConditionAction)Action).ActionsElse.Insert(index, configurator.Action);
            }
                    
            RefreshActions();
        }
    }

    private void ConditionItem_Load(object sender, EventArgs e)
    {
        RefreshActions();
    }

    private void BtnRemove_Click(object sender, EventArgs e)
    {
        OnRemoveClick?.Invoke(this, EventArgs.Empty);
    }

    private void TypeBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        var type = (ConditionType)Enum.Parse(typeof(ConditionType), typeBox.Text);
        ((ConditionAction)Action).ConditionType = type;
        source.Items.Clear();


        switch (type)
        {
            case ConditionType.Variable:
                source.Visible = true;
                methodBox.Visible = true;
                valueToCompare.Visible = true;
                template.Visible = false;
                btnOpenTemplateEditor.Visible = false;
                foreach (var variable in VariableManager.ListVariables)
                {
                    source.Items.Add(variable.Name);
                }

                foreach (var variable in VariableManager.ListVariables)
                {
                    valueToCompare.Items.Add("{" + variable.Name + "}");
                }
                break;
            case ConditionType.Button_State:
                source.Visible = false;
                methodBox.Visible = true;
                valueToCompare.Visible = true;
                template.Visible = false;
                btnOpenTemplateEditor.Visible = false;
                valueToCompare.Items.Clear();
                valueToCompare.SetAutoCompleteMode(AutoCompleteMode.Suggest);
                valueToCompare.SetAutoCompleteSource(AutoCompleteSource.CustomSource);
                valueToCompare.Items.AddRange(stateSuggestions);
                break;
            case ConditionType.Template:
                source.Visible = false;
                methodBox.Visible = false;
                valueToCompare.Visible = false;
                template.Visible = true;
                btnOpenTemplateEditor.Visible = true;
                break;
        }


    }

    private void Template_TextChanged(object sender, EventArgs e)
    {
        if (((ConditionAction)Action).ConditionType == ConditionType.Template && !string.IsNullOrWhiteSpace(template.Text))
        {
            ((ConditionAction)Action).ConditionValue1Source = template.Text;
        }
    }

    private void MethodBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        ((ConditionAction)Action).ConditionMethod = (ConditionMethod)Enum.Parse(typeof(ConditionMethod), methodBox.Text);
        if (((ConditionAction)Action).ConditionType == ConditionType.Variable && string.IsNullOrWhiteSpace(methodBox.Text))
        {
            try
            {
                var variable = VariableManager.ListVariables.FirstOrDefault(v => v.Name == source.Text);
                if (variable != null)
                {
                    valueToCompare.Text = variable.Value;
                }
            }
            catch { }
        }
        Source_SelectedIndexChanged(sender, e);
    }

    private void ValueToCompare_TextChanged(object sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(valueToCompare.Text))
        {
            ((ConditionAction)Action).ConditionValue2 = valueToCompare.Text;
        }
    }

    private void Source_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (((ConditionAction)Action).ConditionType == ConditionType.Template) return;

        ((ConditionAction)Action).ConditionValue1Source = source.Text;

        var variable = VariableManager.ListVariables.FirstOrDefault(v => v.Name == source.Text);

        valueToCompare.Items.Clear();


        if (variable != null)
        {
            if (variable.Suggestions != null && variable.Suggestions.Length > 0)
            {
                valueToCompare.SetAutoCompleteMode(AutoCompleteMode.Suggest);
                valueToCompare.SetAutoCompleteSource(AutoCompleteSource.ListItems);
                valueToCompare.Items.AddRange(variable.Suggestions);
            }
            else if ((variable.Suggestions == null || variable.Suggestions.Length == 0) && variable.Type.Equals(VariableType.Bool))
            {
                valueToCompare.SetAutoCompleteMode(AutoCompleteMode.Suggest);
                valueToCompare.SetAutoCompleteSource(AutoCompleteSource.CustomSource);
                valueToCompare.Items.AddRange(boolSuggestions);
            }
        }

        foreach (var v in VariableManager.ListVariables)
        {
            valueToCompare.Items.Add("{" + v.Name + "}");
        }
    }

    private void BtnUp_Click(object sender, EventArgs e)
    {
        OnMoveUpClick?.Invoke(this, EventArgs.Empty);
    }

    private void BtnDown_Click(object sender, EventArgs e)
    {
        OnMoveDownClick?.Invoke(this, EventArgs.Empty);
    }

    private void MenuItemAction_Click(object sender, EventArgs e)
    {
        using var actionConfigurator = new ActionConfigurator();
        if (actionConfigurator.ShowDialog() == DialogResult.OK)
        {
            if (addItemContextMenu.SourceControl.Equals(btnAddAction))
            {
                ((ConditionAction)Action).Actions.Add(actionConfigurator.Action);
                AddActionItem(actionConfigurator.Action, actionsList);
            } else if (addItemContextMenu.SourceControl.Equals(btnAddActionElse))
            {
                ((ConditionAction)Action).ActionsElse.Add(actionConfigurator.Action);
                AddActionItem(actionConfigurator.Action, elseActionsList);
            }

            //this.RefreshActions();
        }
    }

    private void MenuItemDelay_Click(object sender, EventArgs e)
    {
        var delayAction = new DelayAction();
        if (addItemContextMenu.SourceControl.Equals(btnAddAction))
        {

            ((ConditionAction)Action).Actions.Add(delayAction);
            AddActionItem(delayAction, actionsList);
        }
        else if (addItemContextMenu.SourceControl.Equals(btnAddActionElse))
        {
            ((ConditionAction)Action).ActionsElse.Add(delayAction);
            AddActionItem(delayAction, elseActionsList);
        }

    }

    private void typeBox_Load(object sender, EventArgs e)
    {

    }

    private void BtnOpenTemplateEditor_Click(object sender, EventArgs e)
    {
        using var templateEditor = new TemplateEditor(template.Text);
        if (templateEditor.ShowDialog() == DialogResult.OK)
        {
            template.Text = templateEditor.Template;
            if (!string.IsNullOrWhiteSpace(template.Text))
            {
                ((ConditionAction)Action).ConditionValue1Source = template.Text;
            }
        }
    }
}