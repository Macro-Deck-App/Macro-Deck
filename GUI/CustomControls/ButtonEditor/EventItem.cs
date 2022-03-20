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

        //public IMacroDeckEvent MacroDeckEvent { get { return _macroDeckEvent; } }
        //public List<PluginAction> Actions { get { return _macroDeckActions; } }

        public EventListener EventListener;

        private ActionButton.ActionButton _actionButton;
        //private IMacroDeckEvent _macroDeckEvent;
        //private List<PluginAction> _macroDeckActions = new List<PluginAction>();

        public EventItem(ActionButton.ActionButton actionButton, EventListener eventListener = null)
        {
            this._actionButton = actionButton;
            this.EventListener = eventListener;
            if (this.EventListener == null)
            {
                this.EventListener = new EventListener();
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
            foreach (IMacroDeckEvent macroDeckEvent in EventManager.RegisteredEvents)
            {
                //if (this._actionButton.EventActions.ContainsKey(macroDeckEvent.Name)) continue;
                this.eventBox.Items.Add(macroDeckEvent.Name);
            }
            this.eventBox.SelectedIndexChanged += this.EventBox_SelectedIndexChanged;
            this.eventParameter.SelectedIndexChanged += EventParameter_SelectedIndexChanged;
            if (this.EventListener != null && !String.IsNullOrWhiteSpace(this.EventListener.EventToListen))
            {
                if (!this.eventBox.Items.Contains(this.EventListener.EventToListen))
                {
                    this.eventBox.Items.Add(this.EventListener.EventToListen);
                }
                this.eventBox.Text = this.EventListener.EventToListen;
                this.eventParameter.Text = this.EventListener.Parameter;
            }
        }

        private void EventParameter_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.EventListener.Parameter = this.eventParameter.Text;
        }

        private void EventBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            IMacroDeckEvent macroDeckEvent = EventManager.GetEventByName(this.eventBox.Text);
            if (macroDeckEvent == null) return;
            this.EventListener.EventToListen = macroDeckEvent.Name.ToString();
            this.eventParameter.Items.AddRange(macroDeckEvent.ParameterSuggestions.ToArray());
            if (this.EventChanged != null)
            {
                this.EventChanged(this.EventListener.EventToListen, EventArgs.Empty);
            }
        }

        private void AddActionItem(PluginAction action)
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
       

        private void RefreshActions()
        {
            this.SuspendLayout();
            foreach (IActionConditionItem actionItem in this.actionsList.Controls)
            {
                actionItem.OnRemoveClick -= this.RemoveClicked;
                actionItem.OnEditClick -= this.EditClicked;
                actionItem.OnMoveUpClick -= this.MoveUpClicked;
                actionItem.OnMoveDownClick -= this.MoveDownClicked;
            }
            this.actionsList.Controls.Clear();
            foreach (PluginAction action in this.EventListener.Actions)
            {
                this.AddActionItem(action);
            }
            this.ResumeLayout();
        }

        private void MoveUpClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            PluginAction action = actionItem.Action;
            if (this.EventListener.Actions.Contains(action))
            {
                int currentIndex = this.EventListener.Actions.IndexOf(action);
                if (currentIndex == 0) return;
                this.EventListener.Actions.RemoveAt(currentIndex);
                this.EventListener.Actions.Insert(currentIndex - 1, action);
                this.actionsList.Controls.SetChildIndex((Control)actionItem, currentIndex - 1);
            }
        }

        private void MoveDownClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            PluginAction action = ((IActionConditionItem)sender).Action;
            if (this.EventListener.Actions.Contains(action))
            {
                int currentIndex = this.EventListener.Actions.IndexOf(action);
                if (currentIndex + 1 >= this.EventListener.Actions.Count) return;
                this.EventListener.Actions.RemoveAt(currentIndex);
                this.EventListener.Actions.Insert(currentIndex + 1, action);
                this.actionsList.Controls.SetChildIndex((Control)actionItem, currentIndex + 1);
            }
        }


        private void RemoveClicked(object sender, EventArgs e)
        {
            this.SuspendLayout();
            IActionConditionItem actionItem = sender as IActionConditionItem;
            if (this.EventListener.Actions.Contains(actionItem.Action))
            {
                this.EventListener.Actions.Remove(actionItem.Action);
                this.actionsList.Controls.Remove((Control)actionItem);
            }
            actionItem.OnRemoveClick -= this.RemoveClicked;
            ((Control)actionItem).Dispose();
            this.ResumeLayout();
        }

        private void EditClicked(object sender, EventArgs e)
        {
            IActionConditionItem actionItem = sender as IActionConditionItem;
            using (var configurator = new ActionConfigurator(actionItem.Action))
            {
                if (configurator.ShowDialog() == DialogResult.OK)
                {
                    if (configurator.Action.CanConfigure && configurator.Action.Configuration.Length == 0) return;
                    if (this.EventListener.Actions.Contains(actionItem.Action))
                    {
                        int index = this.EventListener.Actions.IndexOf(actionItem.Action);
                        this.EventListener.Actions.RemoveAt(index);
                        this.EventListener.Actions.Insert(index, configurator.Action);
                    }

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
                    this.EventListener.Actions.Add(actionConfigurator.Action);
                    this.AddActionItem(actionConfigurator.Action);
                }
            }
        }

        private void MenuItemCondition_Click(object sender, EventArgs e)
        {
            ConditionItem conditionItem = new ConditionItem();
            this.EventListener.Actions.Add(conditionItem.Action);
            this.AddActionItem(conditionItem.Action);
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
            this.EventListener.Actions.Add(delayItem.Action);
            this.AddActionItem(delayItem.Action);
        }
    }
}
