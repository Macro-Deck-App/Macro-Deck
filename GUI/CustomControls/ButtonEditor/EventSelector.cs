using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    public partial class EventSelector : RoundedUserControl
    {
        private ActionButton.ActionButton actionButton;
        public EventSelector(ActionButton.ActionButton actionButton)
        {
            this.actionButton = actionButton;
            InitializeComponent();
            btnAddEvent.Text = "+ " + LanguageManager.Strings.Event;
        }

        public List<EventItem> EventItems()
        {
            var eventItems = new List<EventItem>();
            foreach (EventItem eventItem in eventsList.Controls)
            {
                eventItems.Add(eventItem);
            }
            return eventItems;
        }

        private void BtnAddEvent_Click(object sender, EventArgs e)
        {
            var eventItem = new EventItem(actionButton);
            AddEventItem(eventItem);
        }

        private void AddEventItem(EventItem eventItem)
        {
            eventsList.Controls.Add(eventItem);
            eventItem.OnRemoveClick += RemoveClicked;
        }


        private void RemoveClicked(object sender, EventArgs e)
        {
            var eventItem = sender as EventItem;
            actionButton.EventListeners?.Remove(eventItem.EventListener);
            eventItem.OnRemoveClick -= RemoveClicked;
            eventsList.Controls.Remove(eventItem);
        }

        public void RefreshEventsList()
        {
            foreach (Control control in eventsList.Controls)
            {
                ((EventItem)control).OnRemoveClick -= RemoveClicked;
            }
            eventsList.Controls.Clear();
            if (actionButton.EventListeners == null) return;
            foreach (var eventListener in actionButton.EventListeners)
            {
                var eventItem = new EventItem(actionButton, eventListener);
                AddEventItem(eventItem);
            }
        }

        
    }
}
