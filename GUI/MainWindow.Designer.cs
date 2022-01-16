using SuchByte.MacroDeck.Icons;
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
            try
            {
                Language.LanguageManager.LanguageChanged -= LanguageChanged;
                Updater.Updater.OnUpdateAvailable -= UpdateAvailable;
                MacroDeckServer.OnDeviceConnectionStateChanged -= this.OnClientsConnectedChanged;
                MacroDeckServer.OnServerStateChanged -= this.OnServerStateChanged;
                PluginManager.OnPluginsChange -= this.OnPluginsChanged;
                PluginManager.OnUpdateCheckFinished -= OnPackageManagerUpdateCheckFinished;
                IconManager.OnUpdateCheckFinished -= OnPackageManagerUpdateCheckFinished;
            
                if (this.DeckView != null)
                {
                    this.DeckView.Dispose();
                }
                if (this.PackageManagerView != null)
                {
                    this.PackageManagerView.Dispose();
                }
                if (this.VariablesView != null)
                {
                    this.VariablesView.Dispose();
                }
            }
            catch { }
            if (disposing && (components != null))
            {
                components.Dispose();
            }
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
            this.btnPackageManager = new SuchByte.MacroDeck.GUI.CustomControls.ContentSelectorButton();
            this.btnDeviceManager = new SuchByte.MacroDeck.GUI.CustomControls.ContentSelectorButton();
            this.btnVariables = new SuchByte.MacroDeck.GUI.CustomControls.ContentSelectorButton();
            this.panel2 = new System.Windows.Forms.Panel();
            this.btnSettings = new SuchByte.MacroDeck.GUI.CustomControls.ContentSelectorButton();
            this.lblPluginsNotLoaded = new System.Windows.Forms.Label();
            this.lblNumClientsConnected = new System.Windows.Forms.Label();
            this.lblIPAddress = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.lblPort = new System.Windows.Forms.Label();
            this.lblServerStatus = new System.Windows.Forms.Label();
            this.lblIpAddressHostname = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.navigation = new System.Windows.Forms.Panel();
            this.contentButtonPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeck)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnPackageManager)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeviceManager)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnVariables)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnSettings)).BeginInit();
            this.navigation.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblSafeMode
            // 
            this.lblSafeMode.Size = new System.Drawing.Size(140, 30);
            // 
            // lblPluginsLoaded
            // 
            this.lblPluginsLoaded.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblPluginsLoaded.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPluginsLoaded.ForeColor = System.Drawing.Color.White;
            this.lblPluginsLoaded.Location = new System.Drawing.Point(1061, 612);
            this.lblPluginsLoaded.Name = "lblPluginsLoaded";
            this.lblPluginsLoaded.Size = new System.Drawing.Size(134, 21);
            this.lblPluginsLoaded.TabIndex = 3;
            this.lblPluginsLoaded.Text = "0 plugins loaded.";
            this.lblPluginsLoaded.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // lblVersion
            // 
            this.lblVersion.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblVersion.ForeColor = System.Drawing.Color.White;
            this.lblVersion.Location = new System.Drawing.Point(67, 611);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(208, 21);
            this.lblVersion.TabIndex = 3;
            this.lblVersion.Text = "2.0.0";
            this.lblVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // contentPanel
            // 
            this.contentPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.contentPanel.Location = new System.Drawing.Point(61, 68);
            this.contentPanel.Name = "contentPanel";
            this.contentPanel.Size = new System.Drawing.Size(1137, 540);
            this.contentPanel.TabIndex = 4;
            // 
            // contentButtonPanel
            // 
            this.contentButtonPanel.AutoScroll = true;
            this.contentButtonPanel.Controls.Add(this.btnDeck);
            this.contentButtonPanel.Controls.Add(this.panel1);
            this.contentButtonPanel.Controls.Add(this.btnPackageManager);
            this.contentButtonPanel.Controls.Add(this.btnDeviceManager);
            this.contentButtonPanel.Controls.Add(this.btnVariables);
            this.contentButtonPanel.Controls.Add(this.panel2);
            this.contentButtonPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.contentButtonPanel.Location = new System.Drawing.Point(3, 3);
            this.contentButtonPanel.Name = "contentButtonPanel";
            this.contentButtonPanel.Size = new System.Drawing.Size(53, 524);
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
            this.btnDeck.Location = new System.Drawing.Point(5, 5);
            this.btnDeck.Margin = new System.Windows.Forms.Padding(5);
            this.btnDeck.Name = "btnDeck";
            this.btnDeck.Selected = false;
            this.btnDeck.Size = new System.Drawing.Size(43, 43);
            this.btnDeck.TabIndex = 0;
            this.btnDeck.TabStop = false;
            this.btnDeck.Click += new System.EventHandler(this.BtnDeck_Click);
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.Silver;
            this.panel1.Location = new System.Drawing.Point(5, 56);
            this.panel1.Margin = new System.Windows.Forms.Padding(5, 3, 5, 3);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(43, 2);
            this.panel1.TabIndex = 4;
            // 
            // btnPackageManager
            // 
            this.btnPackageManager.BackColor = System.Drawing.Color.Transparent;
            this.btnPackageManager.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Package_Manager_icon;
            this.btnPackageManager.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnPackageManager.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPackageManager.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnPackageManager.ForeColor = System.Drawing.Color.White;
            this.btnPackageManager.Location = new System.Drawing.Point(5, 66);
            this.btnPackageManager.Margin = new System.Windows.Forms.Padding(5);
            this.btnPackageManager.Name = "btnPackageManager";
            this.btnPackageManager.Selected = false;
            this.btnPackageManager.Size = new System.Drawing.Size(43, 43);
            this.btnPackageManager.TabIndex = 1;
            this.btnPackageManager.TabStop = false;
            this.btnPackageManager.Click += new System.EventHandler(this.BtnPackageManager_Click);
            // 
            // btnDeviceManager
            // 
            this.btnDeviceManager.BackColor = System.Drawing.Color.Transparent;
            this.btnDeviceManager.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.device_manager;
            this.btnDeviceManager.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnDeviceManager.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeviceManager.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDeviceManager.ForeColor = System.Drawing.Color.White;
            this.btnDeviceManager.Location = new System.Drawing.Point(5, 119);
            this.btnDeviceManager.Margin = new System.Windows.Forms.Padding(5);
            this.btnDeviceManager.Name = "btnDeviceManager";
            this.btnDeviceManager.Selected = false;
            this.btnDeviceManager.Size = new System.Drawing.Size(43, 43);
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
            this.btnVariables.Location = new System.Drawing.Point(5, 172);
            this.btnVariables.Margin = new System.Windows.Forms.Padding(5);
            this.btnVariables.Name = "btnVariables";
            this.btnVariables.Selected = false;
            this.btnVariables.Size = new System.Drawing.Size(43, 43);
            this.btnVariables.TabIndex = 3;
            this.btnVariables.TabStop = false;
            this.btnVariables.Text = "{x}";
            this.btnVariables.Click += new System.EventHandler(this.BtnVariables_Click);
            // 
            // panel2
            // 
            this.panel2.BackColor = System.Drawing.Color.Silver;
            this.panel2.Location = new System.Drawing.Point(5, 223);
            this.panel2.Margin = new System.Windows.Forms.Padding(5, 3, 5, 3);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(43, 2);
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
            this.btnSettings.Location = new System.Drawing.Point(5, 550);
            this.btnSettings.Margin = new System.Windows.Forms.Padding(5);
            this.btnSettings.Name = "btnSettings";
            this.btnSettings.Selected = false;
            this.btnSettings.Size = new System.Drawing.Size(47, 44);
            this.btnSettings.TabIndex = 1;
            this.btnSettings.TabStop = false;
            this.btnSettings.Click += new System.EventHandler(this.BtnSettings_Click);
            // 
            // lblPluginsNotLoaded
            // 
            this.lblPluginsNotLoaded.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblPluginsNotLoaded.ForeColor = System.Drawing.Color.Red;
            this.lblPluginsNotLoaded.Location = new System.Drawing.Point(854, 612);
            this.lblPluginsNotLoaded.Name = "lblPluginsNotLoaded";
            this.lblPluginsNotLoaded.Size = new System.Drawing.Size(189, 21);
            this.lblPluginsNotLoaded.TabIndex = 7;
            this.lblPluginsNotLoaded.Text = "0 plugins not loaded.";
            this.lblPluginsNotLoaded.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblPluginsNotLoaded.Visible = false;
            // 
            // lblNumClientsConnected
            // 
            this.lblNumClientsConnected.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblNumClientsConnected.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblNumClientsConnected.ForeColor = System.Drawing.Color.White;
            this.lblNumClientsConnected.Location = new System.Drawing.Point(603, 612);
            this.lblNumClientsConnected.Name = "lblNumClientsConnected";
            this.lblNumClientsConnected.Size = new System.Drawing.Size(225, 19);
            this.lblNumClientsConnected.TabIndex = 8;
            this.lblNumClientsConnected.Text = "0 clients connected";
            this.lblNumClientsConnected.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // lblIPAddress
            // 
            this.lblIPAddress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblIPAddress.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.lblIPAddress.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblIPAddress.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIPAddress.ForeColor = System.Drawing.Color.White;
            this.lblIPAddress.Location = new System.Drawing.Point(933, 37);
            this.lblIPAddress.Name = "lblIPAddress";
            this.lblIPAddress.Size = new System.Drawing.Size(159, 28);
            this.lblIPAddress.TabIndex = 9;
            this.lblIPAddress.Text = "0.0.0.0";
            this.lblIPAddress.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.Location = new System.Drawing.Point(1094, 42);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(15, 19);
            this.label1.TabIndex = 10;
            this.label1.Text = ":";
            // 
            // lblPort
            // 
            this.lblPort.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblPort.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.lblPort.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblPort.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPort.ForeColor = System.Drawing.Color.White;
            this.lblPort.Location = new System.Drawing.Point(1115, 36);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(79, 28);
            this.lblPort.TabIndex = 11;
            this.lblPort.Text = "8191";
            this.lblPort.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // lblServerStatus
            // 
            this.lblServerStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblServerStatus.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblServerStatus.ForeColor = System.Drawing.Color.White;
            this.lblServerStatus.Location = new System.Drawing.Point(372, 613);
            this.lblServerStatus.Name = "lblServerStatus";
            this.lblServerStatus.Size = new System.Drawing.Size(225, 19);
            this.lblServerStatus.TabIndex = 12;
            this.lblServerStatus.Text = "Server offline";
            this.lblServerStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // lblIpAddressHostname
            // 
            this.lblIpAddressHostname.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblIpAddressHostname.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIpAddressHostname.ForeColor = System.Drawing.Color.White;
            this.lblIpAddressHostname.Location = new System.Drawing.Point(702, 42);
            this.lblIpAddressHostname.Name = "lblIpAddressHostname";
            this.lblIpAddressHostname.Size = new System.Drawing.Size(225, 19);
            this.lblIpAddressHostname.TabIndex = 13;
            this.lblIpAddressHostname.Text = "IP address/hostname : Port";
            this.lblIpAddressHostname.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // lblTitle
            // 
            this.lblTitle.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblTitle.ForeColor = System.Drawing.Color.Silver;
            this.lblTitle.Location = new System.Drawing.Point(83, 37);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(305, 27);
            this.lblTitle.TabIndex = 14;
            this.lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblTitle.Click += new System.EventHandler(this.lblTitle_Click);
            // 
            // navigation
            // 
            this.navigation.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.navigation.Controls.Add(this.contentButtonPanel);
            this.navigation.Controls.Add(this.btnSettings);
            this.navigation.Dock = System.Windows.Forms.DockStyle.Left;
            this.navigation.Location = new System.Drawing.Point(2, 34);
            this.navigation.Name = "navigation";
            this.navigation.Size = new System.Drawing.Size(59, 599);
            this.navigation.TabIndex = 15;
            this.navigation.Visible = false;
            // 
            // MainWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(1200, 635);
            this.Controls.Add(this.navigation);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblIpAddressHostname);
            this.Controls.Add(this.lblServerStatus);
            this.Controls.Add(this.lblPort);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lblIPAddress);
            this.Controls.Add(this.lblNumClientsConnected);
            this.Controls.Add(this.lblPluginsNotLoaded);
            this.Controls.Add(this.contentPanel);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.lblPluginsLoaded);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(1200, 635);
            this.Name = "MainWindow";
            this.Padding = new System.Windows.Forms.Padding(2);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Macro Deck 2";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.OnFormClosing);
            this.Load += new System.EventHandler(this.MainWindow_Load);
            this.Controls.SetChildIndex(this.lblPluginsLoaded, 0);
            this.Controls.SetChildIndex(this.lblVersion, 0);
            this.Controls.SetChildIndex(this.contentPanel, 0);
            this.Controls.SetChildIndex(this.lblPluginsNotLoaded, 0);
            this.Controls.SetChildIndex(this.lblNumClientsConnected, 0);
            this.Controls.SetChildIndex(this.lblIPAddress, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.lblPort, 0);
            this.Controls.SetChildIndex(this.lblServerStatus, 0);
            this.Controls.SetChildIndex(this.lblIpAddressHostname, 0);
            this.Controls.SetChildIndex(this.lblTitle, 0);
            this.Controls.SetChildIndex(this.navigation, 0);
            this.contentButtonPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnDeck)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnPackageManager)).EndInit();
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
        private CustomControls.ContentSelectorButton btnPackageManager;
        private System.Windows.Forms.Label lblPluginsNotLoaded;
        private CustomControls.ContentSelectorButton btnDeviceManager;
        private System.Windows.Forms.Label lblNumClientsConnected;
        private System.Windows.Forms.Label lblIPAddress;
        private CustomControls.ContentSelectorButton btnVariables;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.Label lblServerStatus;
        private System.Windows.Forms.Label lblIpAddressHostname;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.Panel navigation;
    }
}