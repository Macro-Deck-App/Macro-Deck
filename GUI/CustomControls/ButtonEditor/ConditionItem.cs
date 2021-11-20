using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.ActionButton.Plugin;
using SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class ConditionItem : UserControl, IActionConditionItem
    {
        public PluginAction Action { get; set; } = new ConditionAction();

        public event EventHandler OnRemoveClick;
        public event EventHandler OnEditClick;
        public event EventHandler OnMoveUpClick;
        public event EventHandler OnMoveDownClick;

        string[] boolSuggestions = new string[]
        {
            "True",
            "False"
        };

        string[] stateSuggestions = new string[]
        {
            "On",
            "Off"
        };

        public ConditionItem(PluginAction macroDeckAction = null)
        {
            InitializeComponent();
            this.menuItemAction.Text = Language.LanguageManager.Strings.Action;
            this.menuItemDelay.Text = Language.LanguageManager.Strings.Delay;
            this.lblIf.Text = Language.LanguageManager.Strings.If;
            this.lblElse.Text = Language.LanguageManager.Strings.Else;
            if (macroDeckAction != null)
            {
                this.Action = macroDeckAction;
            }

            this.typeBox.Items.AddRange(new object[] {
            "Variable",
            "Button_State"});

            this.methodBox.Items.AddRange(new object[] {
            "Equals",
            "Not",
            "Bigger",
            "Smaller"});
        }


        private void BtnAddAction_Click(object sender, EventArgs e)
        {
            this.addItemContextMenu.Show(this.btnAddAction, new Point(0, 0 + this.btnAddAction.Height));
            
        }

        private void BtnAddActionElse_Click(object sender, EventArgs e)
        {
            this.addItemContextMenu.Show(this.btnAddActionElse, new Point(0, 0 + this.btnAddActionElse.Height));
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
            actionItem.OnRemoveClick += this.RemoveClicked;
            actionItem.OnEditClick += this.EditClicked;
            actionItem.OnMoveUpClick += this.MoveUpClicked;
            actionItem.OnMoveDownClick += this.MoveDownClicked;
        }

        private void RefreshActions()
        {
            this.typeBox.SelectedIndexChanged -= TypeBox_SelectedIndexChanged;
            this.source.SelectedIndexChanged -= Source_SelectedIndexChanged;
            this.methodBox.SelectedIndexChanged -= MethodBox_SelectedIndexChanged;
            this.valueToCompare.TextChanged -= ValueToCompare_TextChanged;

            this.source.Items.Clear();
            if (((ConditionAction)this.Action).ConditionType == ConditionType.Variable)
            {
                this.source.Visible = true;
                foreach (Variables.Variable variable in Variables.VariableManager.Variables)
                {
                    this.source.Items.Add(variable.Name);
                }
            }
            else if (((ConditionAction)this.Action).ConditionType == ConditionType.Button_State)
            {
                this.source.Visible = false;
            }

            this.typeBox.Text = ((ConditionAction)this.Action).ConditionType.ToString();
            this.source.Text = ((ConditionAction)this.Action).ConditionValue1Source.ToString();
            this.methodBox.Text = ((ConditionAction)this.Action).ConditionMethod.ToString();
            if (!this.valueToCompare.Items.Contains(((ConditionAction)this.Action).ConditionValue2.ToString()))
            {
                this.valueToCompare.Items.Add(((ConditionAction)this.Action).ConditionValue2.ToString());
            }
            this.valueToCompare.Text = ((ConditionAction)this.Action).ConditionValue2.ToString();

            this.typeBox.SelectedIndexChanged += TypeBox_SelectedIndexChanged;
            this.source.SelectedIndexChanged += Source_SelectedIndexChanged;
            this.methodBox.SelectedIndexChanged += MethodBox_SelectedIndexChanged;
            this.valueToCompare.TextChanged += ValueToCompare_TextChanged;


            foreach (IActionConditionItem actionItem in this.actionsList.Controls)
            {
                actionItem.OnRemoveClick -= this.RemoveClicked;
                actionItem.OnEditClick -= this.EditClicked;
                actionItem.OnMoveUpClick -= this.MoveUpClicked;
                actionItem.OnMoveDownClick -= this.MoveDownClicked;
            }
            foreach (IActionConditionItem actionItem in this.elseActionsList.Controls)
            {
                actionItem.OnRemoveClick -= this.RemoveClicked;
                actionItem.OnEditClick -= this.EditClicked;
                actionItem.OnMoveUpClick -= this.MoveUpClicked;
                actionItem.OnMoveDownClick -= this.MoveDownClicked;
            }
            this.actionsList.Controls.Clear();
            this.elseActionsList.Controls.Clear();
            foreach (PluginAction action in ((ConditionAction)this.Action).Actions)
            {
                this.AddActionItem(action, this.actionsList);
            }
            foreach (PluginAction action in ((ConditionAction)this.Action).ActionsElse)
            {
                this.AddActionItem(action, this.elseActionsList);
            }

            this.Source_SelectedIndexChanged(null, EventArgs.Empty);
        }

        private void MoveUpClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            PluginAction action = actionItem.Action;
            if (((ConditionAction)this.Action).Actions.Contains(action))
            {
                int currentIndex = ((ConditionAction)this.Action).Actions.IndexOf(action);
                if (currentIndex == 0) return;
                ((ConditionAction)this.Action).Actions.RemoveAt(currentIndex);
                ((ConditionAction)this.Action).Actions.Insert(currentIndex - 1, action);
                this.actionsList.Controls.SetChildIndex((Control)actionItem, currentIndex - 1);
            } else if (((ConditionAction)this.Action).ActionsElse.Contains(action))
            {
                int currentIndex = ((ConditionAction)this.Action).ActionsElse.IndexOf(action);
                if (currentIndex == 0) return;
                ((ConditionAction)this.Action).ActionsElse.RemoveAt(currentIndex);
                ((ConditionAction)this.Action).ActionsElse.Insert(currentIndex - 1, action);
                this.elseActionsList.Controls.SetChildIndex((Control)actionItem, currentIndex - 1);
            }

            //this.RefreshActions();
        }

        private void MoveDownClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            PluginAction action = actionItem.Action;
            if (((ConditionAction)this.Action).Actions.Contains(action))
            {
                int currentIndex = ((ConditionAction)this.Action).Actions.IndexOf(action);
                if (currentIndex + 1 >= ((ConditionAction)this.Action).Actions.Count) return;
                ((ConditionAction)this.Action).Actions.RemoveAt(currentIndex);
                ((ConditionAction)this.Action).Actions.Insert(currentIndex + 1, action);
                this.actionsList.Controls.SetChildIndex((Control)actionItem, currentIndex + 1);
            }
            else if (((ConditionAction)this.Action).ActionsElse.Contains(action))
            {
                int currentIndex = ((ConditionAction)this.Action).ActionsElse.IndexOf(action);
                if (currentIndex + 1 >= ((ConditionAction)this.Action).Actions.Count) return;
                ((ConditionAction)this.Action).ActionsElse.RemoveAt(currentIndex);
                ((ConditionAction)this.Action).ActionsElse.Insert(currentIndex + 1, action);
                this.elseActionsList.Controls.SetChildIndex((Control)actionItem, currentIndex + 1);
            }
            //this.RefreshActions();
        }


        private void RemoveClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            if (((ConditionAction)this.Action).Actions.Contains(actionItem.Action))
            {
                ((ConditionAction)this.Action).Actions.Remove(actionItem.Action);
                this.actionsList.Controls.Remove((Control)actionItem);
            }
            else if (((ConditionAction)this.Action).ActionsElse.Contains(actionItem.Action))
            {
                ((ConditionAction)this.Action).ActionsElse.Remove(actionItem.Action);
                this.elseActionsList.Controls.Remove((Control)actionItem);
            }
            actionItem.OnRemoveClick -= this.RemoveClicked;
            ((Control)actionItem).Dispose();

            //RefreshActions();
        }

        private void EditClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            using (var configurator = new ActionConfigurator(actionItem.Action))
            {
                if (configurator.ShowDialog() == DialogResult.OK)
                {
                    if (configurator.Action.CanConfigure && configurator.Action.Configuration.Length == 0) return;
                    if (((ConditionAction)this.Action).Actions.Contains(actionItem.Action))
                    {
                        int index = ((ConditionAction)this.Action).Actions.IndexOf(actionItem.Action);
                        ((ConditionAction)this.Action).Actions.RemoveAt(index);
                        ((ConditionAction)this.Action).Actions.Insert(index, configurator.Action);
                    }
                    else if (((ConditionAction)this.Action).ActionsElse.Contains(actionItem.Action))
                    {
                        int index = ((ConditionAction)this.Action).ActionsElse.IndexOf(actionItem.Action);
                        ((ConditionAction)this.Action).ActionsElse.RemoveAt(index);
                        ((ConditionAction)this.Action).ActionsElse.Insert(index, configurator.Action);
                    }
                    
                    this.RefreshActions();
                }
            }
        }

        private void ConditionItem_Load(object sender, EventArgs e)
        {
            this.RefreshActions();
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            if (this.OnRemoveClick != null)
            {
                this.OnRemoveClick(this, EventArgs.Empty);
            }
        }

        private void TypeBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ConditionType type = (ConditionType)Enum.Parse(typeof(ConditionType), typeBox.Text);
            ((ConditionAction)this.Action).ConditionType = type;
            this.source.Items.Clear();
            if (type == ConditionType.Variable)
            {
                this.source.Visible = true;
                foreach (Variables.Variable variable in Variables.VariableManager.Variables)
                {
                    this.source.Items.Add(variable.Name);
                }
            } else if (type == ConditionType.Button_State)
            {
                this.source.Visible = false;
                this.valueToCompare.Items.Clear();
                this.valueToCompare.SetAutoCompleteMode(AutoCompleteMode.Suggest);
                this.valueToCompare.SetAutoCompleteSource(AutoCompleteSource.CustomSource);
                this.valueToCompare.Items.AddRange(this.stateSuggestions);
            }
        }

        private void MethodBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ((ConditionAction)this.Action).ConditionMethod = (ConditionMethod)Enum.Parse(typeof(ConditionMethod), methodBox.Text);
            if (((ConditionAction)this.Action).ConditionType == ConditionType.Variable)
            {
                try
                {
                    Variables.Variable variable = Variables.VariableManager.Variables.Find(v => v.Name.Equals(this.source.Text));
                    if (variable != null)
                    {
                        this.valueToCompare.Text = variable.Value;
                    }
                }
                catch { }
            }
            this.Source_SelectedIndexChanged(sender, e);
        }

        private void ValueToCompare_TextChanged(object sender, EventArgs e)
        {
            if (!String.IsNullOrWhiteSpace(valueToCompare.Text))
            {
                ((ConditionAction)this.Action).ConditionValue2 = valueToCompare.Text;
            }
        }

        private void Source_SelectedIndexChanged(object sender, EventArgs e)
        {
            ((ConditionAction)this.Action).ConditionValue1Source = source.Text;
            methodBox.Text = "Equals";

            Variables.Variable variable = Variables.VariableManager.Variables.Find(v => v.Name.Equals(this.source.Text));

            this.valueToCompare.Items.Clear();


            if (variable != null)
            {
                if (variable.Suggestions != null && variable.Suggestions.Length > 0)
                {
                    this.valueToCompare.SetAutoCompleteMode(AutoCompleteMode.Suggest);
                    this.valueToCompare.SetAutoCompleteSource(AutoCompleteSource.ListItems);
                    this.valueToCompare.Items.AddRange(variable.Suggestions);
                }
                else if ((variable.Suggestions == null || variable.Suggestions.Length == 0) && variable.Type.Equals(Variables.VariableType.Bool))
                {
                    this.valueToCompare.SetAutoCompleteMode(AutoCompleteMode.Suggest);
                    this.valueToCompare.SetAutoCompleteSource(AutoCompleteSource.CustomSource);
                    this.valueToCompare.Items.AddRange(this.boolSuggestions);
                }
            }
        }

        private void BtnUp_Click(object sender, EventArgs e)
        {
            if (this.OnMoveUpClick != null)
            {
                this.OnMoveUpClick(this, EventArgs.Empty);
            }
        }

        private void BtnDown_Click(object sender, EventArgs e)
        {
            if (this.OnMoveDownClick != null)
            {
                this.OnMoveDownClick(this, EventArgs.Empty);
            }
        }

        private void MenuItemAction_Click(object sender, EventArgs e)
        {

            using (var actionConfigurator = new ActionConfigurator())
            {
                if (actionConfigurator.ShowDialog() == DialogResult.OK)
                {
                    if (this.addItemContextMenu.SourceControl.Equals(this.btnAddAction))
                    {
                        ((ConditionAction)this.Action).Actions.Add(actionConfigurator.Action);
                        this.AddActionItem(actionConfigurator.Action, this.actionsList);
                    } else if (this.addItemContextMenu.SourceControl.Equals(this.btnAddActionElse))
                    {
                        ((ConditionAction)this.Action).ActionsElse.Add(actionConfigurator.Action);
                        this.AddActionItem(actionConfigurator.Action, this.elseActionsList);
                    }

                    //this.RefreshActions();
                }
            }
        }

        private void MenuItemDelay_Click(object sender, EventArgs e)
        {
            DelayAction delayAction = new DelayAction();
            if (this.addItemContextMenu.SourceControl.Equals(this.btnAddAction))
            {

                ((ConditionAction)this.Action).Actions.Add(delayAction);
                this.AddActionItem(delayAction, this.actionsList);
            }
            else if (this.addItemContextMenu.SourceControl.Equals(this.btnAddActionElse))
            {
                ((ConditionAction)this.Action).ActionsElse.Add(delayAction);
                this.AddActionItem(delayAction, this.elseActionsList);
            }

        }

        private void typeBox_Load(object sender, EventArgs e)
        {

        }
    }
}
