using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    partial class SettingsView
    {
        /// <summary> 
        /// Erforderliche Designervariable.
        /// </summary>
        private IContainer components = null;

        /// <summary> 
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            Updater.Updater.OnUpdateAvailable -= UpdateAvailable;
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
            this.components = new Container();
            ComponentResourceManager resources = new ComponentResourceManager(typeof(SettingsView));
            this.verticalTabControl = new VerticalTabControl();
            this.tabGeneral = new TabPage();
            this.checkIconCache = new CheckBox();
            this.language = new RoundedComboBox();
            this.lblLanguage = new Label();
            this.checkStartWindows = new CheckBox();
            this.lblBehaviour = new Label();
            this.lblGeneral = new Label();
            this.tabConnection = new TabPage();
            this.btnChangePort = new ButtonPrimary();
            this.groupConnectionInfo = new GroupBox();
            this.lblConnectionInfo = new Label();
            this.port = new NumericUpDown();
            this.lblPort = new Label();
            this.lblIpAddessLabel = new Label();
            this.lblIpAddress = new Label();
            this.networkAdapter = new RoundedComboBox();
            this.lblNetworkAdapter = new Label();
            this.lblConnection = new Label();
            this.tabUpdater = new TabPage();
            this.checkAutoUpdate = new CheckBox();
            this.checkInstallBetaVersions = new CheckBox();
            this.updaterPanel = new Panel();
            this.btnCheckUpdates = new ButtonPrimary();
            this.lblInstalledVersion = new Label();
            this.lblInstalledVersionLabel = new Label();
            this.lblUpdates = new Label();
            this.tabBackups = new TabPage();
            this.btnCreateBackup = new ButtonPrimary();
            this.backupsPanel = new FlowLayoutPanel();
            this.lblBackups = new Label();
            this.tabAbout = new TabPage();
            this.btnGitHub = new PictureButton();
            this.lblBuild = new Label();
            this.lblBuildLabel = new Label();
            this.label1 = new Label();
            this.lblTranslationBy = new Label();
            this.btnLicenses = new ButtonPrimary();
            this.lblPluginAPIVersion = new Label();
            this.lblWebsocketAPIVersion = new Label();
            this.lblPluginAPILabel = new Label();
            this.lblWebSocketAPILabel = new Label();
            this.lblInstalledPlugins = new Label();
            this.lblInstalledPluginsLabel = new Label();
            this.lblDeveloped = new Label();
            this.lblMacroDeck = new Label();
            this.pictureBox1 = new PictureBox();
            this.tabIcons = new ImageList(this.components);
            this.verticalTabControl.SuspendLayout();
            this.tabGeneral.SuspendLayout();
            this.tabConnection.SuspendLayout();
            this.groupConnectionInfo.SuspendLayout();
            ((ISupportInitialize)(this.port)).BeginInit();
            this.tabUpdater.SuspendLayout();
            this.tabBackups.SuspendLayout();
            this.tabAbout.SuspendLayout();
            ((ISupportInitialize)(this.btnGitHub)).BeginInit();
            ((ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // verticalTabControl
            // 
            this.verticalTabControl.Alignment = TabAlignment.Left;
            this.verticalTabControl.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                               | AnchorStyles.Left) 
                                                              | AnchorStyles.Right)));
            this.verticalTabControl.Controls.Add(this.tabGeneral);
            this.verticalTabControl.Controls.Add(this.tabConnection);
            this.verticalTabControl.Controls.Add(this.tabUpdater);
            this.verticalTabControl.Controls.Add(this.tabBackups);
            this.verticalTabControl.Controls.Add(this.tabAbout);
            this.verticalTabControl.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.verticalTabControl.ImageList = this.tabIcons;
            this.verticalTabControl.ItemSize = new Size(44, 200);
            this.verticalTabControl.Location = new Point(3, 3);
            this.verticalTabControl.Multiline = true;
            this.verticalTabControl.Name = "verticalTabControl";
            this.verticalTabControl.SelectedIndex = 0;
            this.verticalTabControl.SelectedTabColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.verticalTabControl.Size = new Size(1131, 534);
            this.verticalTabControl.SizeMode = TabSizeMode.Fixed;
            this.verticalTabControl.TabIndex = 12;
            // 
            // tabGeneral
            // 
            this.tabGeneral.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabGeneral.Controls.Add(this.checkIconCache);
            this.tabGeneral.Controls.Add(this.language);
            this.tabGeneral.Controls.Add(this.lblLanguage);
            this.tabGeneral.Controls.Add(this.checkStartWindows);
            this.tabGeneral.Controls.Add(this.lblBehaviour);
            this.tabGeneral.Controls.Add(this.lblGeneral);
            this.tabGeneral.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.tabGeneral.ForeColor = Color.White;
            this.tabGeneral.Location = new Point(204, 4);
            this.tabGeneral.Name = "tabGeneral";
            this.tabGeneral.Padding = new Padding(3);
            this.tabGeneral.Size = new Size(923, 526);
            this.tabGeneral.TabIndex = 0;
            this.tabGeneral.Text = "General";
            // 
            // checkIconCache
            // 
            this.checkIconCache.AutoSize = true;
            this.checkIconCache.Location = new Point(13, 112);
            this.checkIconCache.Name = "checkIconCache";
            this.checkIconCache.Size = new Size(375, 23);
            this.checkIconCache.TabIndex = 14;
            this.checkIconCache.Text = "Enable icon cache (faster; higher memory usage)";
            this.checkIconCache.UseMnemonic = false;
            this.checkIconCache.UseVisualStyleBackColor = true;
            this.checkIconCache.CheckedChanged += new EventHandler(this.CheckIconCache_CheckedChanged);
            // 
            // language
            // 
            this.language.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.language.Cursor = Cursors.Hand;
            this.language.DropDownStyle = ComboBoxStyle.DropDownList;
            this.language.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.language.ForeColor = Color.White;
            this.language.Icon = null;
            this.language.Location = new Point(12, 222);
            this.language.Name = "language";
            this.language.Padding = new Padding(8, 2, 8, 2);
            this.language.SelectedIndex = -1;
            this.language.SelectedItem = null;
            this.language.Size = new Size(253, 31);
            this.language.TabIndex = 4;
            this.language.SelectedIndexChanged += new EventHandler(this.Language_SelectedIndexChanged);
            // 
            // lblLanguage
            // 
            this.lblLanguage.AutoSize = true;
            this.lblLanguage.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblLanguage.ForeColor = Color.Gray;
            this.lblLanguage.Location = new Point(3, 198);
            this.lblLanguage.Name = "lblLanguage";
            this.lblLanguage.Size = new Size(93, 23);
            this.lblLanguage.TabIndex = 3;
            this.lblLanguage.Text = "Language";
            this.lblLanguage.UseMnemonic = false;
            // 
            // checkStartWindows
            // 
            this.checkStartWindows.AutoSize = true;
            this.checkStartWindows.Location = new Point(13, 89);
            this.checkStartWindows.Name = "checkStartWindows";
            this.checkStartWindows.Size = new Size(165, 23);
            this.checkStartWindows.TabIndex = 2;
            this.checkStartWindows.Text = "Start with Windows";
            this.checkStartWindows.UseMnemonic = false;
            this.checkStartWindows.UseVisualStyleBackColor = true;
            this.checkStartWindows.CheckedChanged += new EventHandler(this.CheckStartWindows_CheckedChanged);
            // 
            // lblBehaviour
            // 
            this.lblBehaviour.AutoSize = true;
            this.lblBehaviour.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblBehaviour.ForeColor = Color.Gray;
            this.lblBehaviour.Location = new Point(3, 63);
            this.lblBehaviour.Name = "lblBehaviour";
            this.lblBehaviour.Size = new Size(82, 23);
            this.lblBehaviour.TabIndex = 1;
            this.lblBehaviour.Text = "Behavior";
            this.lblBehaviour.UseMnemonic = false;
            // 
            // lblGeneral
            // 
            this.lblGeneral.AutoSize = true;
            this.lblGeneral.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblGeneral.Location = new Point(3, 0);
            this.lblGeneral.Name = "lblGeneral";
            this.lblGeneral.Size = new Size(84, 25);
            this.lblGeneral.TabIndex = 0;
            this.lblGeneral.Text = "General";
            this.lblGeneral.UseMnemonic = false;
            // 
            // tabConnection
            // 
            this.tabConnection.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabConnection.Controls.Add(this.btnChangePort);
            this.tabConnection.Controls.Add(this.groupConnectionInfo);
            this.tabConnection.Controls.Add(this.port);
            this.tabConnection.Controls.Add(this.lblPort);
            this.tabConnection.Controls.Add(this.lblIpAddessLabel);
            this.tabConnection.Controls.Add(this.lblIpAddress);
            this.tabConnection.Controls.Add(this.networkAdapter);
            this.tabConnection.Controls.Add(this.lblNetworkAdapter);
            this.tabConnection.Controls.Add(this.lblConnection);
            this.tabConnection.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.tabConnection.ForeColor = Color.White;
            this.tabConnection.Location = new Point(204, 4);
            this.tabConnection.Name = "tabConnection";
            this.tabConnection.Size = new Size(923, 526);
            this.tabConnection.TabIndex = 3;
            this.tabConnection.Text = "Connection";
            // 
            // btnChangePort
            // 
            this.btnChangePort.BorderRadius = 8;
            this.btnChangePort.Cursor = Cursors.Hand;
            this.btnChangePort.FlatAppearance.BorderSize = 0;
            this.btnChangePort.FlatStyle = FlatStyle.Flat;
            this.btnChangePort.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnChangePort.ForeColor = Color.White;
            this.btnChangePort.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnChangePort.Icon = null;
            this.btnChangePort.Location = new Point(126, 89);
            this.btnChangePort.Name = "btnChangePort";
            this.btnChangePort.Progress = 0;
            this.btnChangePort.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnChangePort.Size = new Size(46, 24);
            this.btnChangePort.TabIndex = 12;
            this.btnChangePort.Text = "Ok";
            this.btnChangePort.UseMnemonic = false;
            this.btnChangePort.UseVisualStyleBackColor = false;
            this.btnChangePort.UseWindowsAccentColor = true;
            this.btnChangePort.Click += new EventHandler(this.BtnChangePort_Click);
            // 
            // groupConnectionInfo
            // 
            this.groupConnectionInfo.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                                | AnchorStyles.Left) 
                                                               | AnchorStyles.Right)));
            this.groupConnectionInfo.Controls.Add(this.lblConnectionInfo);
            this.groupConnectionInfo.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.groupConnectionInfo.ForeColor = Color.White;
            this.groupConnectionInfo.Location = new Point(12, 342);
            this.groupConnectionInfo.Name = "groupConnectionInfo";
            this.groupConnectionInfo.Size = new Size(896, 173);
            this.groupConnectionInfo.TabIndex = 11;
            this.groupConnectionInfo.TabStop = false;
            this.groupConnectionInfo.Text = "Info";
            // 
            // lblConnectionInfo
            // 
            this.lblConnectionInfo.Dock = DockStyle.Fill;
            this.lblConnectionInfo.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblConnectionInfo.Location = new Point(3, 26);
            this.lblConnectionInfo.Name = "lblConnectionInfo";
            this.lblConnectionInfo.Size = new Size(890, 144);
            this.lblConnectionInfo.TabIndex = 0;
            this.lblConnectionInfo.Text = resources.GetString("lblConnectionInfo.Text");
            this.lblConnectionInfo.UseMnemonic = false;
            // 
            // port
            // 
            this.port.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.port.BorderStyle = BorderStyle.FixedSingle;
            this.port.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.port.ForeColor = Color.White;
            this.port.Location = new Point(13, 89);
            this.port.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.port.Name = "port";
            this.port.Size = new Size(107, 26);
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
            this.lblPort.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblPort.ForeColor = Color.Gray;
            this.lblPort.Location = new Point(3, 63);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new Size(43, 23);
            this.lblPort.TabIndex = 8;
            this.lblPort.Text = "Port";
            this.lblPort.UseMnemonic = false;
            // 
            // lblIpAddessLabel
            // 
            this.lblIpAddessLabel.AutoSize = true;
            this.lblIpAddessLabel.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblIpAddessLabel.Location = new Point(18, 280);
            this.lblIpAddessLabel.Name = "lblIpAddessLabel";
            this.lblIpAddessLabel.Size = new Size(83, 18);
            this.lblIpAddessLabel.TabIndex = 7;
            this.lblIpAddessLabel.Text = "IP address:";
            this.lblIpAddessLabel.UseMnemonic = false;
            this.lblIpAddessLabel.Visible = false;
            // 
            // lblIpAddress
            // 
            this.lblIpAddress.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblIpAddress.Location = new Point(107, 280);
            this.lblIpAddress.Name = "lblIpAddress";
            this.lblIpAddress.Size = new Size(187, 17);
            this.lblIpAddress.TabIndex = 6;
            this.lblIpAddress.Text = "0.0.0.0";
            this.lblIpAddress.UseMnemonic = false;
            this.lblIpAddress.Visible = false;
            // 
            // networkAdapter
            // 
            this.networkAdapter.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.networkAdapter.Cursor = Cursors.Hand;
            this.networkAdapter.DropDownStyle = ComboBoxStyle.DropDownList;
            this.networkAdapter.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.networkAdapter.ForeColor = Color.White;
            this.networkAdapter.Icon = null;
            this.networkAdapter.Location = new Point(18, 245);
            this.networkAdapter.Margin = new Padding(4);
            this.networkAdapter.Name = "networkAdapter";
            this.networkAdapter.Padding = new Padding(8, 2, 8, 2);
            this.networkAdapter.SelectedIndex = -1;
            this.networkAdapter.SelectedItem = null;
            this.networkAdapter.Size = new Size(276, 31);
            this.networkAdapter.TabIndex = 5;
            this.networkAdapter.Visible = false;
            this.networkAdapter.SelectedIndexChanged += new EventHandler(this.NetworkAdapter_SelectedIndexChanged);
            // 
            // lblNetworkAdapter
            // 
            this.lblNetworkAdapter.AutoSize = true;
            this.lblNetworkAdapter.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblNetworkAdapter.ForeColor = Color.Gray;
            this.lblNetworkAdapter.Location = new Point(9, 221);
            this.lblNetworkAdapter.Name = "lblNetworkAdapter";
            this.lblNetworkAdapter.Size = new Size(150, 23);
            this.lblNetworkAdapter.TabIndex = 2;
            this.lblNetworkAdapter.Text = "Network adapter";
            this.lblNetworkAdapter.UseMnemonic = false;
            this.lblNetworkAdapter.Visible = false;
            // 
            // lblConnection
            // 
            this.lblConnection.AutoSize = true;
            this.lblConnection.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblConnection.Location = new Point(3, 0);
            this.lblConnection.Name = "lblConnection";
            this.lblConnection.Size = new Size(116, 25);
            this.lblConnection.TabIndex = 1;
            this.lblConnection.Text = "Connection";
            this.lblConnection.UseMnemonic = false;
            // 
            // tabUpdater
            // 
            this.tabUpdater.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabUpdater.Controls.Add(this.checkAutoUpdate);
            this.tabUpdater.Controls.Add(this.checkInstallBetaVersions);
            this.tabUpdater.Controls.Add(this.updaterPanel);
            this.tabUpdater.Controls.Add(this.btnCheckUpdates);
            this.tabUpdater.Controls.Add(this.lblInstalledVersion);
            this.tabUpdater.Controls.Add(this.lblInstalledVersionLabel);
            this.tabUpdater.Controls.Add(this.lblUpdates);
            this.tabUpdater.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.tabUpdater.ForeColor = Color.White;
            this.tabUpdater.Location = new Point(204, 4);
            this.tabUpdater.Name = "tabUpdater";
            this.tabUpdater.Size = new Size(923, 526);
            this.tabUpdater.TabIndex = 1;
            this.tabUpdater.Text = "Updates";
            // 
            // checkAutoUpdate
            // 
            this.checkAutoUpdate.AutoSize = true;
            this.checkAutoUpdate.Location = new Point(13, 28);
            this.checkAutoUpdate.Name = "checkAutoUpdate";
            this.checkAutoUpdate.Size = new Size(253, 23);
            this.checkAutoUpdate.TabIndex = 17;
            this.checkAutoUpdate.Text = "Automatically check for updates";
            this.checkAutoUpdate.UseMnemonic = false;
            this.checkAutoUpdate.UseVisualStyleBackColor = true;
            // 
            // checkInstallBetaVersions
            // 
            this.checkInstallBetaVersions.AutoSize = true;
            this.checkInstallBetaVersions.Location = new Point(13, 57);
            this.checkInstallBetaVersions.Name = "checkInstallBetaVersions";
            this.checkInstallBetaVersions.Size = new Size(169, 23);
            this.checkInstallBetaVersions.TabIndex = 16;
            this.checkInstallBetaVersions.Text = "Install Beta versions";
            this.checkInstallBetaVersions.UseMnemonic = false;
            this.checkInstallBetaVersions.UseVisualStyleBackColor = true;
            // 
            // updaterPanel
            // 
            this.updaterPanel.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                         | AnchorStyles.Left) 
                                                        | AnchorStyles.Right)));
            this.updaterPanel.Location = new Point(3, 103);
            this.updaterPanel.Name = "updaterPanel";
            this.updaterPanel.Size = new Size(920, 420);
            this.updaterPanel.TabIndex = 14;
            // 
            // btnCheckUpdates
            // 
            this.btnCheckUpdates.BorderRadius = 8;
            this.btnCheckUpdates.Cursor = Cursors.Hand;
            this.btnCheckUpdates.FlatAppearance.BorderSize = 0;
            this.btnCheckUpdates.FlatStyle = FlatStyle.Flat;
            this.btnCheckUpdates.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnCheckUpdates.ForeColor = Color.White;
            this.btnCheckUpdates.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnCheckUpdates.Icon = null;
            this.btnCheckUpdates.Location = new Point(354, 65);
            this.btnCheckUpdates.Name = "btnCheckUpdates";
            this.btnCheckUpdates.Progress = 0;
            this.btnCheckUpdates.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnCheckUpdates.Size = new Size(215, 32);
            this.btnCheckUpdates.TabIndex = 9;
            this.btnCheckUpdates.Text = "Check for updates now";
            this.btnCheckUpdates.UseMnemonic = false;
            this.btnCheckUpdates.UseVisualStyleBackColor = false;
            this.btnCheckUpdates.UseWindowsAccentColor = true;
            this.btnCheckUpdates.Click += new EventHandler(this.BtnCheckUpdates_Click);
            // 
            // lblInstalledVersion
            // 
            this.lblInstalledVersion.Location = new Point(769, 29);
            this.lblInstalledVersion.Name = "lblInstalledVersion";
            this.lblInstalledVersion.Size = new Size(151, 19);
            this.lblInstalledVersion.TabIndex = 8;
            this.lblInstalledVersion.Text = "2.0.0";
            this.lblInstalledVersion.TextAlign = ContentAlignment.MiddleRight;
            this.lblInstalledVersion.UseMnemonic = false;
            // 
            // lblInstalledVersionLabel
            // 
            this.lblInstalledVersionLabel.Location = new Point(534, 29);
            this.lblInstalledVersionLabel.Name = "lblInstalledVersionLabel";
            this.lblInstalledVersionLabel.Size = new Size(229, 19);
            this.lblInstalledVersionLabel.TabIndex = 7;
            this.lblInstalledVersionLabel.Text = "Installed Version:";
            this.lblInstalledVersionLabel.TextAlign = ContentAlignment.MiddleRight;
            this.lblInstalledVersionLabel.UseMnemonic = false;
            // 
            // lblUpdates
            // 
            this.lblUpdates.AutoSize = true;
            this.lblUpdates.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblUpdates.Location = new Point(3, 0);
            this.lblUpdates.Name = "lblUpdates";
            this.lblUpdates.Size = new Size(88, 25);
            this.lblUpdates.TabIndex = 2;
            this.lblUpdates.Text = "Updates";
            this.lblUpdates.UseMnemonic = false;
            // 
            // tabBackups
            // 
            this.tabBackups.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabBackups.Controls.Add(this.btnCreateBackup);
            this.tabBackups.Controls.Add(this.backupsPanel);
            this.tabBackups.Controls.Add(this.lblBackups);
            this.tabBackups.ForeColor = Color.White;
            this.tabBackups.Location = new Point(204, 4);
            this.tabBackups.Name = "tabBackups";
            this.tabBackups.Size = new Size(923, 526);
            this.tabBackups.TabIndex = 4;
            this.tabBackups.Text = "Backups";
            // 
            // btnCreateBackup
            // 
            this.btnCreateBackup.BorderRadius = 8;
            this.btnCreateBackup.Cursor = Cursors.Hand;
            this.btnCreateBackup.FlatAppearance.BorderSize = 0;
            this.btnCreateBackup.FlatStyle = FlatStyle.Flat;
            this.btnCreateBackup.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnCreateBackup.ForeColor = Color.White;
            this.btnCreateBackup.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnCreateBackup.Icon = null;
            this.btnCreateBackup.Location = new Point(770, 493);
            this.btnCreateBackup.Name = "btnCreateBackup";
            this.btnCreateBackup.Progress = 0;
            this.btnCreateBackup.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnCreateBackup.Size = new Size(150, 30);
            this.btnCreateBackup.TabIndex = 5;
            this.btnCreateBackup.Text = "Create backup";
            this.btnCreateBackup.UseMnemonic = false;
            this.btnCreateBackup.UseVisualStyleBackColor = false;
            this.btnCreateBackup.UseWindowsAccentColor = true;
            this.btnCreateBackup.Click += new EventHandler(this.BtnCreateBackup_Click);
            // 
            // backupsPanel
            // 
            this.backupsPanel.AutoScroll = true;
            this.backupsPanel.Location = new Point(3, 28);
            this.backupsPanel.Name = "backupsPanel";
            this.backupsPanel.Size = new Size(917, 459);
            this.backupsPanel.TabIndex = 4;
            // 
            // lblBackups
            // 
            this.lblBackups.AutoSize = true;
            this.lblBackups.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblBackups.Location = new Point(3, 0);
            this.lblBackups.Name = "lblBackups";
            this.lblBackups.Size = new Size(89, 25);
            this.lblBackups.TabIndex = 3;
            this.lblBackups.Text = "Backups";
            this.lblBackups.UseMnemonic = false;
            // 
            // tabAbout
            // 
            this.tabAbout.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabAbout.Controls.Add(this.btnGitHub);
            this.tabAbout.Controls.Add(this.lblBuild);
            this.tabAbout.Controls.Add(this.lblBuildLabel);
            this.tabAbout.Controls.Add(this.label1);
            this.tabAbout.Controls.Add(this.lblTranslationBy);
            this.tabAbout.Controls.Add(this.btnLicenses);
            this.tabAbout.Controls.Add(this.lblPluginAPIVersion);
            this.tabAbout.Controls.Add(this.lblWebsocketAPIVersion);
            this.tabAbout.Controls.Add(this.lblPluginAPILabel);
            this.tabAbout.Controls.Add(this.lblWebSocketAPILabel);
            this.tabAbout.Controls.Add(this.lblInstalledPlugins);
            this.tabAbout.Controls.Add(this.lblInstalledPluginsLabel);
            this.tabAbout.Controls.Add(this.lblDeveloped);
            this.tabAbout.Controls.Add(this.lblMacroDeck);
            this.tabAbout.Controls.Add(this.pictureBox1);
            this.tabAbout.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.tabAbout.ForeColor = Color.White;
            this.tabAbout.Location = new Point(204, 4);
            this.tabAbout.Name = "tabAbout";
            this.tabAbout.Size = new Size(923, 526);
            this.tabAbout.TabIndex = 2;
            this.tabAbout.Text = "About";
            // 
            // btnGitHub
            // 
            this.btnGitHub.BackColor = Color.Transparent;
            this.btnGitHub.BackgroundImage = Resources.GitHub_Mark_Light;
            this.btnGitHub.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnGitHub.Cursor = Cursors.Hand;
            this.btnGitHub.HoverImage = null;
            this.btnGitHub.Location = new Point(436, 63);
            this.btnGitHub.Name = "btnGitHub";
            this.btnGitHub.Size = new Size(50, 50);
            this.btnGitHub.TabIndex = 18;
            this.btnGitHub.TabStop = false;
            this.btnGitHub.Click += new EventHandler(this.BtnGitHub_Click);
            // 
            // lblBuild
            // 
            this.lblBuild.AutoSize = true;
            this.lblBuild.Location = new Point(564, 380);
            this.lblBuild.Name = "lblBuild";
            this.lblBuild.Size = new Size(27, 19);
            this.lblBuild.TabIndex = 17;
            this.lblBuild.Text = "13";
            this.lblBuild.UseMnemonic = false;
            // 
            // lblBuildLabel
            // 
            this.lblBuildLabel.AutoSize = true;
            this.lblBuildLabel.Location = new Point(332, 380);
            this.lblBuildLabel.Name = "lblBuildLabel";
            this.lblBuildLabel.Size = new Size(44, 19);
            this.lblBuildLabel.TabIndex = 16;
            this.lblBuildLabel.Text = "Build";
            this.lblBuildLabel.UseMnemonic = false;
            // 
            // label1
            // 
            this.label1.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.label1.Location = new Point(219, 226);
            this.label1.Name = "label1";
            this.label1.Size = new Size(485, 18);
            this.label1.TabIndex = 15;
            this.label1.Text = "Licensed under Apache-2.0";
            this.label1.TextAlign = ContentAlignment.MiddleCenter;
            this.label1.UseMnemonic = false;
            // 
            // lblTranslationBy
            // 
            this.lblTranslationBy.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblTranslationBy.Location = new Point(219, 164);
            this.lblTranslationBy.Name = "lblTranslationBy";
            this.lblTranslationBy.Size = new Size(485, 18);
            this.lblTranslationBy.TabIndex = 14;
            this.lblTranslationBy.Text = "English translation by Macro Deck";
            this.lblTranslationBy.TextAlign = ContentAlignment.MiddleCenter;
            this.lblTranslationBy.UseMnemonic = false;
            // 
            // btnLicenses
            // 
            this.btnLicenses.BorderRadius = 8;
            this.btnLicenses.Cursor = Cursors.Hand;
            this.btnLicenses.FlatAppearance.BorderSize = 0;
            this.btnLicenses.FlatStyle = FlatStyle.Flat;
            this.btnLicenses.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnLicenses.ForeColor = Color.White;
            this.btnLicenses.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnLicenses.Icon = null;
            this.btnLicenses.Location = new Point(361, 297);
            this.btnLicenses.Name = "btnLicenses";
            this.btnLicenses.Progress = 0;
            this.btnLicenses.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnLicenses.Size = new Size(200, 27);
            this.btnLicenses.TabIndex = 13;
            this.btnLicenses.Text = "3rd party licenses";
            this.btnLicenses.UseMnemonic = false;
            this.btnLicenses.UseVisualStyleBackColor = false;
            this.btnLicenses.UseWindowsAccentColor = true;
            this.btnLicenses.Click += new EventHandler(this.BtnLicenses_Click);
            // 
            // lblPluginAPIVersion
            // 
            this.lblPluginAPIVersion.AutoSize = true;
            this.lblPluginAPIVersion.Location = new Point(564, 434);
            this.lblPluginAPIVersion.Name = "lblPluginAPIVersion";
            this.lblPluginAPIVersion.Size = new Size(27, 19);
            this.lblPluginAPIVersion.TabIndex = 12;
            this.lblPluginAPIVersion.Text = "20";
            this.lblPluginAPIVersion.UseMnemonic = false;
            // 
            // lblWebsocketAPIVersion
            // 
            this.lblWebsocketAPIVersion.AutoSize = true;
            this.lblWebsocketAPIVersion.Location = new Point(564, 407);
            this.lblWebsocketAPIVersion.Name = "lblWebsocketAPIVersion";
            this.lblWebsocketAPIVersion.Size = new Size(27, 19);
            this.lblWebsocketAPIVersion.TabIndex = 11;
            this.lblWebsocketAPIVersion.Text = "20";
            this.lblWebsocketAPIVersion.UseMnemonic = false;
            // 
            // lblPluginAPILabel
            // 
            this.lblPluginAPILabel.AutoSize = true;
            this.lblPluginAPILabel.Location = new Point(332, 434);
            this.lblPluginAPILabel.Name = "lblPluginAPILabel";
            this.lblPluginAPILabel.Size = new Size(146, 19);
            this.lblPluginAPILabel.TabIndex = 10;
            this.lblPluginAPILabel.Text = "Plugin API version:";
            this.lblPluginAPILabel.UseMnemonic = false;
            // 
            // lblWebSocketAPILabel
            // 
            this.lblWebSocketAPILabel.AutoSize = true;
            this.lblWebSocketAPILabel.Location = new Point(332, 407);
            this.lblWebSocketAPILabel.Name = "lblWebSocketAPILabel";
            this.lblWebSocketAPILabel.Size = new Size(177, 19);
            this.lblWebSocketAPILabel.TabIndex = 9;
            this.lblWebSocketAPILabel.Text = "Websocket API version:";
            this.lblWebSocketAPILabel.UseMnemonic = false;
            // 
            // lblInstalledPlugins
            // 
            this.lblInstalledPlugins.AutoSize = true;
            this.lblInstalledPlugins.Location = new Point(564, 461);
            this.lblInstalledPlugins.Name = "lblInstalledPlugins";
            this.lblInstalledPlugins.Size = new Size(18, 19);
            this.lblInstalledPlugins.TabIndex = 7;
            this.lblInstalledPlugins.Text = "0";
            this.lblInstalledPlugins.UseMnemonic = false;
            // 
            // lblInstalledPluginsLabel
            // 
            this.lblInstalledPluginsLabel.AutoSize = true;
            this.lblInstalledPluginsLabel.Location = new Point(332, 461);
            this.lblInstalledPluginsLabel.Name = "lblInstalledPluginsLabel";
            this.lblInstalledPluginsLabel.Size = new Size(131, 19);
            this.lblInstalledPluginsLabel.TabIndex = 4;
            this.lblInstalledPluginsLabel.Text = "Installed plugins:";
            this.lblInstalledPluginsLabel.UseMnemonic = false;
            // 
            // lblDeveloped
            // 
            this.lblDeveloped.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblDeveloped.Location = new Point(219, 206);
            this.lblDeveloped.Name = "lblDeveloped";
            this.lblDeveloped.Size = new Size(485, 18);
            this.lblDeveloped.TabIndex = 2;
            this.lblDeveloped.Text = "Developed by Manuel Mayer (SuchByte) in Germany";
            this.lblDeveloped.TextAlign = ContentAlignment.MiddleCenter;
            this.lblDeveloped.UseMnemonic = false;
            // 
            // lblMacroDeck
            // 
            this.lblMacroDeck.Font = new Font("Tahoma", 21.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblMacroDeck.ForeColor = Color.LightGray;
            this.lblMacroDeck.Location = new Point(3, 3);
            this.lblMacroDeck.Name = "lblMacroDeck";
            this.lblMacroDeck.Size = new Size(917, 41);
            this.lblMacroDeck.TabIndex = 1;
            this.lblMacroDeck.Text = "Macro Deck";
            this.lblMacroDeck.TextAlign = ContentAlignment.MiddleCenter;
            this.lblMacroDeck.UseMnemonic = false;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImage = Resources.Icon;
            this.pictureBox1.BackgroundImageLayout = ImageLayout.Stretch;
            this.pictureBox1.Location = new Point(3, 47);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new Size(200, 187);
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // tabIcons
            // 
            this.tabIcons.ColorDepth = ColorDepth.Depth4Bit;
            this.tabIcons.ImageStream = ((ImageListStreamer)(resources.GetObject("tabIcons.ImageStream")));
            this.tabIcons.TransparentColor = Color.Transparent;
            this.tabIcons.Images.SetKeyName(0, "Cog.png");
            this.tabIcons.Images.SetKeyName(1, "Ethernet.png");
            this.tabIcons.Images.SetKeyName(2, "Update.png");
            this.tabIcons.Images.SetKeyName(3, "Backup-Restore.png");
            this.tabIcons.Images.SetKeyName(4, "Informationpng.png");
            // 
            // SettingsView
            // 
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.verticalTabControl);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "SettingsView";
            this.Size = new Size(1137, 540);
            this.Load += new EventHandler(this.Settings_Load);
            this.verticalTabControl.ResumeLayout(false);
            this.tabGeneral.ResumeLayout(false);
            this.tabGeneral.PerformLayout();
            this.tabConnection.ResumeLayout(false);
            this.tabConnection.PerformLayout();
            this.groupConnectionInfo.ResumeLayout(false);
            ((ISupportInitialize)(this.port)).EndInit();
            this.tabUpdater.ResumeLayout(false);
            this.tabUpdater.PerformLayout();
            this.tabBackups.ResumeLayout(false);
            this.tabBackups.PerformLayout();
            this.tabAbout.ResumeLayout(false);
            this.tabAbout.PerformLayout();
            ((ISupportInitialize)(this.btnGitHub)).EndInit();
            ((ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private VerticalTabControl verticalTabControl;
        private TabPage tabGeneral;
        private TabPage tabUpdater;
        private TabPage tabAbout;
        private PictureBox pictureBox1;
        private Label lblMacroDeck;
        private Label lblDeveloped;
        private Label lblInstalledPluginsLabel;
        private Label lblInstalledPlugins;
        private TabPage tabConnection;
        private Label lblGeneral;
        private Label lblConnection;
        private Label lblUpdates;
        private Label lblBehaviour;
        private CheckBox checkStartWindows;
        private Label lblLanguage;
        private RoundedComboBox language;
        private RoundedComboBox networkAdapter;
        private Label lblNetworkAdapter;
        private Label lblIpAddress;
        private Label lblIpAddessLabel;
        private Label lblPort;
        private NumericUpDown port;
        private GroupBox groupConnectionInfo;
        private Label lblConnectionInfo;
        private ButtonPrimary btnChangePort;
        private ButtonPrimary btnCheckUpdates;
        private Label lblInstalledVersion;
        private Label lblInstalledVersionLabel;
        private Label lblPluginAPIVersion;
        private Label lblWebsocketAPIVersion;
        private Label lblPluginAPILabel;
        private Label lblWebSocketAPILabel;
        private ButtonPrimary btnLicenses;
        private Label lblTranslationBy;
        private Panel updaterPanel;
        private CheckBox checkIconCache;
        private ImageList tabIcons;
        private Label label1;
        private CheckBox checkInstallBetaVersions;
        private CheckBox checkAutoUpdate;
        private TabPage tabBackups;
        private Label lblBackups;
        private FlowLayoutPanel backupsPanel;
        private ButtonPrimary btnCreateBackup;
        private Label lblBuild;
        private Label lblBuildLabel;
        private PictureButton btnGitHub;
    }
}
