using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.CustomControls.Notifications;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System;

namespace SuchByte.MacroDeck.GUI
{
    partial class MainWindow
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

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
                Language.LanguageManager.LanguageChanged -= LanguageChanged;
                Updater.Updater.OnUpdateAvailable -= UpdateAvailable;
                MacroDeckServer.OnDeviceConnectionStateChanged -= this.OnClientsConnectedChanged;
                MacroDeckServer.OnServerStateChanged -= this.OnServerStateChanged;
                PluginManager.OnPluginsChange -= this.OnPluginsChanged;
                PluginManager.OnUpdateCheckFinished -= OnPackageManagerUpdateCheckFinished;
                IconManager.OnUpdateCheckFinished -= OnPackageManagerUpdateCheckFinished;
<<<<<<< HEAD
                NotificationManager.OnNotification -= NotificationsChanged;
                NotificationManager.OnNotificationRemoved -= NotificationsChanged;
                DeckView?.Dispose();
=======
                MacroDeckLogger.OnWarningOrError -= MacroDeckLogger_OnWarningOrError;

                if (this.DeckView != null)
                {
                    this.DeckView.Dispose();
                }
                if (this.ExtensionsView != null)
                {
                    this.ExtensionsView.Dispose();
                }
                if (this.VariablesView != null)
                {
                    this.VariablesView.Dispose();
                }
>>>>>>> origin/main
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
            this.lblPluginsLoaded = new System.Windows.Forms.Label();
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
            this.lblIPAddress = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.lblPort = new System.Windows.Forms.Label();
            this.lblServerStatus = new System.Windows.Forms.Label();
            this.lblIpAddressHostname = new System.Windows.Forms.Label();
<<<<<<< HEAD
            this.navigation = new SuchByte.MacroDeck.GUI.CustomControls.RoundedPanel();
            this.btnNotifications = new SuchByte.MacroDeck.GUI.CustomControls.Notifications.NotificationButton();
=======
            this.navigation = new System.Windows.Forms.Panel();
            this.warningsErrorPanel = new System.Windows.Forms.Panel();
            this.pictureBox2 = new System.Windows.Forms.PictureBox();
            this.lblErrorsWarnings = new System.Windows.Forms.LinkLabel();
>>>>>>> origin/main
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
            // lblPluginsLoaded
            // 
            this.lblPluginsLoaded.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblPluginsLoaded.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPluginsLoaded.ForeColor = System.Drawing.Color.White;
            this.lblPluginsLoaded.Location = new System.Drawing.Point(987, 613);
            this.lblPluginsLoaded.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblPluginsLoaded.Name = "lblPluginsLoaded";
            this.lblPluginsLoaded.Size = new System.Drawing.Size(209, 20);
            this.lblPluginsLoaded.TabIndex = 3;
            this.lblPluginsLoaded.Text = "0 plugins loaded.";
            this.lblPluginsLoaded.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblPluginsLoaded.UseMnemonic = false;
<<<<<<< HEAD
=======
            this.lblPluginsLoaded.Click += new System.EventHandler(this.lblPluginsLoaded_Click);
>>>>>>> origin/main
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
<<<<<<< HEAD
            this.contentPanel.Location = new System.Drawing.Point(62, 90);
            this.contentPanel.Margin = new System.Windows.Forms.Padding(6, 3, 6, 3);
            this.contentPanel.Name = "contentPanel";
            this.contentPanel.Size = new System.Drawing.Size(1134, 522);
=======
            this.contentPanel.Location = new System.Drawing.Point(62, 64);
            this.contentPanel.Margin = new System.Windows.Forms.Padding(6, 3, 6, 3);
            this.contentPanel.Name = "contentPanel";
            this.contentPanel.Size = new System.Drawing.Size(1134, 548);
>>>>>>> origin/main
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
            this.contentButtonPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.contentButtonPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
<<<<<<< HEAD
            this.contentButtonPanel.Location = new System.Drawing.Point(5, 6);
            this.contentButtonPanel.Margin = new System.Windows.Forms.Padding(0);
            this.contentButtonPanel.Name = "contentButtonPanel";
            this.contentButtonPanel.Size = new System.Drawing.Size(44, 502);
=======
            this.contentButtonPanel.Location = new System.Drawing.Point(0, 0);
            this.contentButtonPanel.Margin = new System.Windows.Forms.Padding(0);
            this.contentButtonPanel.Name = "contentButtonPanel";
            this.contentButtonPanel.Size = new System.Drawing.Size(54, 510);
>>>>>>> origin/main
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
<<<<<<< HEAD
            this.btnDeck.Location = new System.Drawing.Point(0, 0);
            this.btnDeck.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
=======
            this.btnDeck.Location = new System.Drawing.Point(5, 4);
            this.btnDeck.Margin = new System.Windows.Forms.Padding(5, 4, 5, 4);
>>>>>>> origin/main
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
<<<<<<< HEAD
            this.panel1.Location = new System.Drawing.Point(0, 51);
            this.panel1.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
=======
            this.panel1.Location = new System.Drawing.Point(5, 55);
            this.panel1.Margin = new System.Windows.Forms.Padding(5, 3, 5, 3);
>>>>>>> origin/main
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
<<<<<<< HEAD
            this.btnExtensions.Location = new System.Drawing.Point(0, 60);
            this.btnExtensions.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
=======
            this.btnExtensions.Location = new System.Drawing.Point(5, 64);
            this.btnExtensions.Margin = new System.Windows.Forms.Padding(5, 4, 5, 4);
>>>>>>> origin/main
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
<<<<<<< HEAD
            this.btnDeviceManager.Location = new System.Drawing.Point(0, 112);
            this.btnDeviceManager.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
=======
            this.btnDeviceManager.Location = new System.Drawing.Point(5, 116);
            this.btnDeviceManager.Margin = new System.Windows.Forms.Padding(5, 4, 5, 4);
>>>>>>> origin/main
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
<<<<<<< HEAD
            this.btnVariables.Location = new System.Drawing.Point(0, 164);
            this.btnVariables.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
=======
            this.btnVariables.Location = new System.Drawing.Point(5, 168);
            this.btnVariables.Margin = new System.Windows.Forms.Padding(5, 4, 5, 4);
>>>>>>> origin/main
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
<<<<<<< HEAD
            this.panel2.Location = new System.Drawing.Point(0, 215);
            this.panel2.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
=======
            this.panel2.Location = new System.Drawing.Point(5, 219);
            this.panel2.Margin = new System.Windows.Forms.Padding(5, 3, 5, 3);
>>>>>>> origin/main
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
<<<<<<< HEAD
            this.btnSettings.Location = new System.Drawing.Point(5, 512);
=======
            this.btnSettings.Location = new System.Drawing.Point(5, 515);
>>>>>>> origin/main
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
            this.lblNumClientsConnected.Location = new System.Drawing.Point(742, 614);
            this.lblNumClientsConnected.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblNumClientsConnected.Name = "lblNumClientsConnected";
            this.lblNumClientsConnected.Size = new System.Drawing.Size(233, 18);
            this.lblNumClientsConnected.TabIndex = 8;
            this.lblNumClientsConnected.Text = "0 clients connected";
            this.lblNumClientsConnected.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblNumClientsConnected.UseMnemonic = false;
            // 
            // lblIPAddress
            // 
            this.lblIPAddress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblIPAddress.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.lblIPAddress.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblIPAddress.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIPAddress.ForeColor = System.Drawing.Color.White;
<<<<<<< HEAD
            this.lblIPAddress.Location = new System.Drawing.Point(778, 45);
=======
            this.lblIPAddress.Location = new System.Drawing.Point(778, 38);
>>>>>>> origin/main
            this.lblIPAddress.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblIPAddress.Name = "lblIPAddress";
            this.lblIPAddress.Size = new System.Drawing.Size(252, 21);
            this.lblIPAddress.TabIndex = 9;
            this.lblIPAddress.Text = "0.0.0.0";
            this.lblIPAddress.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblIPAddress.UseMnemonic = false;
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
<<<<<<< HEAD
            this.label1.Location = new System.Drawing.Point(1033, 45);
=======
            this.label1.Location = new System.Drawing.Point(1033, 38);
>>>>>>> origin/main
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
            this.lblPort.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblPort.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPort.ForeColor = System.Drawing.Color.White;
<<<<<<< HEAD
            this.lblPort.Location = new System.Drawing.Point(1067, 45);
=======
            this.lblPort.Location = new System.Drawing.Point(1067, 38);
>>>>>>> origin/main
            this.lblPort.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(124, 21);
            this.lblPort.TabIndex = 11;
            this.lblPort.Text = "8191";
            this.lblPort.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblPort.UseMnemonic = false;
            // 
            // lblServerStatus
            // 
            this.lblServerStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblServerStatus.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblServerStatus.ForeColor = System.Drawing.Color.White;
            this.lblServerStatus.Location = new System.Drawing.Point(512, 614);
            this.lblServerStatus.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblServerStatus.Name = "lblServerStatus";
            this.lblServerStatus.Size = new System.Drawing.Size(218, 18);
            this.lblServerStatus.TabIndex = 12;
            this.lblServerStatus.Text = "Server offline";
            this.lblServerStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblServerStatus.UseMnemonic = false;
            // 
            // lblIpAddressHostname
            // 
            this.lblIpAddressHostname.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblIpAddressHostname.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIpAddressHostname.ForeColor = System.Drawing.Color.White;
<<<<<<< HEAD
            this.lblIpAddressHostname.Location = new System.Drawing.Point(454, 47);
=======
            this.lblIpAddressHostname.Location = new System.Drawing.Point(454, 40);
>>>>>>> origin/main
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
<<<<<<< HEAD
            this.navigation.Location = new System.Drawing.Point(0, 41);
            this.navigation.Margin = new System.Windows.Forms.Padding(0);
=======
            this.navigation.Location = new System.Drawing.Point(2, 41);
            this.navigation.Margin = new System.Windows.Forms.Padding(0, 3, 6, 3);
>>>>>>> origin/main
            this.navigation.Name = "navigation";
            this.navigation.Size = new System.Drawing.Size(54, 563);
            this.navigation.TabIndex = 15;
            this.navigation.Visible = false;
            // 
<<<<<<< HEAD
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
            this.btnNotifications.Size = new System.Drawing.Size(43, 43);
            this.btnNotifications.TabIndex = 16;
            this.btnNotifications.UseVisualStyleBackColor = true;
            this.btnNotifications.UseWindowsAccentColor = false;
            this.btnNotifications.Visible = false;
            this.btnNotifications.Click += new System.EventHandler(this.BtnNotifications_Click);
            // 
            // MainWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
=======
            // warningsErrorPanel
            // 
            this.warningsErrorPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.warningsErrorPanel.Controls.Add(this.pictureBox2);
            this.warningsErrorPanel.Controls.Add(this.lblErrorsWarnings);
            this.warningsErrorPanel.Location = new System.Drawing.Point(229, 613);
            this.warningsErrorPanel.Margin = new System.Windows.Forms.Padding(0);
            this.warningsErrorPanel.Name = "warningsErrorPanel";
            this.warningsErrorPanel.Size = new System.Drawing.Size(277, 20);
            this.warningsErrorPanel.TabIndex = 16;
            // 
            // pictureBox2
            // 
            this.pictureBox2.Image = global::SuchByte.MacroDeck.Properties.Resources.Alert;
            this.pictureBox2.Location = new System.Drawing.Point(6, 0);
            this.pictureBox2.Margin = new System.Windows.Forms.Padding(0);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new System.Drawing.Size(20, 20);
            this.pictureBox2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox2.TabIndex = 1;
            this.pictureBox2.TabStop = false;
            // 
            // lblErrorsWarnings
            // 
            this.lblErrorsWarnings.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblErrorsWarnings.ForeColor = System.Drawing.SystemColors.WindowText;
            this.lblErrorsWarnings.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.lblErrorsWarnings.Location = new System.Drawing.Point(30, 3);
            this.lblErrorsWarnings.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.lblErrorsWarnings.Name = "lblErrorsWarnings";
            this.lblErrorsWarnings.Size = new System.Drawing.Size(241, 14);
            this.lblErrorsWarnings.TabIndex = 0;
            this.lblErrorsWarnings.TabStop = true;
            this.lblErrorsWarnings.Text = "0 warning(s), 0 error(s)";
            this.lblErrorsWarnings.UseMnemonic = false;
            this.lblErrorsWarnings.VisitedLinkColor = System.Drawing.Color.Red;
            this.lblErrorsWarnings.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.LblErrorsWarnings_LinkClicked);
            // 
            // MainWindow
            // 
            
>>>>>>> origin/main
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(1200, 635);
            this.Controls.Add(this.btnNotifications);
            this.Controls.Add(this.navigation);
            this.Controls.Add(this.lblIpAddressHostname);
            this.Controls.Add(this.lblServerStatus);
            this.Controls.Add(this.lblPort);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lblIPAddress);
            this.Controls.Add(this.lblNumClientsConnected);
            this.Controls.Add(this.contentPanel);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.lblPluginsLoaded);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Location = new System.Drawing.Point(0, 0);
            this.Margin = new System.Windows.Forms.Padding(7, 3, 7, 3);
            this.MinimumSize = new System.Drawing.Size(1200, 635);
            this.Name = "MainWindow";
            this.Text = "Macro Deck 2";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.OnFormClosing);
            this.Load += new System.EventHandler(this.MainWindow_Load);
            this.Controls.SetChildIndex(this.lblPluginsLoaded, 0);
            this.Controls.SetChildIndex(this.lblVersion, 0);
            this.Controls.SetChildIndex(this.contentPanel, 0);
            this.Controls.SetChildIndex(this.lblNumClientsConnected, 0);
            this.Controls.SetChildIndex(this.lblIPAddress, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.lblPort, 0);
            this.Controls.SetChildIndex(this.lblServerStatus, 0);
            this.Controls.SetChildIndex(this.lblIpAddressHostname, 0);
            this.Controls.SetChildIndex(this.navigation, 0);
            this.Controls.SetChildIndex(this.btnNotifications, 0);
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
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.Label lblPluginsLoaded;
        private CustomControls.BufferedPanel contentPanel;
        private CustomControls.ContentSelectorButton btnDeck;
        private CustomControls.ContentSelectorButton btnSettings;
        public System.Windows.Forms.FlowLayoutPanel contentButtonPanel;
        private CustomControls.ContentSelectorButton btnExtensions;
        private CustomControls.ContentSelectorButton btnDeviceManager;
        private System.Windows.Forms.Label lblNumClientsConnected;
        private System.Windows.Forms.Label lblIPAddress;
        private CustomControls.ContentSelectorButton btnVariables;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.Label lblServerStatus;
        private System.Windows.Forms.Label lblIpAddressHostname;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel panel2;
<<<<<<< HEAD
        private RoundedPanel navigation;
        private NotificationButton btnNotifications;
=======
        private System.Windows.Forms.Panel navigation;
        private System.Windows.Forms.Panel warningsErrorPanel;
        private System.Windows.Forms.PictureBox pictureBox2;
        private System.Windows.Forms.LinkLabel lblErrorsWarnings;
>>>>>>> origin/main
    }
}