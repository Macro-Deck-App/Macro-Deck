using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.CustomControls.Notifications;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Services;

namespace SuchByte.MacroDeck.GUI
{
    partial class MainWindow
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            try
            {
                if (this.notificationsList != null && this.Controls.Contains(this.notificationsList))
                {
                    this.Controls.Remove(this.notificationsList);
                }
                LanguageManager.LanguageChanged -= LanguageChanged;
                UpdateService.Instance().UpdateAvailable -= UpdateAvailable;
                MacroDeckServer.OnDeviceConnectionStateChanged -= this.OnClientsConnectedChanged;
                MacroDeckServer.OnServerStateChanged -= this.OnServerStateChanged;
                PluginManager.OnPluginsChange -= this.OnPluginsChanged;
                IconManager.OnUpdateCheckFinished -= OnPackageManagerUpdateCheckFinished;
                NotificationManager.OnNotification -= NotificationsChanged;
                NotificationManager.OnNotificationRemoved -= NotificationsChanged;
                DeckView?.Dispose();
            }
            catch { }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainWindow));
            this.lblVersion = new System.Windows.Forms.Label();
            this.contentPanel = new SuchByte.MacroDeck.GUI.CustomControls.BufferedPanel();
            this.contentButtonPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.btnDeck = new SuchByte.MacroDeck.GUI.CustomControls.ContentSelectorButton();
            this.panel1 = new System.Windows.Forms.Panel();
            this.btnExtensions = new SuchByte.MacroDeck.GUI.CustomControls.ContentSelectorButton();
            this.btnDeviceManager = new SuchByte.MacroDeck.GUI.CustomControls.ContentSelectorButton();
            this.btnVariables = new SuchByte.MacroDeck.GUI.CustomControls.ContentSelectorButton();
            this.panel2 = new System.Windows.Forms.Panel();
            this.btnSettings = new SuchByte.MacroDeck.GUI.CustomControls.ContentSelectorButton();
            this.lblNumClientsConnected = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.lblPort = new System.Windows.Forms.Label();
            this.lblServerStatus = new System.Windows.Forms.Label();
            this.lblIpAddressHostname = new System.Windows.Forms.Label();
            this.navigation = new SuchByte.MacroDeck.GUI.CustomControls.RoundedPanel();
            this.btnNotifications = new SuchByte.MacroDeck.GUI.CustomControls.Notifications.NotificationButton();
            this.hosts = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.contentButtonPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeck)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnExtensions)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeviceManager)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnVariables)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnSettings)).BeginInit();
            this.navigation.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblSafeMode
            // 
            this.lblSafeMode.Margin = new System.Windows.Forms.Padding(7, 0, 7, 0);
            this.lblSafeMode.Size = new System.Drawing.Size(222, 28);
            // 
            // lblVersion
            // 
            this.lblVersion.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblVersion.ForeColor = System.Drawing.Color.White;
            this.lblVersion.Location = new System.Drawing.Point(4, 613);
            this.lblVersion.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(219, 20);
            this.lblVersion.TabIndex = 3;
            this.lblVersion.Text = "2.0.0";
            this.lblVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblVersion.UseMnemonic = false;
            // 
            // contentPanel
            // 
            this.contentPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.contentPanel.Location = new System.Drawing.Point(62, 90);
            this.contentPanel.Margin = new System.Windows.Forms.Padding(6, 3, 6, 3);
            this.contentPanel.Name = "contentPanel";
            this.contentPanel.Size = new System.Drawing.Size(1134, 522);
            this.contentPanel.TabIndex = 4;
            // 
            // contentButtonPanel
            // 
            this.contentButtonPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.contentButtonPanel.Controls.Add(this.btnDeck);
            this.contentButtonPanel.Controls.Add(this.panel1);
            this.contentButtonPanel.Controls.Add(this.btnExtensions);
            this.contentButtonPanel.Controls.Add(this.btnDeviceManager);
            this.contentButtonPanel.Controls.Add(this.btnVariables);
            this.contentButtonPanel.Controls.Add(this.panel2);
            this.contentButtonPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.contentButtonPanel.Location = new System.Drawing.Point(5, 6);
            this.contentButtonPanel.Margin = new System.Windows.Forms.Padding(0);
            this.contentButtonPanel.Name = "contentButtonPanel";
            this.contentButtonPanel.Size = new System.Drawing.Size(44, 502);
            this.contentButtonPanel.TabIndex = 5;
            // 
            // btnDeck
            // 
            this.btnDeck.BackColor = System.Drawing.Color.Transparent;
            this.btnDeck.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.deck;
            this.btnDeck.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnDeck.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeck.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDeck.ForeColor = System.Drawing.Color.White;
            this.btnDeck.Location = new System.Drawing.Point(0, 0);
            this.btnDeck.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.btnDeck.Name = "btnDeck";
            this.btnDeck.Selected = false;
            this.btnDeck.Size = new System.Drawing.Size(44, 44);
            this.btnDeck.TabIndex = 0;
            this.btnDeck.TabStop = false;
            this.btnDeck.Click += new System.EventHandler(this.BtnDeck_Click);
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.Silver;
            this.panel1.Location = new System.Drawing.Point(0, 51);
            this.panel1.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(44, 2);
            this.panel1.TabIndex = 4;
            // 
            // btnExtensions
            // 
            this.btnExtensions.BackColor = System.Drawing.Color.Transparent;
            this.btnExtensions.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Package_Manager_icon;
            this.btnExtensions.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnExtensions.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnExtensions.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnExtensions.ForeColor = System.Drawing.Color.White;
            this.btnExtensions.Location = new System.Drawing.Point(0, 60);
            this.btnExtensions.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.btnExtensions.Name = "btnExtensions";
            this.btnExtensions.Selected = false;
            this.btnExtensions.Size = new System.Drawing.Size(44, 44);
            this.btnExtensions.TabIndex = 1;
            this.btnExtensions.TabStop = false;
            this.btnExtensions.Click += new System.EventHandler(this.BtnExtensions_Click);
            // 
            // btnDeviceManager
            // 
            this.btnDeviceManager.BackColor = System.Drawing.Color.Transparent;
            this.btnDeviceManager.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.device_manager;
            this.btnDeviceManager.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnDeviceManager.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeviceManager.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDeviceManager.ForeColor = System.Drawing.Color.White;
            this.btnDeviceManager.Location = new System.Drawing.Point(0, 112);
            this.btnDeviceManager.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.btnDeviceManager.Name = "btnDeviceManager";
            this.btnDeviceManager.Selected = false;
            this.btnDeviceManager.Size = new System.Drawing.Size(44, 44);
            this.btnDeviceManager.TabIndex = 2;
            this.btnDeviceManager.TabStop = false;
            this.btnDeviceManager.Click += new System.EventHandler(this.BtnDeviceManager_Click);
            // 
            // btnVariables
            // 
            this.btnVariables.BackColor = System.Drawing.Color.Transparent;
            this.btnVariables.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.variables;
            this.btnVariables.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnVariables.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnVariables.Font = new System.Drawing.Font("Tahoma", 12.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnVariables.ForeColor = System.Drawing.Color.White;
            this.btnVariables.Location = new System.Drawing.Point(0, 164);
            this.btnVariables.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.btnVariables.Name = "btnVariables";
            this.btnVariables.Selected = false;
            this.btnVariables.Size = new System.Drawing.Size(44, 44);
            this.btnVariables.TabIndex = 3;
            this.btnVariables.TabStop = false;
            this.btnVariables.Text = "{x}";
            this.btnVariables.Click += new System.EventHandler(this.BtnVariables_Click);
            // 
            // panel2
            // 
            this.panel2.BackColor = System.Drawing.Color.Silver;
            this.panel2.Location = new System.Drawing.Point(0, 215);
            this.panel2.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(44, 2);
            this.panel2.TabIndex = 5;
            // 
            // btnSettings
            // 
            this.btnSettings.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnSettings.BackColor = System.Drawing.Color.Transparent;
            this.btnSettings.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.settings;
            this.btnSettings.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnSettings.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnSettings.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnSettings.ForeColor = System.Drawing.Color.White;
            this.btnSettings.Location = new System.Drawing.Point(5, 512);
            this.btnSettings.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
            this.btnSettings.Name = "btnSettings";
            this.btnSettings.Selected = false;
            this.btnSettings.Size = new System.Drawing.Size(44, 44);
            this.btnSettings.TabIndex = 1;
            this.btnSettings.TabStop = false;
            this.btnSettings.Click += new System.EventHandler(this.BtnSettings_Click);
            // 
            // lblNumClientsConnected
            // 
            this.lblNumClientsConnected.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblNumClientsConnected.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblNumClientsConnected.ForeColor = System.Drawing.Color.White;
            this.lblNumClientsConnected.Location = new System.Drawing.Point(844, 64);
            this.lblNumClientsConnected.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblNumClientsConnected.Name = "lblNumClientsConnected";
            this.lblNumClientsConnected.Size = new System.Drawing.Size(252, 18);
            this.lblNumClientsConnected.TabIndex = 8;
            this.lblNumClientsConnected.Text = "0 clients connected";
            this.lblNumClientsConnected.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblNumClientsConnected.UseMnemonic = false;
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.Location = new System.Drawing.Point(1096, 41);
            this.label1.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(15, 19);
            this.label1.TabIndex = 10;
            this.label1.Text = ":";
            this.label1.UseMnemonic = false;
            // 
            // lblPort
            // 
            this.lblPort.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblPort.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.lblPort.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPort.ForeColor = System.Drawing.Color.White;
            this.lblPort.Location = new System.Drawing.Point(1113, 40);
            this.lblPort.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(80, 21);
            this.lblPort.TabIndex = 11;
            this.lblPort.Text = "8191";
            this.lblPort.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblPort.UseMnemonic = false;
            // 
            // lblServerStatus
            // 
            this.lblServerStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblServerStatus.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblServerStatus.ForeColor = System.Drawing.Color.White;
            this.lblServerStatus.Location = new System.Drawing.Point(614, 64);
            this.lblServerStatus.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblServerStatus.Name = "lblServerStatus";
            this.lblServerStatus.Size = new System.Drawing.Size(218, 18);
            this.lblServerStatus.TabIndex = 12;
            this.lblServerStatus.Text = "Server offline";
            this.lblServerStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblServerStatus.UseMnemonic = false;
            // 
            // lblIpAddressHostname
            // 
            this.lblIpAddressHostname.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblIpAddressHostname.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIpAddressHostname.ForeColor = System.Drawing.Color.White;
            this.lblIpAddressHostname.Location = new System.Drawing.Point(520, 43);
            this.lblIpAddressHostname.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblIpAddressHostname.Name = "lblIpAddressHostname";
            this.lblIpAddressHostname.Size = new System.Drawing.Size(312, 18);
            this.lblIpAddressHostname.TabIndex = 13;
            this.lblIpAddressHostname.Text = "IP address/hostname : Port";
            this.lblIpAddressHostname.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblIpAddressHostname.UseMnemonic = false;
            // 
            // navigation
            // 
            this.navigation.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.navigation.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.navigation.Controls.Add(this.contentButtonPanel);
            this.navigation.Controls.Add(this.btnSettings);
            this.navigation.Location = new System.Drawing.Point(0, 41);
            this.navigation.Margin = new System.Windows.Forms.Padding(0);
            this.navigation.Name = "navigation";
            this.navigation.Size = new System.Drawing.Size(54, 563);
            this.navigation.TabIndex = 15;
            this.navigation.Visible = false;
            // 
            // btnNotifications
            // 
            this.btnNotifications.BorderRadius = 8;
            this.btnNotifications.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnNotifications.FlatAppearance.BorderSize = 0;
            this.btnNotifications.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNotifications.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnNotifications.ForeColor = System.Drawing.Color.White;
            this.btnNotifications.HoverColor = System.Drawing.Color.Empty;
            this.btnNotifications.Icon = global::SuchByte.MacroDeck.Properties.Resources.Bell;
            this.btnNotifications.Location = new System.Drawing.Point(62, 41);
            this.btnNotifications.Name = "btnNotifications";
            this.btnNotifications.NotificationCount = 0;
            this.btnNotifications.Padding = new System.Windows.Forms.Padding(0, 3, 3, 0);
            this.btnNotifications.Progress = 0;
            this.btnNotifications.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(205)))));
            this.btnNotifications.Size = new System.Drawing.Size(41, 41);
            this.btnNotifications.TabIndex = 16;
            this.btnNotifications.UseVisualStyleBackColor = true;
            this.btnNotifications.UseWindowsAccentColor = false;
            this.btnNotifications.Visible = false;
            this.btnNotifications.Click += new System.EventHandler(this.BtnNotifications_Click);
            // 
            // hosts
            // 
            this.hosts.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.hosts.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.hosts.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.hosts.Icon = null;
            this.hosts.Location = new System.Drawing.Point(841, 38);
            this.hosts.Name = "hosts";
            this.hosts.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.hosts.SelectedIndex = -1;
            this.hosts.SelectedItem = null;
            this.hosts.Size = new System.Drawing.Size(250, 26);
            this.hosts.TabIndex = 17;
            this.hosts.SelectedIndexChanged += new System.EventHandler(this.Hosts_SelectedIndexChanged);
            // 
            // MainWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(1200, 635);
            this.Controls.Add(this.hosts);
            this.Controls.Add(this.btnNotifications);
            this.Controls.Add(this.navigation);
            this.Controls.Add(this.lblIpAddressHostname);
            this.Controls.Add(this.lblServerStatus);
            this.Controls.Add(this.lblPort);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lblNumClientsConnected);
            this.Controls.Add(this.contentPanel);
            this.Controls.Add(this.lblVersion);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Location = new System.Drawing.Point(0, 0);
            this.Margin = new System.Windows.Forms.Padding(7, 3, 7, 3);
            this.MinimumSize = new System.Drawing.Size(1200, 635);
            this.Name = "MainWindow";
            this.Text = "Macro Deck 2";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.OnFormClosing);
            this.Load += new System.EventHandler(this.MainWindow_Load);
            this.Controls.SetChildIndex(this.lblVersion, 0);
            this.Controls.SetChildIndex(this.contentPanel, 0);
            this.Controls.SetChildIndex(this.lblNumClientsConnected, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.lblPort, 0);
            this.Controls.SetChildIndex(this.lblServerStatus, 0);
            this.Controls.SetChildIndex(this.lblIpAddressHostname, 0);
            this.Controls.SetChildIndex(this.navigation, 0);
            this.Controls.SetChildIndex(this.btnNotifications, 0);
            this.Controls.SetChildIndex(this.hosts, 0);
            this.contentButtonPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnDeck)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnExtensions)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeviceManager)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnVariables)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnSettings)).EndInit();
            this.navigation.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private Label lblVersion;
        private BufferedPanel contentPanel;
        private ContentSelectorButton btnDeck;
        private ContentSelectorButton btnSettings;
        public FlowLayoutPanel contentButtonPanel;
        private ContentSelectorButton btnExtensions;
        private ContentSelectorButton btnDeviceManager;
        private Label lblNumClientsConnected;
        private ContentSelectorButton btnVariables;
        private Label label1;
        private Label lblPort;
        private Label lblServerStatus;
        private Label lblIpAddressHostname;
        private Panel panel1;
        private Panel panel2;
        private RoundedPanel navigation;
        private NotificationButton btnNotifications;
        private RoundedComboBox hosts;
    }
}