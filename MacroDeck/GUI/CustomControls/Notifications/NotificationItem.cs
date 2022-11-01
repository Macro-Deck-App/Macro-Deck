using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Notifications;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class NotificationItem : RoundedUserControl
{
    public string Id { get; private set; }

    private NotificationModel _notificationModel;

    public void ClearAdditionalControls()
    {
        MacroDeckLogger.Trace("Clear");
        if (InvokeRequired)
        {
            Invoke(() => ClearAdditionalControls());
            return;
        }
        foreach (Control control in additionalControls.Controls)
        {
            control.Parent = null;
        }
    }

    public NotificationItem(NotificationModel notificationModel)
    {
        _notificationModel = notificationModel;
        Id = notificationModel.Id;
        InitializeComponent();
        lblPluginName.Text = notificationModel.SenderName;
        lblTitle.Text = notificationModel.Title;
        lblDateTime.Text = DateTimeOffset.FromUnixTimeSeconds(notificationModel.Timestamp).LocalDateTime.ToString();
        lblMessage.Text = notificationModel.Message;
        pluginIcon.BackgroundImage = notificationModel.Icon;

        if (notificationModel.AdditionalControls != null)
        {
            foreach (var control in notificationModel.AdditionalControls)
            {
                additionalControls.Controls.Add(control);
            }
        }
    }

    private void NotificationItem_Load(object sender, EventArgs e)
    {
            
    }

    private void BtnRemove_Click(object sender, EventArgs e)
    {
        ClearAdditionalControls();
        NotificationManager.RemoveNotification(_notificationModel);
    }
}