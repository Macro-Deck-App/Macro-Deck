using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    public partial class EventSelector : RoundedUserControl
    {
        private ActionButton.ActionButton actionButton;
        public EventSelector(ActionButton.ActionButton actionButton)
        {
            this.actionButton = actionButton;
            InitializeComponent();
            this.btnAddEvent.Text = "+ " + Language.LanguageManager.Strings.Event;
        }

        public List<EventItem> EventItems()
        {
            List<EventItem> eventItems = new List<EventItem>();
            foreach (EventItem eventItem in this.eventsList.Controls)
            {
                eventItems.Add(eventItem);
            }
            return eventItems;
        }

        private void BtnAddEvent_Click(object sender, EventArgs e)
        {
            EventItem eventItem = new EventItem(this.actionButton);
            AddEventItem(eventItem);
        }

        private void AddEventItem(EventItem eventItem)
        {
            this.eventsList.Controls.Add(eventItem);
            eventItem.OnRemoveClick += RemoveClicked;
        }


        private void RemoveClicked(object sender, EventArgs e)
        {
            EventItem eventItem = sender as EventItem;
            if (this.actionButton.EventListeners != null)
            {
                this.actionButton.EventListeners.Remove(eventItem.EventListener);
            }
            eventItem.OnRemoveClick -= this.RemoveClicked;
            this.eventsList.Controls.Remove(eventItem);
        }

        public void RefreshEventsList()
        {
            foreach (Control control in this.eventsList.Controls)
            {
                ((EventItem)control).OnRemoveClick -= this.RemoveClicked;
            }
            this.eventsList.Controls.Clear();
            if (this.actionButton.EventListeners == null) return;
            foreach (var eventListener in this.actionButton.EventListeners)
            {
                EventItem eventItem = new EventItem(this.actionButton, eventListener);
                AddEventItem(eventItem);
            }
        }

        
    }
}
