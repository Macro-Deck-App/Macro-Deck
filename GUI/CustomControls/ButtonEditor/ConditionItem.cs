using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class ConditionItem : UserControl, IActionConditionItem
    {
        public IMacroDeckAction Action { get; set; } = new ConditionAction();

        public event EventHandler OnRemoveClick;
        public event EventHandler OnEditClick;
        public event EventHandler OnMoveUpClick;
        public event EventHandler OnMoveDownClick;

        public ConditionItem(IMacroDeckAction macroDeckAction = null)
        {
            InitializeComponent();
            if (macroDeckAction != null)
            {
                this.Action = macroDeckAction;
            }
        }


        private void BtnAddAction_Click(object sender, EventArgs e)
        {
            using (var actionConfigurator = new ActionConfigurator())
            {
                if (actionConfigurator.ShowDialog() == DialogResult.OK)
                {
                    ((ConditionAction) this.Action).Actions.Add(actionConfigurator.Action);
                    this.RefreshActions();
                }
            }
        }

        private void BtnAddActionElse_Click(object sender, EventArgs e)
        {
            using (var actionConfigurator = new ActionConfigurator())
            {
                if (actionConfigurator.ShowDialog() == DialogResult.OK)
                {
                    ((ConditionAction)this.Action).ActionsElse.Add(actionConfigurator.Action);
                    this.RefreshActions();
                }
            }
        }

        private void RefreshActions()
        {
            this.typeBox.SelectedIndexChanged -= typeBox_SelectedIndexChanged;
            this.source.SelectedIndexChanged -= source_SelectedIndexChanged;
            this.methodBox.SelectedIndexChanged -= methodBox_SelectedIndexChanged;
            this.valueToCompare.TextChanged -= valueToCompare_TextChanged;

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
            this.valueToCompare.Text = ((ConditionAction)this.Action).ConditionValue2.ToString();

            this.typeBox.SelectedIndexChanged += typeBox_SelectedIndexChanged;
            this.source.SelectedIndexChanged += source_SelectedIndexChanged;
            this.methodBox.SelectedIndexChanged += methodBox_SelectedIndexChanged;
            this.valueToCompare.TextChanged += valueToCompare_TextChanged;


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
            foreach (IMacroDeckAction action in ((ConditionAction)this.Action).Actions)
            {
                ActionItem actionItem = new ActionItem(action);
                this.actionsList.Controls.Add(actionItem);
                actionItem.OnRemoveClick += this.RemoveClicked;
                actionItem.OnEditClick += this.EditClicked;
                actionItem.OnMoveUpClick += this.MoveUpClicked;
                actionItem.OnMoveDownClick += this.MoveDownClicked;
            }
            foreach (IMacroDeckAction action in ((ConditionAction)this.Action).ActionsElse)
            {
                ActionItem actionItem = new ActionItem(action);
                this.elseActionsList.Controls.Add(actionItem);
                actionItem.OnRemoveClick += this.RemoveClicked;
                actionItem.OnEditClick += this.EditClicked;
                actionItem.OnMoveUpClick += this.MoveUpClicked;
                actionItem.OnMoveDownClick += this.MoveDownClicked;
            }
        }

        private void MoveUpClicked(object sender, EventArgs e)
        {
            IMacroDeckAction action = ((IActionConditionItem)sender).Action;
            if (((ConditionAction)this.Action).Actions.Contains(action))
            {
                int currentIndex = ((ConditionAction)this.Action).Actions.IndexOf(action);
                if (currentIndex == 0) return;
                ((ConditionAction)this.Action).Actions.RemoveAt(currentIndex);
                ((ConditionAction)this.Action).Actions.Insert(currentIndex - 1, action);
            } else if (((ConditionAction)this.Action).ActionsElse.Contains(action))
            {
                int currentIndex = ((ConditionAction)this.Action).ActionsElse.IndexOf(action);
                if (currentIndex == 0) return;
                ((ConditionAction)this.Action).ActionsElse.RemoveAt(currentIndex);
                ((ConditionAction)this.Action).ActionsElse.Insert(currentIndex - 1, action);
            }
            
            this.RefreshActions();
        }

        private void MoveDownClicked(object sender, EventArgs e)
        {
            IMacroDeckAction action = ((IActionConditionItem)sender).Action;
            if (((ConditionAction)this.Action).Actions.Contains(action))
            {
                int currentIndex = ((ConditionAction)this.Action).Actions.IndexOf(action);
                if (currentIndex >= ((ConditionAction)this.Action).Actions.Count - 1) return;
                ((ConditionAction)this.Action).Actions.RemoveAt(currentIndex);
                ((ConditionAction)this.Action).Actions.Insert(currentIndex + 1, action);
            }
            else if (((ConditionAction)this.Action).ActionsElse.Contains(action))
            {
                int currentIndex = ((ConditionAction)this.Action).ActionsElse.IndexOf(action);
                if (currentIndex >= ((ConditionAction)this.Action).Actions.Count - 1) return;
                ((ConditionAction)this.Action).ActionsElse.RemoveAt(currentIndex);
                ((ConditionAction)this.Action).ActionsElse.Insert(currentIndex + 1, action);
            }
            this.RefreshActions();
        }


        private void RemoveClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            if (((ConditionAction)this.Action).Actions.Contains(actionItem.Action))
            {
                ((ConditionAction)this.Action).Actions.Remove(actionItem.Action);
            }
            else if (((ConditionAction)this.Action).ActionsElse.Contains(actionItem.Action))
            {
                ((ConditionAction)this.Action).ActionsElse.Remove(actionItem.Action);
            }
            actionItem.OnRemoveClick -= this.RemoveClicked;

            RefreshActions();
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

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (this.OnRemoveClick != null)
            {
                this.OnRemoveClick(this, EventArgs.Empty);
            }
        }

        private void typeBox_SelectedIndexChanged(object sender, EventArgs e)
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
            }
            
        }

        private void methodBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ((ConditionAction)this.Action).ConditionMethod = (ConditionMethod)Enum.Parse(typeof(ConditionMethod), methodBox.Text);
            if (((ConditionAction)this.Action).ConditionType == ConditionType.Variable)
            {
                try
                {
                    Variables.Variable variable = Variables.VariableManager.Variables.Find(v => v.Name.Equals(this.source.Text));
                    this.valueToCompare.Text = variable.Value;
                }
                catch { }
            }
            else if (((ConditionAction)this.Action).ConditionType == ConditionType.Variable)
            {
                this.valueToCompare.Text = "on";
            }
        }

        private void valueToCompare_TextChanged(object sender, EventArgs e)
        {
            ((ConditionAction)this.Action).ConditionValue2 = valueToCompare.Text;
        }

        private void source_SelectedIndexChanged(object sender, EventArgs e)
        {
            ((ConditionAction)this.Action).ConditionValue1Source = source.Text;
            methodBox.Text = "Equals";
        }

        private void btnUp_Click(object sender, EventArgs e)
        {
            this.panelEdit.Visible = false;
            if (this.OnMoveUpClick != null)
            {
                this.OnMoveUpClick(this, EventArgs.Empty);
            }
        }

        private void btnDown_Click(object sender, EventArgs e)
        {
            this.panelEdit.Visible = false;
            if (this.OnMoveDownClick != null)
            {
                this.OnMoveDownClick(this, EventArgs.Empty);
            }
        }

        private void elseActionsList_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
