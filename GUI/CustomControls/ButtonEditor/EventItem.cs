using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.ActionButton.Plugin;
using SuchByte.MacroDeck.Events;
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
    public partial class EventItem : UserControl
    {

        public event EventHandler EventChanged;
        public event EventHandler OnRemoveClick;

        public IMacroDeckEvent MacroDeckEvent { get { return _macroDeckEvent; } }
        public List<PluginAction> Actions { get { return _macroDeckActions; } }

        private ActionButton.ActionButton _actionButton;
        private IMacroDeckEvent _macroDeckEvent;
        private List<PluginAction> _macroDeckActions = new List<PluginAction>();

        public EventItem(ActionButton.ActionButton actionButton, IMacroDeckEvent macroDeckEvent = null, List<PluginAction> actions = null)
        {
            this._actionButton = actionButton;
            this._macroDeckEvent = macroDeckEvent;
            this._macroDeckActions = actions;

            if (this._macroDeckActions == null)
            {
                this._macroDeckActions = new List<PluginAction>();
            }
            InitializeComponent();
            this.menuItemAction.Text = Language.LanguageManager.Strings.Action;
            this.menuItemCondition.Text = Language.LanguageManager.Strings.Condition;
            this.menuItemDelay.Text = Language.LanguageManager.Strings.Delay;
            this.lblTrigger.Text = Language.LanguageManager.Strings.Trigger;
            
        }

        private void EventItem_Load(object sender, EventArgs e)
        {
            this.LoadEvents();
            this.RefreshActions();
        }


        private void LoadEvents()
        {
            this.eventBox.SelectedIndexChanged -= this.EventBox_SelectedIndexChanged;
            this.eventBox.Items.Clear();
            foreach (IMacroDeckEvent macroDeckEvent in MacroDeck.EventManager.RegisteredEvents)
            {
                if (this._actionButton.EventActions.ContainsKey(macroDeckEvent.Name)) continue;
                this.eventBox.Items.Add(macroDeckEvent.Name);
            }
            if (this._macroDeckEvent != null)
            {
                if (!this.eventBox.Items.Contains(this._macroDeckEvent.Name))
                {
                    this.eventBox.Items.Add(this._macroDeckEvent.Name);
                }
                this.eventBox.Text = this._macroDeckEvent.Name;
            }
            this.eventBox.SelectedIndexChanged += this.EventBox_SelectedIndexChanged;
        }


        private void EventBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            this._macroDeckEvent = MacroDeck.EventManager.GetEventByName(this.eventBox.Text);
            if (this._macroDeckEvent != null)
            {
                if (this.EventChanged != null)
                {
                    this.EventChanged(this._macroDeckEvent, EventArgs.Empty);
                }
            }
        }

       

        private void RefreshActions()
        {
            foreach (IActionConditionItem actionItem in this.actionsList.Controls)
            {
                actionItem.OnRemoveClick -= this.RemoveClicked;
                actionItem.OnEditClick -= this.EditClicked;
                actionItem.OnMoveUpClick -= this.MoveUpClicked;
                actionItem.OnMoveDownClick -= this.MoveDownClicked;
            }
            this.actionsList.Controls.Clear();
            foreach (PluginAction action in this._macroDeckActions)
            {
                if (action.GetType() == typeof(ConditionAction))
                {
                    ConditionItem conditionItem = new ConditionItem(action);
                    this.actionsList.Controls.Add(conditionItem);
                    conditionItem.OnRemoveClick += this.RemoveClicked;
                } 
                else if (action.GetType() == typeof(DelayAction))
                {
                    DelayItem delayItem = new DelayItem(action);
                    this.actionsList.Controls.Add(delayItem);
                    delayItem.OnRemoveClick += this.RemoveClicked;
                    delayItem.OnMoveUpClick += this.MoveUpClicked;
                    delayItem.OnMoveDownClick += this.MoveDownClicked;
                }
                else
                {
                    ActionItem actionItem = new ActionItem(action);
                    this.actionsList.Controls.Add(actionItem);
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
            if (this._macroDeckActions.Contains(action))
            {
                int currentIndex = this._macroDeckActions.IndexOf(action);
                if (currentIndex == 0) return;
                this._macroDeckActions.RemoveAt(currentIndex);
                this._macroDeckActions.Insert(currentIndex - 1, action);
            }

            this.RefreshActions();
        }

        private void MoveDownClicked(object sender, EventArgs e)
        {
            PluginAction action = ((IActionConditionItem)sender).Action;
            if (this._macroDeckActions.Contains(action))
            {
                int currentIndex = this._macroDeckActions.IndexOf(action);
                if (currentIndex >= this._macroDeckActions.Count - 1) return;
                this._macroDeckActions.RemoveAt(currentIndex);
                this._macroDeckActions.Insert(currentIndex + 1, action);
            }
            this.RefreshActions();
        }


        private void RemoveClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            if (this._macroDeckActions.Contains(actionItem.Action))
            {
                this._macroDeckActions.Remove(actionItem.Action);
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
                    if (this._macroDeckActions.Contains(actionItem.Action))
                    {
                        int index = this._macroDeckActions.IndexOf(actionItem.Action);
                        this._macroDeckActions.RemoveAt(index);
                        this._macroDeckActions.Insert(index, configurator.Action);
                    }

                    this.RefreshActions();
                }
            }
        }




        private void BtnAdd_Click(object sender, EventArgs e)
        {
            this.addItemContextMenu.Show(this.btnAdd, new Point(0, 0 + this.btnAdd.Height));
        }

        private void menuItemAction_Click(object sender, EventArgs e)
        {
            using (var actionConfigurator = new ActionConfigurator())
            {
                if (actionConfigurator.ShowDialog() == DialogResult.OK)
                {
                    this._macroDeckActions.Add(actionConfigurator.Action);
                    this.RefreshActions();
                }
            }
        }

        private void MenuItemCondition_Click(object sender, EventArgs e)
        {
            ConditionItem conditionItem = new ConditionItem();
            this._macroDeckActions.Add(conditionItem.Action);
            this.RefreshActions();
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            if (this.OnRemoveClick != null)
            {
                this.OnRemoveClick(this, EventArgs.Empty);
            }
        }

        private void MenuItemDelay_Click(object sender, EventArgs e)
        {
            DelayItem delayItem = new DelayItem();
            this._macroDeckActions.Add(delayItem.Action);
            this.RefreshActions();
        }
    }
}
