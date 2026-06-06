using SuchByte.MacroDeck.Notifications;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class NotificationsList : RoundedUserControl
{
    public event EventHandler OnCloseRequested;

    public NotificationsList()
    {
        InitializeComponent();
        notificationList.HorizontalScroll.Visible = false;
        notificationList.VerticalScroll.Visible = false;

        NotificationManager.OnNotification += NotificationAdded;
        NotificationManager.OnNotificationRemoved += NotificationRemoved;

        foreach (var notification in NotificationManager.Notifications)
        {
            var notificationItem = new NotificationItem(notification);
            notificationList.Controls.Add(notificationItem);
        }
    }


    private void NotificationAdded(object sender, NotificationEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        var notificationItem = new NotificationItem(e.Notification);
        Invoke(() => notificationList.Controls.Add(notificationItem));
    }

    private void NotificationRemoved(object sender, NotificationRemovedEventArgs e)
    {
        var control = notificationList.Controls.OfType<NotificationItem>().Where(x => x.Id == e.Id).FirstOrDefault();
        if (control != null)
        {
            control.ClearAdditionalControls();
            notificationList.Controls.Remove(control);
        }
        if (notificationList.Controls.Count == 0)
        {
            OnCloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}