using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Utils;
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
    public partial class NotificationItem : RoundedUserControl
    {
        public string Id { get; private set; }

        private NotificationModel _notificationModel;


        public NotificationItem(NotificationModel notificationModel)
        {
            this._notificationModel = notificationModel;
            this.Id = notificationModel.Id;
            InitializeComponent();
            this.lblPluginName.Text = notificationModel.SenderName;
            this.lblTitle.Text = notificationModel.Title;
            this.lblDateTime.Text = DateTimeOffset.FromUnixTimeSeconds(notificationModel.Timestamp).LocalDateTime.ToString();
            this.lblMessage.Text = notificationModel.Message;
            MacroDeckPlugin plugin = PluginManager.Plugins.Values.Where(x => x.Name == notificationModel.SenderName).FirstOrDefault();
            if (plugin == null)
            {
                this.pluginIcon.BackgroundImage = Properties.Resources.Macro_Deck_2021;
            } else
            {
                this.pluginIcon.BackgroundImage = plugin.PluginIcon;
            }
            if (notificationModel.AdditionalControls != null)
            {
                foreach (var control in notificationModel.AdditionalControls)
                {
                    this.additionalControls.Controls.Add(control);
                }
            }
        }

        private void NotificationItem_Load(object sender, EventArgs e)
        {
            
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            NotificationManager.RemoveNotification(this._notificationModel);
        }
    }
}
