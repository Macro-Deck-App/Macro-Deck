using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.Notifications
{
    public class NotificationEventArgs : EventArgs
    {
        public NotificationModel Notification { get; set; }
    }

    public class NotificationRemovedEventArgs : EventArgs
    {
        public string Id { get; set; }
    }


    public class NotificationManager
    {
        public static EventHandler<NotificationEventArgs> OnNotification;

        public static EventHandler<NotificationRemovedEventArgs> OnNotificationRemoved;

        private static List<NotificationModel> _notifications = new List<NotificationModel>();

        internal static List<NotificationModel> Notifications
        {
            get => _notifications;
        }

        /// <summary>
        /// Returns the notification
        /// </summary>
        public static NotificationModel GetNotification(string id)
        {
            return _notifications.Find(x => x.Id == id);
        }

        /// <summary>
        /// Returns the id of the notification
        /// </summary>
        public static string Notify(MacroDeckPlugin macroDeckPlugin, string title, string message, bool showBalloonTip = false, List<Control> controls = null)
        {
            var notificationModel = new NotificationModel()
            {
                SenderName = macroDeckPlugin.Name,
                Title = $"{macroDeckPlugin.Name}: {title}",
                Message = message,
                AdditionalControls = controls
            };

            Notify(notificationModel, showBalloonTip);

            return notificationModel.Id;
        }
        
        /// <summary>
        /// Removes a notification
        /// </summary>
        public static void RemoveNotification(NotificationModel notificationModel)
        {
            if (notificationModel == null || !_notifications.Contains(notificationModel)) return;
            _notifications.Remove(notificationModel);

            if (OnNotificationRemoved != null)
            {
                OnNotificationRemoved(null, new NotificationRemovedEventArgs() { Id = notificationModel.Id });
            }
        }

        internal static string SystemNotification(string title, string message, bool showBalloonTip = false, List<Control> controls = null, Bitmap image = null)
        {
            var notificationModel = new NotificationModel()
            {
                SenderName = "Macro Deck",
                Title = $"{title}",
                Message = message,
                AdditionalControls = controls,
                CustomImage = image
            };

            Notify(notificationModel, showBalloonTip);

            return notificationModel.Id;
        }

        private static void Notify(NotificationModel notificationModel, bool showBalloonTip)
        {
            if (_notifications.Find(x => x.Id == notificationModel.Id) != null) return;
            if (_notifications.FindAll(x => x.SenderName == notificationModel.SenderName).Count >= 5) return;

            _notifications.Add(notificationModel);

            if (OnNotification != null)
            {
                OnNotification(null, new NotificationEventArgs() { Notification = notificationModel });
            }

            if (showBalloonTip)
            {
                MacroDeck.ShowBalloonTip($"{notificationModel.SenderName}: {notificationModel.Title}", notificationModel.Message);
            }
        }

    }
}
