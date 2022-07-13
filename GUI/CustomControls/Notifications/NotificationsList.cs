using Newtonsoft.Json;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Notifications;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class NotificationsList : RoundedUserControl
    {
        public event EventHandler OnCloseRequested;

        public NotificationsList()
        {
            InitializeComponent();
            this.notificationList.HorizontalScroll.Visible = false;
            this.notificationList.VerticalScroll.Visible = false;

            NotificationManager.OnNotification += NotificationAdded;
            NotificationManager.OnNotificationRemoved += NotificationRemoved;

            foreach (var notification in NotificationManager.Notifications)
            {
                var notificationItem = new NotificationItem(notification);
                this.notificationList.Controls.Add(notificationItem);
            }
        }


        private void NotificationAdded(object sender, NotificationEventArgs e)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            var notificationItem = new NotificationItem(e.Notification);
            this.Invoke(new Action(() => this.notificationList.Controls.Add(notificationItem)));
        }

        private void NotificationRemoved(object sender, NotificationRemovedEventArgs e)
        {
            var control = this.notificationList.Controls.OfType<NotificationItem>().Where(x => x.Id == e.Id).FirstOrDefault();
            if (control != null)
            {
                control.ClearAdditionalControls();
                this.notificationList.Controls.Remove(control);
            }
            if (this.notificationList.Controls.Count == 0)
            {
                if (this.OnCloseRequested != null)
                {
                    this.OnCloseRequested(this, EventArgs.Empty);
                }
            }
        }
    }
}
