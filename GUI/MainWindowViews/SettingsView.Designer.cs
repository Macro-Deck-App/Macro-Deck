
namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    partial class SettingsView
    {
        /// <summary> 
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Komponenten-Designer generierter Code

        /// <summary> 
        /// Erforderliche Methode für die Designerunterstützung. 
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsView));
            this.verticalTabControl = new SuchByte.MacroDeck.GUI.CustomControls.VerticalTabControl();
            this.tabGeneral = new System.Windows.Forms.TabPage();
            this.language = new System.Windows.Forms.ComboBox();
            this.lblLanguage = new System.Windows.Forms.Label();
            this.checkStartWindows = new System.Windows.Forms.CheckBox();
            this.lblBehaviour = new System.Windows.Forms.Label();
            this.lblGeneral = new System.Windows.Forms.Label();
            this.tabConnection = new System.Windows.Forms.TabPage();
            this.btnChangePort = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.groupConnectionInfo = new System.Windows.Forms.GroupBox();
            this.lblConnectionInfo = new System.Windows.Forms.Label();
            this.port = new System.Windows.Forms.NumericUpDown();
            this.lblPort = new System.Windows.Forms.Label();
            this.lblIpAddessLabel = new System.Windows.Forms.Label();
            this.lblIpAddress = new System.Windows.Forms.Label();
            this.networkAdapter = new System.Windows.Forms.ComboBox();
            this.lblNetworkAdapter = new System.Windows.Forms.Label();
            this.lblConnection = new System.Windows.Forms.Label();
            this.tabBackup = new System.Windows.Forms.TabPage();
            this.label19 = new System.Windows.Forms.Label();
            this.lblBackup = new System.Windows.Forms.Label();
            this.tabUpdater = new System.Windows.Forms.TabPage();
            this.progressCheckUpdates = new System.Windows.Forms.ProgressBar();
            this.checkAutoUpdate = new System.Windows.Forms.CheckBox();
            this.updateChannel = new System.Windows.Forms.ComboBox();
            this.lblUpdateChannelLabel = new System.Windows.Forms.Label();
            this.btnCheckUpdates = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblInstalledVersion = new System.Windows.Forms.Label();
            this.lblInstalledVersionLabel = new System.Windows.Forms.Label();
            this.lblUpdates = new System.Windows.Forms.Label();
            this.tabAbout = new System.Windows.Forms.TabPage();
            this.lblTranslationBy = new System.Windows.Forms.Label();
            this.btnLicenses = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblPluginAPIVersion = new System.Windows.Forms.Label();
            this.lblWebsocketAPIVersion = new System.Windows.Forms.Label();
            this.lblPluginAPILabel = new System.Windows.Forms.Label();
            this.lblWebSocketAPILabel = new System.Windows.Forms.Label();
            this.lblOS = new System.Windows.Forms.Label();
            this.lblInstalledPlugins = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            this.lblOSLabel = new System.Windows.Forms.Label();
            this.lblInstalledPluginsLabel = new System.Windows.Forms.Label();
            this.lblVersionLabel = new System.Windows.Forms.Label();
            this.lblDeveloped = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.verticalTabControl.SuspendLayout();
            this.tabGeneral.SuspendLayout();
            this.tabConnection.SuspendLayout();
            this.groupConnectionInfo.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.port)).BeginInit();
            this.tabBackup.SuspendLayout();
            this.tabUpdater.SuspendLayout();
            this.tabAbout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // verticalTabControl
            // 
            this.verticalTabControl.Alignment = System.Windows.Forms.TabAlignment.Left;
            this.verticalTabControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.verticalTabControl.Controls.Add(this.tabGeneral);
            this.verticalTabControl.Controls.Add(this.tabConnection);
            this.verticalTabControl.Controls.Add(this.tabBackup);
            this.verticalTabControl.Controls.Add(this.tabUpdater);
            this.verticalTabControl.Controls.Add(this.tabAbout);
            this.verticalTabControl.ItemSize = new System.Drawing.Size(44, 136);
            this.verticalTabControl.Location = new System.Drawing.Point(3, 3);
            this.verticalTabControl.Multiline = true;
            this.verticalTabControl.Name = "verticalTabControl";
            this.verticalTabControl.SelectedIndex = 0;
            this.verticalTabControl.Size = new System.Drawing.Size(1131, 534);
            this.verticalTabControl.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.verticalTabControl.TabIndex = 12;
            // 
            // tabGeneral
            // 
            this.tabGeneral.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabGeneral.Controls.Add(this.language);
            this.tabGeneral.Controls.Add(this.lblLanguage);
            this.tabGeneral.Controls.Add(this.checkStartWindows);
            this.tabGeneral.Controls.Add(this.lblBehaviour);
            this.tabGeneral.Controls.Add(this.lblGeneral);
            this.tabGeneral.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.tabGeneral.ForeColor = System.Drawing.Color.White;
            this.tabGeneral.Location = new System.Drawing.Point(140, 4);
            this.tabGeneral.Name = "tabGeneral";
            this.tabGeneral.Padding = new System.Windows.Forms.Padding(3);
            this.tabGeneral.Size = new System.Drawing.Size(973, 528);
            this.tabGeneral.TabIndex = 0;
            this.tabGeneral.Text = "General";
            // 
            // language
            // 
            this.language.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.language.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.language.ForeColor = System.Drawing.Color.White;
            this.language.FormattingEnabled = true;
            this.language.Location = new System.Drawing.Point(12, 183);
            this.language.Name = "language";
            this.language.Size = new System.Drawing.Size(276, 27);
            this.language.TabIndex = 4;
            this.language.SelectedIndexChanged += new System.EventHandler(this.Language_SelectedIndexChanged);
            // 
            // lblLanguage
            // 
            this.lblLanguage.AutoSize = true;
            this.lblLanguage.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblLanguage.ForeColor = System.Drawing.Color.Gray;
            this.lblLanguage.Location = new System.Drawing.Point(3, 159);
            this.lblLanguage.Name = "lblLanguage";
            this.lblLanguage.Size = new System.Drawing.Size(93, 23);
            this.lblLanguage.TabIndex = 3;
            this.lblLanguage.Text = "Language";
            // 
            // checkStartWindows
            // 
            this.checkStartWindows.AutoSize = true;
            this.checkStartWindows.Location = new System.Drawing.Point(12, 87);
            this.checkStartWindows.Name = "checkStartWindows";
            this.checkStartWindows.Size = new System.Drawing.Size(165, 23);
            this.checkStartWindows.TabIndex = 2;
            this.checkStartWindows.Text = "Start with Windows";
            this.checkStartWindows.UseVisualStyleBackColor = true;
            this.checkStartWindows.CheckedChanged += new System.EventHandler(this.CheckStartWindows_CheckedChanged);
            // 
            // lblBehaviour
            // 
            this.lblBehaviour.AutoSize = true;
            this.lblBehaviour.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblBehaviour.ForeColor = System.Drawing.Color.Gray;
            this.lblBehaviour.Location = new System.Drawing.Point(3, 63);
            this.lblBehaviour.Name = "lblBehaviour";
            this.lblBehaviour.Size = new System.Drawing.Size(82, 23);
            this.lblBehaviour.TabIndex = 1;
            this.lblBehaviour.Text = "Behavior";
            // 
            // lblGeneral
            // 
            this.lblGeneral.AutoSize = true;
            this.lblGeneral.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblGeneral.Location = new System.Drawing.Point(3, 0);
            this.lblGeneral.Name = "lblGeneral";
            this.lblGeneral.Size = new System.Drawing.Size(84, 25);
            this.lblGeneral.TabIndex = 0;
            this.lblGeneral.Text = "General";
            // 
            // tabConnection
            // 
            this.tabConnection.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabConnection.Controls.Add(this.btnChangePort);
            this.tabConnection.Controls.Add(this.groupConnectionInfo);
            this.tabConnection.Controls.Add(this.port);
            this.tabConnection.Controls.Add(this.lblPort);
            this.tabConnection.Controls.Add(this.lblIpAddessLabel);
            this.tabConnection.Controls.Add(this.lblIpAddress);
            this.tabConnection.Controls.Add(this.networkAdapter);
            this.tabConnection.Controls.Add(this.lblNetworkAdapter);
            this.tabConnection.Controls.Add(this.lblConnection);
            this.tabConnection.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.tabConnection.ForeColor = System.Drawing.Color.White;
            this.tabConnection.Location = new System.Drawing.Point(140, 4);
            this.tabConnection.Name = "tabConnection";
            this.tabConnection.Size = new System.Drawing.Size(973, 528);
            this.tabConnection.TabIndex = 3;
            this.tabConnection.Text = "Connection";
            // 
            // btnChangePort
            // 
            this.btnChangePort.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnChangePort.BorderRadius = 8;
            this.btnChangePort.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnChangePort.FlatAppearance.BorderSize = 0;
            this.btnChangePort.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnChangePort.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnChangePort.ForeColor = System.Drawing.Color.White;
            this.btnChangePort.Location = new System.Drawing.Point(125, 178);
            this.btnChangePort.Name = "btnChangePort";
            this.btnChangePort.Size = new System.Drawing.Size(46, 24);
            this.btnChangePort.TabIndex = 12;
            this.btnChangePort.Text = "Ok";
            this.btnChangePort.UseVisualStyleBackColor = false;
            this.btnChangePort.Click += new System.EventHandler(this.BtnChangePort_Click);
            // 
            // groupConnectionInfo
            // 
            this.groupConnectionInfo.Controls.Add(this.lblConnectionInfo);
            this.groupConnectionInfo.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.groupConnectionInfo.ForeColor = System.Drawing.Color.White;
            this.groupConnectionInfo.Location = new System.Drawing.Point(12, 342);
            this.groupConnectionInfo.Name = "groupConnectionInfo";
            this.groupConnectionInfo.Size = new System.Drawing.Size(685, 173);
            this.groupConnectionInfo.TabIndex = 11;
            this.groupConnectionInfo.TabStop = false;
            this.groupConnectionInfo.Text = "Info";
            // 
            // lblConnectionInfo
            // 
            this.lblConnectionInfo.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblConnectionInfo.Location = new System.Drawing.Point(6, 24);
            this.lblConnectionInfo.Name = "lblConnectionInfo";
            this.lblConnectionInfo.Size = new System.Drawing.Size(673, 146);
            this.lblConnectionInfo.TabIndex = 0;
            this.lblConnectionInfo.Text = resources.GetString("lblConnectionInfo.Text");
            // 
            // port
            // 
            this.port.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.port.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.port.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.port.ForeColor = System.Drawing.Color.White;
            this.port.Location = new System.Drawing.Point(12, 178);
            this.port.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.port.Name = "port";
            this.port.Size = new System.Drawing.Size(107, 26);
            this.port.TabIndex = 9;
            this.port.Value = new decimal(new int[] {
            8191,
            0,
            0,
            0});
            // 
            // lblPort
            // 
            this.lblPort.AutoSize = true;
            this.lblPort.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPort.ForeColor = System.Drawing.Color.Gray;
            this.lblPort.Location = new System.Drawing.Point(3, 154);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(43, 23);
            this.lblPort.TabIndex = 8;
            this.lblPort.Text = "Port";
            // 
            // lblIpAddessLabel
            // 
            this.lblIpAddessLabel.AutoSize = true;
            this.lblIpAddessLabel.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIpAddessLabel.Location = new System.Drawing.Point(12, 115);
            this.lblIpAddessLabel.Name = "lblIpAddessLabel";
            this.lblIpAddessLabel.Size = new System.Drawing.Size(83, 18);
            this.lblIpAddessLabel.TabIndex = 7;
            this.lblIpAddessLabel.Text = "IP address:";
            // 
            // lblIpAddress
            // 
            this.lblIpAddress.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIpAddress.Location = new System.Drawing.Point(101, 115);
            this.lblIpAddress.Name = "lblIpAddress";
            this.lblIpAddress.Size = new System.Drawing.Size(187, 17);
            this.lblIpAddress.TabIndex = 6;
            this.lblIpAddress.Text = "0.0.0.0";
            // 
            // networkAdapter
            // 
            this.networkAdapter.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.networkAdapter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.networkAdapter.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.networkAdapter.ForeColor = System.Drawing.Color.White;
            this.networkAdapter.FormattingEnabled = true;
            this.networkAdapter.Location = new System.Drawing.Point(12, 87);
            this.networkAdapter.Name = "networkAdapter";
            this.networkAdapter.Size = new System.Drawing.Size(276, 27);
            this.networkAdapter.TabIndex = 5;
            this.networkAdapter.SelectedIndexChanged += new System.EventHandler(this.NetworkAdapter_SelectedIndexChanged);
            // 
            // lblNetworkAdapter
            // 
            this.lblNetworkAdapter.AutoSize = true;
            this.lblNetworkAdapter.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblNetworkAdapter.ForeColor = System.Drawing.Color.Gray;
            this.lblNetworkAdapter.Location = new System.Drawing.Point(3, 63);
            this.lblNetworkAdapter.Name = "lblNetworkAdapter";
            this.lblNetworkAdapter.Size = new System.Drawing.Size(150, 23);
            this.lblNetworkAdapter.TabIndex = 2;
            this.lblNetworkAdapter.Text = "Network adapter";
            // 
            // lblConnection
            // 
            this.lblConnection.AutoSize = true;
            this.lblConnection.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblConnection.Location = new System.Drawing.Point(3, 0);
            this.lblConnection.Name = "lblConnection";
            this.lblConnection.Size = new System.Drawing.Size(116, 25);
            this.lblConnection.TabIndex = 1;
            this.lblConnection.Text = "Connection";
            // 
            // tabBackup
            // 
            this.tabBackup.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabBackup.Controls.Add(this.label19);
            this.tabBackup.Controls.Add(this.lblBackup);
            this.tabBackup.Location = new System.Drawing.Point(140, 4);
            this.tabBackup.Name = "tabBackup";
            this.tabBackup.Size = new System.Drawing.Size(973, 528);
            this.tabBackup.TabIndex = 4;
            this.tabBackup.Text = "Backup";
            // 
            // label19
            // 
            this.label19.AutoSize = true;
            this.label19.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label19.ForeColor = System.Drawing.Color.Gray;
            this.label19.Location = new System.Drawing.Point(3, 39);
            this.label19.Name = "label19";
            this.label19.Size = new System.Drawing.Size(118, 23);
            this.label19.TabIndex = 4;
            this.label19.Text = "Coming soon";
            // 
            // lblBackup
            // 
            this.lblBackup.AutoSize = true;
            this.lblBackup.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblBackup.ForeColor = System.Drawing.Color.White;
            this.lblBackup.Location = new System.Drawing.Point(3, 0);
            this.lblBackup.Name = "lblBackup";
            this.lblBackup.Size = new System.Drawing.Size(80, 25);
            this.lblBackup.TabIndex = 2;
            this.lblBackup.Text = "Backup";
            // 
            // tabUpdater
            // 
            this.tabUpdater.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabUpdater.Controls.Add(this.progressCheckUpdates);
            this.tabUpdater.Controls.Add(this.checkAutoUpdate);
            this.tabUpdater.Controls.Add(this.updateChannel);
            this.tabUpdater.Controls.Add(this.lblUpdateChannelLabel);
            this.tabUpdater.Controls.Add(this.btnCheckUpdates);
            this.tabUpdater.Controls.Add(this.lblInstalledVersion);
            this.tabUpdater.Controls.Add(this.lblInstalledVersionLabel);
            this.tabUpdater.Controls.Add(this.lblUpdates);
            this.tabUpdater.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.tabUpdater.ForeColor = System.Drawing.Color.White;
            this.tabUpdater.Location = new System.Drawing.Point(140, 4);
            this.tabUpdater.Name = "tabUpdater";
            this.tabUpdater.Size = new System.Drawing.Size(973, 528);
            this.tabUpdater.TabIndex = 1;
            this.tabUpdater.Text = "Updates";
            // 
            // progressCheckUpdates
            // 
            this.progressCheckUpdates.Location = new System.Drawing.Point(3, 185);
            this.progressCheckUpdates.Name = "progressCheckUpdates";
            this.progressCheckUpdates.Size = new System.Drawing.Size(180, 9);
            this.progressCheckUpdates.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressCheckUpdates.TabIndex = 13;
            this.progressCheckUpdates.Visible = false;
            // 
            // checkAutoUpdate
            // 
            this.checkAutoUpdate.AutoSize = true;
            this.checkAutoUpdate.Location = new System.Drawing.Point(3, 119);
            this.checkAutoUpdate.Name = "checkAutoUpdate";
            this.checkAutoUpdate.Size = new System.Drawing.Size(253, 23);
            this.checkAutoUpdate.TabIndex = 12;
            this.checkAutoUpdate.Text = "Automatically check for updates";
            this.checkAutoUpdate.UseVisualStyleBackColor = true;
            this.checkAutoUpdate.CheckedChanged += new System.EventHandler(this.CheckAutoUpdate_CheckedChanged);
            // 
            // updateChannel
            // 
            this.updateChannel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.updateChannel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.updateChannel.Enabled = false;
            this.updateChannel.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.updateChannel.ForeColor = System.Drawing.Color.White;
            this.updateChannel.FormattingEnabled = true;
            this.updateChannel.Items.AddRange(new object[] {
            "Dev",
            "Beta",
            "Stable"});
            this.updateChannel.Location = new System.Drawing.Point(167, 75);
            this.updateChannel.Name = "updateChannel";
            this.updateChannel.Size = new System.Drawing.Size(119, 27);
            this.updateChannel.TabIndex = 11;
            this.updateChannel.SelectedIndexChanged += new System.EventHandler(this.UpdateChannel_SelectedIndexChanged);
            // 
            // lblUpdateChannelLabel
            // 
            this.lblUpdateChannelLabel.AutoSize = true;
            this.lblUpdateChannelLabel.Location = new System.Drawing.Point(3, 77);
            this.lblUpdateChannelLabel.Name = "lblUpdateChannelLabel";
            this.lblUpdateChannelLabel.Size = new System.Drawing.Size(124, 19);
            this.lblUpdateChannelLabel.TabIndex = 10;
            this.lblUpdateChannelLabel.Text = "Update channel:";
            // 
            // btnCheckUpdates
            // 
            this.btnCheckUpdates.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnCheckUpdates.BorderRadius = 8;
            this.btnCheckUpdates.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCheckUpdates.FlatAppearance.BorderSize = 0;
            this.btnCheckUpdates.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckUpdates.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnCheckUpdates.ForeColor = System.Drawing.Color.White;
            this.btnCheckUpdates.Location = new System.Drawing.Point(3, 161);
            this.btnCheckUpdates.Name = "btnCheckUpdates";
            this.btnCheckUpdates.Size = new System.Drawing.Size(180, 23);
            this.btnCheckUpdates.TabIndex = 9;
            this.btnCheckUpdates.Text = "Check for updates now";
            this.btnCheckUpdates.UseVisualStyleBackColor = false;
            this.btnCheckUpdates.Click += new System.EventHandler(this.BtnCheckUpdates_Click);
            // 
            // lblInstalledVersion
            // 
            this.lblInstalledVersion.AutoSize = true;
            this.lblInstalledVersion.Location = new System.Drawing.Point(167, 42);
            this.lblInstalledVersion.Name = "lblInstalledVersion";
            this.lblInstalledVersion.Size = new System.Drawing.Size(46, 19);
            this.lblInstalledVersion.TabIndex = 8;
            this.lblInstalledVersion.Text = "2.0.0";
            // 
            // lblInstalledVersionLabel
            // 
            this.lblInstalledVersionLabel.AutoSize = true;
            this.lblInstalledVersionLabel.Location = new System.Drawing.Point(3, 42);
            this.lblInstalledVersionLabel.Name = "lblInstalledVersionLabel";
            this.lblInstalledVersionLabel.Size = new System.Drawing.Size(133, 19);
            this.lblInstalledVersionLabel.TabIndex = 7;
            this.lblInstalledVersionLabel.Text = "Installed Version:";
            // 
            // lblUpdates
            // 
            this.lblUpdates.AutoSize = true;
            this.lblUpdates.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblUpdates.Location = new System.Drawing.Point(3, 0);
            this.lblUpdates.Name = "lblUpdates";
            this.lblUpdates.Size = new System.Drawing.Size(88, 25);
            this.lblUpdates.TabIndex = 2;
            this.lblUpdates.Text = "Updates";
            // 
            // tabAbout
            // 
            this.tabAbout.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabAbout.Controls.Add(this.lblTranslationBy);
            this.tabAbout.Controls.Add(this.btnLicenses);
            this.tabAbout.Controls.Add(this.lblPluginAPIVersion);
            this.tabAbout.Controls.Add(this.lblWebsocketAPIVersion);
            this.tabAbout.Controls.Add(this.lblPluginAPILabel);
            this.tabAbout.Controls.Add(this.lblWebSocketAPILabel);
            this.tabAbout.Controls.Add(this.lblOS);
            this.tabAbout.Controls.Add(this.lblInstalledPlugins);
            this.tabAbout.Controls.Add(this.lblVersion);
            this.tabAbout.Controls.Add(this.lblOSLabel);
            this.tabAbout.Controls.Add(this.lblInstalledPluginsLabel);
            this.tabAbout.Controls.Add(this.lblVersionLabel);
            this.tabAbout.Controls.Add(this.lblDeveloped);
            this.tabAbout.Controls.Add(this.label2);
            this.tabAbout.Controls.Add(this.pictureBox1);
            this.tabAbout.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.tabAbout.ForeColor = System.Drawing.Color.White;
            this.tabAbout.Location = new System.Drawing.Point(140, 4);
            this.tabAbout.Name = "tabAbout";
            this.tabAbout.Size = new System.Drawing.Size(987, 526);
            this.tabAbout.TabIndex = 2;
            this.tabAbout.Text = "About";
            // 
            // lblTranslationBy
            // 
            this.lblTranslationBy.AutoSize = true;
            this.lblTranslationBy.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblTranslationBy.Location = new System.Drawing.Point(3, 505);
            this.lblTranslationBy.Name = "lblTranslationBy";
            this.lblTranslationBy.Size = new System.Drawing.Size(225, 18);
            this.lblTranslationBy.TabIndex = 14;
            this.lblTranslationBy.Text = "English translation by Macro Deck";
            // 
            // btnLicenses
            // 
            this.btnLicenses.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnLicenses.BorderRadius = 8;
            this.btnLicenses.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnLicenses.FlatAppearance.BorderSize = 0;
            this.btnLicenses.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLicenses.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnLicenses.ForeColor = System.Drawing.Color.White;
            this.btnLicenses.Location = new System.Drawing.Point(770, 196);
            this.btnLicenses.Name = "btnLicenses";
            this.btnLicenses.Size = new System.Drawing.Size(200, 31);
            this.btnLicenses.TabIndex = 13;
            this.btnLicenses.Text = "3rd party licenses";
            this.btnLicenses.UseVisualStyleBackColor = false;
            this.btnLicenses.Click += new System.EventHandler(this.BtnLicenses_Click);
            // 
            // lblPluginAPIVersion
            // 
            this.lblPluginAPIVersion.AutoSize = true;
            this.lblPluginAPIVersion.Location = new System.Drawing.Point(241, 135);
            this.lblPluginAPIVersion.Name = "lblPluginAPIVersion";
            this.lblPluginAPIVersion.Size = new System.Drawing.Size(27, 19);
            this.lblPluginAPIVersion.TabIndex = 12;
            this.lblPluginAPIVersion.Text = "20";
            // 
            // lblWebsocketAPIVersion
            // 
            this.lblWebsocketAPIVersion.AutoSize = true;
            this.lblWebsocketAPIVersion.Location = new System.Drawing.Point(241, 110);
            this.lblWebsocketAPIVersion.Name = "lblWebsocketAPIVersion";
            this.lblWebsocketAPIVersion.Size = new System.Drawing.Size(27, 19);
            this.lblWebsocketAPIVersion.TabIndex = 11;
            this.lblWebsocketAPIVersion.Text = "20";
            // 
            // lblPluginAPILabel
            // 
            this.lblPluginAPILabel.AutoSize = true;
            this.lblPluginAPILabel.Location = new System.Drawing.Point(3, 135);
            this.lblPluginAPILabel.Name = "lblPluginAPILabel";
            this.lblPluginAPILabel.Size = new System.Drawing.Size(146, 19);
            this.lblPluginAPILabel.TabIndex = 10;
            this.lblPluginAPILabel.Text = "Plugin API version:";
            // 
            // lblWebSocketAPILabel
            // 
            this.lblWebSocketAPILabel.AutoSize = true;
            this.lblWebSocketAPILabel.Location = new System.Drawing.Point(3, 110);
            this.lblWebSocketAPILabel.Name = "lblWebSocketAPILabel";
            this.lblWebSocketAPILabel.Size = new System.Drawing.Size(177, 19);
            this.lblWebSocketAPILabel.TabIndex = 9;
            this.lblWebSocketAPILabel.Text = "Websocket API version:";
            // 
            // lblOS
            // 
            this.lblOS.AutoSize = true;
            this.lblOS.Location = new System.Drawing.Point(241, 186);
            this.lblOS.Name = "lblOS";
            this.lblOS.Size = new System.Drawing.Size(25, 19);
            this.lblOS.TabIndex = 8;
            this.lblOS.Text = "os";
            // 
            // lblInstalledPlugins
            // 
            this.lblInstalledPlugins.AutoSize = true;
            this.lblInstalledPlugins.Location = new System.Drawing.Point(241, 161);
            this.lblInstalledPlugins.Name = "lblInstalledPlugins";
            this.lblInstalledPlugins.Size = new System.Drawing.Size(18, 19);
            this.lblInstalledPlugins.TabIndex = 7;
            this.lblInstalledPlugins.Text = "0";
            // 
            // lblVersion
            // 
            this.lblVersion.AutoSize = true;
            this.lblVersion.Location = new System.Drawing.Point(241, 85);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(46, 19);
            this.lblVersion.TabIndex = 6;
            this.lblVersion.Text = "2.0.0";
            // 
            // lblOSLabel
            // 
            this.lblOSLabel.AutoSize = true;
            this.lblOSLabel.Location = new System.Drawing.Point(3, 186);
            this.lblOSLabel.Name = "lblOSLabel";
            this.lblOSLabel.Size = new System.Drawing.Size(36, 19);
            this.lblOSLabel.TabIndex = 5;
            this.lblOSLabel.Text = "OS:";
            // 
            // lblInstalledPluginsLabel
            // 
            this.lblInstalledPluginsLabel.AutoSize = true;
            this.lblInstalledPluginsLabel.Location = new System.Drawing.Point(3, 161);
            this.lblInstalledPluginsLabel.Name = "lblInstalledPluginsLabel";
            this.lblInstalledPluginsLabel.Size = new System.Drawing.Size(131, 19);
            this.lblInstalledPluginsLabel.TabIndex = 4;
            this.lblInstalledPluginsLabel.Text = "Installed plugins:";
            // 
            // lblVersionLabel
            // 
            this.lblVersionLabel.AutoSize = true;
            this.lblVersionLabel.Location = new System.Drawing.Point(3, 85);
            this.lblVersionLabel.Name = "lblVersionLabel";
            this.lblVersionLabel.Size = new System.Drawing.Size(68, 19);
            this.lblVersionLabel.TabIndex = 3;
            this.lblVersionLabel.Text = "Version:";
            // 
            // lblDeveloped
            // 
            this.lblDeveloped.AutoSize = true;
            this.lblDeveloped.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDeveloped.Location = new System.Drawing.Point(3, 481);
            this.lblDeveloped.Name = "lblDeveloped";
            this.lblDeveloped.Size = new System.Drawing.Size(352, 18);
            this.lblDeveloped.TabIndex = 2;
            this.lblDeveloped.Text = "Developed by Manuel Mayer (SuchByte) in Germany";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Tahoma", 36F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label2.ForeColor = System.Drawing.Color.LightGray;
            this.label2.Location = new System.Drawing.Point(3, 3);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(271, 58);
            this.label2.TabIndex = 1;
            this.label2.Text = "Macro Deck";
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Icon;
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox1.Location = new System.Drawing.Point(770, 3);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(200, 187);
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // SettingsView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.verticalTabControl);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "SettingsView";
            this.Size = new System.Drawing.Size(1137, 540);
            this.Load += new System.EventHandler(this.Settings_Load);
            this.verticalTabControl.ResumeLayout(false);
            this.tabGeneral.ResumeLayout(false);
            this.tabGeneral.PerformLayout();
            this.tabConnection.ResumeLayout(false);
            this.tabConnection.PerformLayout();
            this.groupConnectionInfo.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.port)).EndInit();
            this.tabBackup.ResumeLayout(false);
            this.tabBackup.PerformLayout();
            this.tabUpdater.ResumeLayout(false);
            this.tabUpdater.PerformLayout();
            this.tabAbout.ResumeLayout(false);
            this.tabAbout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private CustomControls.VerticalTabControl verticalTabControl;
        private System.Windows.Forms.TabPage tabGeneral;
        private System.Windows.Forms.TabPage tabUpdater;
        private System.Windows.Forms.TabPage tabAbout;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label lblDeveloped;
        private System.Windows.Forms.Label lblOSLabel;
        private System.Windows.Forms.Label lblInstalledPluginsLabel;
        private System.Windows.Forms.Label lblVersionLabel;
        private System.Windows.Forms.Label lblOS;
        private System.Windows.Forms.Label lblInstalledPlugins;
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.TabPage tabConnection;
        private System.Windows.Forms.Label lblGeneral;
        private System.Windows.Forms.Label lblConnection;
        private System.Windows.Forms.Label lblUpdates;
        private System.Windows.Forms.Label lblBehaviour;
        private System.Windows.Forms.CheckBox checkStartWindows;
        private System.Windows.Forms.Label lblLanguage;
        private System.Windows.Forms.ComboBox language;
        private System.Windows.Forms.ComboBox networkAdapter;
        private System.Windows.Forms.Label lblNetworkAdapter;
        private System.Windows.Forms.Label lblIpAddress;
        private System.Windows.Forms.Label lblIpAddessLabel;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.NumericUpDown port;
        private System.Windows.Forms.GroupBox groupConnectionInfo;
        private System.Windows.Forms.Label lblConnectionInfo;
        private CustomControls.ButtonPrimary btnChangePort;
        private CustomControls.ButtonPrimary btnCheckUpdates;
        private System.Windows.Forms.Label lblInstalledVersion;
        private System.Windows.Forms.Label lblInstalledVersionLabel;
        private System.Windows.Forms.ComboBox updateChannel;
        private System.Windows.Forms.Label lblUpdateChannelLabel;
        private System.Windows.Forms.CheckBox checkAutoUpdate;
        private System.Windows.Forms.ProgressBar progressCheckUpdates;
        private System.Windows.Forms.TabPage tabBackup;
        private System.Windows.Forms.Label label19;
        private System.Windows.Forms.Label lblBackup;
        private System.Windows.Forms.Label lblPluginAPIVersion;
        private System.Windows.Forms.Label lblWebsocketAPIVersion;
        private System.Windows.Forms.Label lblPluginAPILabel;
        private System.Windows.Forms.Label lblWebSocketAPILabel;
        private CustomControls.ButtonPrimary btnLicenses;
        private System.Windows.Forms.Label lblTranslationBy;
    }
}
