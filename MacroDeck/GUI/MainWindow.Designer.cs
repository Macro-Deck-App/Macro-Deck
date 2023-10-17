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
                if (this._notificationsList != null && this.Controls.Contains(this._notificationsList))
                {
                    this.Controls.Remove(this._notificationsList);
                }
                LanguageManager.LanguageChanged -= LanguageChanged;
                UpdateService.Instance().UpdateAvailable -= UpdateAvailable;
                MacroDeckServer.OnDeviceConnectionStateChanged -= this.OnClientsConnectedChanged;
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
            ComponentResourceManager resources = new ComponentResourceManager(typeof(MainWindow));
            lblVersion = new Label();
            contentPanel = new BufferedPanel();
            contentButtonPanel = new FlowLayoutPanel();
            btnDeck = new ContentSelectorButton();
            panel1 = new Panel();
            btnExtensions = new ContentSelectorButton();
            btnDeviceManager = new ContentSelectorButton();
            btnVariables = new ContentSelectorButton();
            panel2 = new Panel();
            btnSettings = new ContentSelectorButton();
            lblNumClientsConnected = new Label();
            label1 = new Label();
            lblPort = new Label();
            lblIpAddressHostname = new Label();
            navigation = new RoundedPanel();
            btnNotifications = new NotificationButton();
            hosts = new RoundedComboBox();
            contentButtonPanel.SuspendLayout();
            ((ISupportInitialize)btnDeck).BeginInit();
            ((ISupportInitialize)btnExtensions).BeginInit();
            ((ISupportInitialize)btnDeviceManager).BeginInit();
            ((ISupportInitialize)btnVariables).BeginInit();
            ((ISupportInitialize)btnSettings).BeginInit();
            navigation.SuspendLayout();
            SuspendLayout();
            // 
            // lblSafeMode
            // 
            lblSafeMode.Margin = new Padding(10, 0, 10, 0);
            lblSafeMode.Size = new Size(333, 42);
            // 
            // lblVersion
            // 
            lblVersion.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblVersion.ForeColor = Color.White;
            lblVersion.Location = new Point(2, 603);
            lblVersion.Margin = new Padding(9, 0, 9, 0);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(328, 30);
            lblVersion.TabIndex = 3;
            lblVersion.Text = "2.0.0";
            lblVersion.TextAlign = ContentAlignment.MiddleLeft;
            lblVersion.UseMnemonic = false;
            // 
            // contentPanel
            // 
            contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            contentPanel.Location = new Point(69, 94);
            contentPanel.Margin = new Padding(9, 4, 9, 4);
            contentPanel.Name = "contentPanel";
            contentPanel.Size = new Size(1131, 509);
            contentPanel.TabIndex = 4;
            // 
            // contentButtonPanel
            // 
            contentButtonPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            contentButtonPanel.Controls.Add(btnDeck);
            contentButtonPanel.Controls.Add(panel1);
            contentButtonPanel.Controls.Add(btnExtensions);
            contentButtonPanel.Controls.Add(btnDeviceManager);
            contentButtonPanel.Controls.Add(btnVariables);
            contentButtonPanel.Controls.Add(panel2);
            contentButtonPanel.FlowDirection = FlowDirection.TopDown;
            contentButtonPanel.Location = new Point(8, 9);
            contentButtonPanel.Margin = new Padding(0);
            contentButtonPanel.Name = "contentButtonPanel";
            contentButtonPanel.Size = new Size(45, 470);
            contentButtonPanel.TabIndex = 5;
            // 
            // btnDeck
            // 
            btnDeck.BackColor = Color.Transparent;
            btnDeck.BackgroundImage = Properties.Resources.deck;
            btnDeck.BackgroundImageLayout = ImageLayout.Stretch;
            btnDeck.Cursor = Cursors.Hand;
            btnDeck.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnDeck.ForeColor = Color.White;
            btnDeck.Location = new Point(0, 0);
            btnDeck.Margin = new Padding(0, 0, 0, 6);
            btnDeck.Name = "btnDeck";
            btnDeck.Selected = false;
            btnDeck.Size = new Size(44, 44);
            btnDeck.TabIndex = 0;
            btnDeck.TabStop = false;
            btnDeck.Click += BtnDeck_Click;
            // 
            // panel1
            // 
            panel1.BackColor = Color.Silver;
            panel1.Location = new Point(0, 54);
            panel1.Margin = new Padding(0, 4, 0, 4);
            panel1.Name = "panel1";
            panel1.Size = new Size(66, 3);
            panel1.TabIndex = 4;
            // 
            // btnExtensions
            // 
            btnExtensions.BackColor = Color.Transparent;
            btnExtensions.BackgroundImage = Properties.Resources.Package_Manager_icon;
            btnExtensions.BackgroundImageLayout = ImageLayout.Stretch;
            btnExtensions.Cursor = Cursors.Hand;
            btnExtensions.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnExtensions.ForeColor = Color.White;
            btnExtensions.Location = new Point(0, 67);
            btnExtensions.Margin = new Padding(0, 6, 0, 6);
            btnExtensions.Name = "btnExtensions";
            btnExtensions.Selected = false;
            btnExtensions.Size = new Size(44, 44);
            btnExtensions.TabIndex = 1;
            btnExtensions.TabStop = false;
            btnExtensions.Click += BtnExtensions_Click;
            // 
            // btnDeviceManager
            // 
            btnDeviceManager.BackColor = Color.Transparent;
            btnDeviceManager.BackgroundImage = Properties.Resources.device_manager;
            btnDeviceManager.BackgroundImageLayout = ImageLayout.Stretch;
            btnDeviceManager.Cursor = Cursors.Hand;
            btnDeviceManager.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnDeviceManager.ForeColor = Color.White;
            btnDeviceManager.Location = new Point(0, 123);
            btnDeviceManager.Margin = new Padding(0, 6, 0, 6);
            btnDeviceManager.Name = "btnDeviceManager";
            btnDeviceManager.Selected = false;
            btnDeviceManager.Size = new Size(44, 44);
            btnDeviceManager.TabIndex = 2;
            btnDeviceManager.TabStop = false;
            btnDeviceManager.Click += BtnDeviceManager_Click;
            // 
            // btnVariables
            // 
            btnVariables.BackColor = Color.Transparent;
            btnVariables.BackgroundImage = Properties.Resources.variables;
            btnVariables.BackgroundImageLayout = ImageLayout.Stretch;
            btnVariables.Cursor = Cursors.Hand;
            btnVariables.Font = new Font("Tahoma", 12.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnVariables.ForeColor = Color.White;
            btnVariables.Location = new Point(0, 179);
            btnVariables.Margin = new Padding(0, 6, 0, 6);
            btnVariables.Name = "btnVariables";
            btnVariables.Selected = false;
            btnVariables.Size = new Size(44, 44);
            btnVariables.TabIndex = 3;
            btnVariables.TabStop = false;
            btnVariables.Text = "{x}";
            btnVariables.Click += BtnVariables_Click;
            // 
            // panel2
            // 
            panel2.BackColor = Color.Silver;
            panel2.Location = new Point(0, 233);
            panel2.Margin = new Padding(0, 4, 0, 4);
            panel2.Name = "panel2";
            panel2.Size = new Size(66, 3);
            panel2.TabIndex = 5;
            // 
            // btnSettings
            // 
            btnSettings.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnSettings.BackColor = Color.Transparent;
            btnSettings.BackgroundImage = Properties.Resources.settings;
            btnSettings.BackgroundImageLayout = ImageLayout.Stretch;
            btnSettings.Cursor = Cursors.Hand;
            btnSettings.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnSettings.ForeColor = Color.White;
            btnSettings.Location = new Point(8, 511);
            btnSettings.Margin = new Padding(12, 6, 12, 6);
            btnSettings.Name = "btnSettings";
            btnSettings.Selected = false;
            btnSettings.Size = new Size(44, 44);
            btnSettings.TabIndex = 1;
            btnSettings.TabStop = false;
            btnSettings.Click += BtnSettings_Click;
            // 
            // lblNumClientsConnected
            // 
            lblNumClientsConnected.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            lblNumClientsConnected.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblNumClientsConnected.ForeColor = Color.White;
            lblNumClientsConnected.Location = new Point(936, 606);
            lblNumClientsConnected.Margin = new Padding(9, 0, 9, 0);
            lblNumClientsConnected.Name = "lblNumClientsConnected";
            lblNumClientsConnected.Size = new Size(264, 27);
            lblNumClientsConnected.TabIndex = 8;
            lblNumClientsConnected.Text = "0 clients connected";
            lblNumClientsConnected.TextAlign = ContentAlignment.MiddleRight;
            lblNumClientsConnected.UseMnemonic = false;
            // 
            // label1
            // 
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            label1.AutoSize = true;
            label1.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(1094, 44);
            label1.Margin = new Padding(9, 0, 9, 0);
            label1.Name = "label1";
            label1.Size = new Size(15, 19);
            label1.TabIndex = 10;
            label1.Text = ":";
            label1.UseMnemonic = false;
            // 
            // lblPort
            // 
            lblPort.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblPort.BackColor = Color.FromArgb(45, 45, 45);
            lblPort.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            lblPort.ForeColor = Color.White;
            lblPort.Location = new Point(1127, 37);
            lblPort.Margin = new Padding(9, 0, 9, 0);
            lblPort.Name = "lblPort";
            lblPort.Size = new Size(69, 32);
            lblPort.TabIndex = 11;
            lblPort.Text = "8191";
            lblPort.TextAlign = ContentAlignment.MiddleLeft;
            lblPort.UseMnemonic = false;
            // 
            // lblIpAddressHostname
            // 
            lblIpAddressHostname.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblIpAddressHostname.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblIpAddressHostname.ForeColor = Color.White;
            lblIpAddressHostname.Location = new Point(225, 42);
            lblIpAddressHostname.Margin = new Padding(9, 0, 9, 0);
            lblIpAddressHostname.Name = "lblIpAddressHostname";
            lblIpAddressHostname.Size = new Size(468, 27);
            lblIpAddressHostname.TabIndex = 13;
            lblIpAddressHostname.Text = "IP address/hostname : Port";
            lblIpAddressHostname.TextAlign = ContentAlignment.MiddleRight;
            lblIpAddressHostname.UseMnemonic = false;
            // 
            // navigation
            // 
            navigation.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            navigation.BackColor = Color.FromArgb(32, 32, 32);
            navigation.Controls.Add(contentButtonPanel);
            navigation.Controls.Add(btnSettings);
            navigation.Location = new Point(0, 42);
            navigation.Margin = new Padding(0);
            navigation.Name = "navigation";
            navigation.Size = new Size(60, 561);
            navigation.TabIndex = 15;
            // 
            // btnNotifications
            // 
            btnNotifications.BorderRadius = 8;
            btnNotifications.Cursor = Cursors.Hand;
            btnNotifications.FlatAppearance.BorderSize = 0;
            btnNotifications.FlatStyle = FlatStyle.Flat;
            btnNotifications.Font = new Font("Tahoma", 8F, FontStyle.Regular, GraphicsUnit.Point);
            btnNotifications.ForeColor = Color.White;
            btnNotifications.HoverColor = Color.Empty;
            btnNotifications.Icon = Properties.Resources.Bell;
            btnNotifications.Location = new Point(69, 42);
            btnNotifications.Margin = new Padding(4);
            btnNotifications.Name = "btnNotifications";
            btnNotifications.NotificationCount = 0;
            btnNotifications.Padding = new Padding(0, 4, 4, 0);
            btnNotifications.Progress = 0;
            btnNotifications.ProgressColor = Color.FromArgb(0, 103, 205);
            btnNotifications.Size = new Size(44, 44);
            btnNotifications.TabIndex = 16;
            btnNotifications.UseVisualStyleBackColor = true;
            btnNotifications.UseWindowsAccentColor = false;
            btnNotifications.Visible = false;
            btnNotifications.WriteProgress = true;
            btnNotifications.Click += BtnNotifications_Click;
            // 
            // hosts
            // 
            hosts.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hosts.BackColor = Color.FromArgb(65, 65, 65);
            hosts.DropDownStyle = ComboBoxStyle.DropDownList;
            hosts.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            hosts.Icon = null;
            hosts.Location = new Point(706, 41);
            hosts.Margin = new Padding(4);
            hosts.Name = "hosts";
            hosts.Padding = new Padding(12, 3, 12, 3);
            hosts.SelectedIndex = -1;
            hosts.SelectedItem = null;
            hosts.Size = new Size(375, 28);
            hosts.TabIndex = 17;
            hosts.SelectedIndexChanged += Hosts_SelectedIndexChanged;
            // 
            // MainWindow
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(45, 45, 45);
            ClientSize = new Size(1200, 635);
            Controls.Add(hosts);
            Controls.Add(btnNotifications);
            Controls.Add(navigation);
            Controls.Add(lblIpAddressHostname);
            Controls.Add(lblPort);
            Controls.Add(label1);
            Controls.Add(lblNumClientsConnected);
            Controls.Add(contentPanel);
            Controls.Add(lblVersion);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Location = new Point(0, 0);
            Margin = new Padding(10, 4, 10, 4);
            MinimumSize = new Size(1200, 635);
            Name = "MainWindow";
            Text = "Macro Deck 2";
            FormClosing += OnFormClosing;
            Load += MainWindow_Load;
            Controls.SetChildIndex(lblVersion, 0);
            Controls.SetChildIndex(contentPanel, 0);
            Controls.SetChildIndex(lblNumClientsConnected, 0);
            Controls.SetChildIndex(label1, 0);
            Controls.SetChildIndex(lblPort, 0);
            Controls.SetChildIndex(lblIpAddressHostname, 0);
            Controls.SetChildIndex(navigation, 0);
            Controls.SetChildIndex(btnNotifications, 0);
            Controls.SetChildIndex(hosts, 0);
            contentButtonPanel.ResumeLayout(false);
            ((ISupportInitialize)btnDeck).EndInit();
            ((ISupportInitialize)btnExtensions).EndInit();
            ((ISupportInitialize)btnDeviceManager).EndInit();
            ((ISupportInitialize)btnVariables).EndInit();
            ((ISupportInitialize)btnSettings).EndInit();
            navigation.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
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
        private Label lblIpAddressHostname;
        private Panel panel1;
        private Panel panel2;
        private RoundedPanel navigation;
        private NotificationButton btnNotifications;
        private RoundedComboBox hosts;
    }
}