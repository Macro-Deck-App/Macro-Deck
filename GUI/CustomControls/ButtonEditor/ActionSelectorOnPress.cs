using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.ActionButton.Plugin;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    public partial class ActionSelectorOnPress : UserControl
    {

        private ActionButton.ActionButton actionButton;

        public ActionSelectorOnPress(ActionButton.ActionButton actionButton)
        {
            this.actionButton = actionButton;
            InitializeComponent();
            this.menuItemAction.Text = Language.LanguageManager.Strings.Action;
            this.menuItemCondition.Text = Language.LanguageManager.Strings.Condition;
            this.menuItemDelay.Text = Language.LanguageManager.Strings.Delay;
        }

        public void RefreshActions()
        {
            foreach (IActionConditionItem actionItem in this.actionsOnPress.Controls)
            {
                actionItem.OnRemoveClick -= this.RemoveClicked;
                actionItem.OnEditClick -= this.EditClicked;
                actionItem.OnMoveUpClick -= this.MoveUpClicked;
                actionItem.OnMoveDownClick -= this.MoveDownClicked;

            }
            this.actionsOnPress.Controls.Clear();
            foreach (PluginAction action in actionButton.Actions)
            {
                if (action.GetType() == typeof(ConditionAction))
                {
                    ConditionItem conditionItem = new ConditionItem(action);
                    this.actionsOnPress.Controls.Add(conditionItem);
                    conditionItem.OnRemoveClick += this.RemoveClicked;
                } 
                else if (action.GetType() == typeof(DelayAction))
                {
                    DelayItem delayItem = new DelayItem(action);
                    this.actionsOnPress.Controls.Add(delayItem);
                    delayItem.OnRemoveClick += this.RemoveClicked;
                    delayItem.OnMoveUpClick += this.MoveUpClicked;
                    delayItem.OnMoveDownClick += this.MoveDownClicked;
                }
                else
                {
                    ActionItem actionItem = new ActionItem(action);
                    this.actionsOnPress.Controls.Add(actionItem);
                    actionItem.OnRemoveClick += this.RemoveClicked;
                    actionItem.OnEditClick += this.EditClicked;
                    actionItem.OnMoveUpClick += this.MoveUpClicked;
                    actionItem.OnMoveDownClick += this.MoveDownClicked;
                }
            }
        }

        private void MoveUpClicked(object sender, EventArgs e)
        {
            PluginAction action = ((IActionConditionItem)sender).Action;
            int currentIndex = this.actionButton.Actions.IndexOf(action);
            if (currentIndex == 0) return;
            this.actionButton.Actions.RemoveAt(currentIndex);
            this.actionButton.Actions.Insert(currentIndex - 1, action);
            this.RefreshActions();
        }

        private void MoveDownClicked(object sender, EventArgs e)
        {
            PluginAction action = ((IActionConditionItem)sender).Action;
            int currentIndex = this.actionButton.Actions.IndexOf(action);
            if (currentIndex >= this.actionButton.Actions.Count) return;
            this.actionButton.Actions.RemoveAt(currentIndex);
            this.actionButton.Actions.Insert(currentIndex + 1, action);
            this.RefreshActions();
        }

        private void RemoveClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            this.actionButton.Actions.Remove(actionItem.Action);
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
                    int index = this.actionButton.Actions.IndexOf(actionItem.Action);
                    this.actionButton.Actions.RemoveAt(index);
                    this.actionButton.Actions.Insert(index, configurator.Action);
                    this.RefreshActions();
                }
            }
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            this.addItemContextMenu.Show(this.btnAdd, new Point(0, 0 + this.btnAdd.Height));
        }

        private void MenuItemAction_Click(object sender, EventArgs e)
        {
            using (var actionConfigurator = new ActionConfigurator())
            {
                if (actionConfigurator.ShowDialog() == DialogResult.OK)
                {
                    this.actionButton.Actions.Add(actionConfigurator.Action);
                    this.RefreshActions();
                }
            }
        }

        private void MenuItemCondition_Click(object sender, EventArgs e)
        {
            ConditionItem conditionItem = new ConditionItem();
            this.actionButton.Actions.Add(conditionItem.Action);
            this.RefreshActions();
        }

        private void ActionSelectorOnPress_Load(object sender, EventArgs e)
        {

        }

        private void DelayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DelayItem delayItem = new DelayItem();
            this.actionButton.Actions.Add(delayItem.Action);
            this.RefreshActions();
        }
    }
}
