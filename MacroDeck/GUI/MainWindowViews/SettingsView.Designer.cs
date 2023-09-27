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
            components = new Container();
            ComponentResourceManager resources = new ComponentResourceManager(typeof(SettingsView));
            verticalTabControl = new VerticalTabControl();
            tabGeneral = new TabPage();
            checkIconCache = new CheckBox();
            language = new RoundedComboBox();
            lblLanguage = new Label();
            checkStartWindows = new CheckBox();
            lblBehaviour = new Label();
            lblGeneral = new Label();
            tabConnection = new TabPage();
            btnChangePort = new ButtonPrimary();
            groupConnectionInfo = new GroupBox();
            lblConnectionInfo = new Label();
            port = new NumericUpDown();
            lblPort = new Label();
            lblIpAddessLabel = new Label();
            lblIpAddress = new Label();
            networkAdapter = new RoundedComboBox();
            lblNetworkAdapter = new Label();
            lblConnection = new Label();
            tabUpdater = new TabPage();
            label2 = new Label();
            checkAutoUpdate = new CheckBox();
            checkInstallBetaVersions = new CheckBox();
            btnCheckUpdates = new ButtonPrimary();
            lblInstalledVersion = new Label();
            lblInstalledVersionLabel = new Label();
            lblUpdates = new Label();
            tabBackups = new TabPage();
            btnCreateBackup = new ButtonPrimary();
            backupsPanel = new FlowLayoutPanel();
            lblBackups = new Label();
            tabAbout = new TabPage();
            btnGitHub = new PictureButton();
            label1 = new Label();
            lblTranslationBy = new Label();
            btnLicenses = new ButtonPrimary();
            lblPluginAPIVersion = new Label();
            lblWebsocketAPIVersion = new Label();
            lblPluginAPILabel = new Label();
            lblWebSocketAPILabel = new Label();
            lblInstalledPlugins = new Label();
            lblInstalledPluginsLabel = new Label();
            lblDeveloped = new Label();
            lblMacroDeck = new Label();
            pictureBox1 = new PictureBox();
            tabIcons = new ImageList(components);
            verticalTabControl.SuspendLayout();
            tabGeneral.SuspendLayout();
            tabConnection.SuspendLayout();
            groupConnectionInfo.SuspendLayout();
            ((ISupportInitialize)port).BeginInit();
            tabUpdater.SuspendLayout();
            tabBackups.SuspendLayout();
            tabAbout.SuspendLayout();
            ((ISupportInitialize)btnGitHub).BeginInit();
            ((ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // verticalTabControl
            // 
            verticalTabControl.Alignment = TabAlignment.Left;
            verticalTabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            verticalTabControl.Controls.Add(tabGeneral);
            verticalTabControl.Controls.Add(tabConnection);
            verticalTabControl.Controls.Add(tabUpdater);
            verticalTabControl.Controls.Add(tabBackups);
            verticalTabControl.Controls.Add(tabAbout);
            verticalTabControl.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            verticalTabControl.ImageList = tabIcons;
            verticalTabControl.ItemSize = new Size(44, 200);
            verticalTabControl.Location = new Point(3, 3);
            verticalTabControl.Multiline = true;
            verticalTabControl.Name = "verticalTabControl";
            verticalTabControl.SelectedIndex = 0;
            verticalTabControl.SelectedTabColor = Color.FromArgb(0, 103, 225);
            verticalTabControl.Size = new Size(1131, 534);
            verticalTabControl.SizeMode = TabSizeMode.Fixed;
            verticalTabControl.TabIndex = 12;
            // 
            // tabGeneral
            // 
            tabGeneral.BackColor = Color.FromArgb(45, 45, 45);
            tabGeneral.Controls.Add(checkIconCache);
            tabGeneral.Controls.Add(language);
            tabGeneral.Controls.Add(lblLanguage);
            tabGeneral.Controls.Add(checkStartWindows);
            tabGeneral.Controls.Add(lblBehaviour);
            tabGeneral.Controls.Add(lblGeneral);
            tabGeneral.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            tabGeneral.ForeColor = Color.White;
            tabGeneral.Location = new Point(204, 4);
            tabGeneral.Name = "tabGeneral";
            tabGeneral.Padding = new Padding(3);
            tabGeneral.Size = new Size(923, 526);
            tabGeneral.TabIndex = 0;
            tabGeneral.Text = "General";
            // 
            // checkIconCache
            // 
            checkIconCache.AutoSize = true;
            checkIconCache.Location = new Point(13, 112);
            checkIconCache.Name = "checkIconCache";
            checkIconCache.Size = new Size(375, 23);
            checkIconCache.TabIndex = 14;
            checkIconCache.Text = "Enable icon cache (faster; higher memory usage)";
            checkIconCache.UseMnemonic = false;
            checkIconCache.UseVisualStyleBackColor = true;
            checkIconCache.CheckedChanged += CheckIconCache_CheckedChanged;
            // 
            // language
            // 
            language.BackColor = Color.FromArgb(65, 65, 65);
            language.Cursor = Cursors.Hand;
            language.DropDownStyle = ComboBoxStyle.DropDownList;
            language.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            language.ForeColor = Color.White;
            language.Icon = null;
            language.Location = new Point(12, 222);
            language.Name = "language";
            language.Padding = new Padding(8, 2, 8, 2);
            language.SelectedIndex = -1;
            language.SelectedItem = null;
            language.Size = new Size(253, 31);
            language.TabIndex = 4;
            language.SelectedIndexChanged += Language_SelectedIndexChanged;
            // 
            // lblLanguage
            // 
            lblLanguage.AutoSize = true;
            lblLanguage.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblLanguage.ForeColor = Color.Gray;
            lblLanguage.Location = new Point(3, 198);
            lblLanguage.Name = "lblLanguage";
            lblLanguage.Size = new Size(93, 23);
            lblLanguage.TabIndex = 3;
            lblLanguage.Text = "Language";
            lblLanguage.UseMnemonic = false;
            // 
            // checkStartWindows
            // 
            checkStartWindows.AutoSize = true;
            checkStartWindows.Location = new Point(13, 89);
            checkStartWindows.Name = "checkStartWindows";
            checkStartWindows.Size = new Size(165, 23);
            checkStartWindows.TabIndex = 2;
            checkStartWindows.Text = "Start with Windows";
            checkStartWindows.UseMnemonic = false;
            checkStartWindows.UseVisualStyleBackColor = true;
            checkStartWindows.CheckedChanged += CheckStartWindows_CheckedChanged;
            // 
            // lblBehaviour
            // 
            lblBehaviour.AutoSize = true;
            lblBehaviour.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblBehaviour.ForeColor = Color.Gray;
            lblBehaviour.Location = new Point(3, 63);
            lblBehaviour.Name = "lblBehaviour";
            lblBehaviour.Size = new Size(82, 23);
            lblBehaviour.TabIndex = 1;
            lblBehaviour.Text = "Behavior";
            lblBehaviour.UseMnemonic = false;
            // 
            // lblGeneral
            // 
            lblGeneral.AutoSize = true;
            lblGeneral.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            lblGeneral.Location = new Point(3, 0);
            lblGeneral.Name = "lblGeneral";
            lblGeneral.Size = new Size(84, 25);
            lblGeneral.TabIndex = 0;
            lblGeneral.Text = "General";
            lblGeneral.UseMnemonic = false;
            // 
            // tabConnection
            // 
            tabConnection.BackColor = Color.FromArgb(45, 45, 45);
            tabConnection.Controls.Add(btnChangePort);
            tabConnection.Controls.Add(groupConnectionInfo);
            tabConnection.Controls.Add(port);
            tabConnection.Controls.Add(lblPort);
            tabConnection.Controls.Add(lblIpAddessLabel);
            tabConnection.Controls.Add(lblIpAddress);
            tabConnection.Controls.Add(networkAdapter);
            tabConnection.Controls.Add(lblNetworkAdapter);
            tabConnection.Controls.Add(lblConnection);
            tabConnection.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            tabConnection.ForeColor = Color.White;
            tabConnection.Location = new Point(204, 4);
            tabConnection.Name = "tabConnection";
            tabConnection.Size = new Size(923, 526);
            tabConnection.TabIndex = 3;
            tabConnection.Text = "Connection";
            // 
            // btnChangePort
            // 
            btnChangePort.BorderRadius = 8;
            btnChangePort.Cursor = Cursors.Hand;
            btnChangePort.FlatAppearance.BorderSize = 0;
            btnChangePort.FlatStyle = FlatStyle.Flat;
            btnChangePort.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnChangePort.ForeColor = Color.White;
            btnChangePort.HoverColor = Color.FromArgb(0, 89, 184);
            btnChangePort.Icon = null;
            btnChangePort.Location = new Point(126, 89);
            btnChangePort.Name = "btnChangePort";
            btnChangePort.Progress = 0;
            btnChangePort.ProgressColor = Color.FromArgb(0, 46, 94);
            btnChangePort.Size = new Size(46, 24);
            btnChangePort.TabIndex = 12;
            btnChangePort.Text = "Ok";
            btnChangePort.UseMnemonic = false;
            btnChangePort.UseVisualStyleBackColor = false;
            btnChangePort.UseWindowsAccentColor = true;
            btnChangePort.WriteProgress = true;
            btnChangePort.Click += BtnChangePort_Click;
            // 
            // groupConnectionInfo
            // 
            groupConnectionInfo.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            groupConnectionInfo.Controls.Add(lblConnectionInfo);
            groupConnectionInfo.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            groupConnectionInfo.ForeColor = Color.White;
            groupConnectionInfo.Location = new Point(12, 342);
            groupConnectionInfo.Name = "groupConnectionInfo";
            groupConnectionInfo.Size = new Size(896, 173);
            groupConnectionInfo.TabIndex = 11;
            groupConnectionInfo.TabStop = false;
            groupConnectionInfo.Text = "Info";
            // 
            // lblConnectionInfo
            // 
            lblConnectionInfo.Dock = DockStyle.Fill;
            lblConnectionInfo.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblConnectionInfo.Location = new Point(3, 26);
            lblConnectionInfo.Name = "lblConnectionInfo";
            lblConnectionInfo.Size = new Size(890, 144);
            lblConnectionInfo.TabIndex = 0;
            lblConnectionInfo.Text = resources.GetString("lblConnectionInfo.Text");
            lblConnectionInfo.UseMnemonic = false;
            // 
            // port
            // 
            port.BackColor = Color.FromArgb(65, 65, 65);
            port.BorderStyle = BorderStyle.FixedSingle;
            port.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            port.ForeColor = Color.White;
            port.Location = new Point(13, 89);
            port.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            port.Name = "port";
            port.Size = new Size(107, 26);
            port.TabIndex = 9;
            port.Value = new decimal(new int[] { 8191, 0, 0, 0 });
            // 
            // lblPort
            // 
            lblPort.AutoSize = true;
            lblPort.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblPort.ForeColor = Color.Gray;
            lblPort.Location = new Point(3, 63);
            lblPort.Name = "lblPort";
            lblPort.Size = new Size(43, 23);
            lblPort.TabIndex = 8;
            lblPort.Text = "Port";
            lblPort.UseMnemonic = false;
            // 
            // lblIpAddessLabel
            // 
            lblIpAddessLabel.AutoSize = true;
            lblIpAddessLabel.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblIpAddessLabel.Location = new Point(18, 280);
            lblIpAddessLabel.Name = "lblIpAddessLabel";
            lblIpAddessLabel.Size = new Size(83, 18);
            lblIpAddessLabel.TabIndex = 7;
            lblIpAddessLabel.Text = "IP address:";
            lblIpAddessLabel.UseMnemonic = false;
            lblIpAddessLabel.Visible = false;
            // 
            // lblIpAddress
            // 
            lblIpAddress.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblIpAddress.Location = new Point(107, 280);
            lblIpAddress.Name = "lblIpAddress";
            lblIpAddress.Size = new Size(187, 17);
            lblIpAddress.TabIndex = 6;
            lblIpAddress.Text = "0.0.0.0";
            lblIpAddress.UseMnemonic = false;
            lblIpAddress.Visible = false;
            // 
            // networkAdapter
            // 
            networkAdapter.BackColor = Color.FromArgb(65, 65, 65);
            networkAdapter.Cursor = Cursors.Hand;
            networkAdapter.DropDownStyle = ComboBoxStyle.DropDownList;
            networkAdapter.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            networkAdapter.ForeColor = Color.White;
            networkAdapter.Icon = null;
            networkAdapter.Location = new Point(18, 245);
            networkAdapter.Margin = new Padding(4);
            networkAdapter.Name = "networkAdapter";
            networkAdapter.Padding = new Padding(8, 2, 8, 2);
            networkAdapter.SelectedIndex = -1;
            networkAdapter.SelectedItem = null;
            networkAdapter.Size = new Size(276, 31);
            networkAdapter.TabIndex = 5;
            networkAdapter.Visible = false;
            networkAdapter.SelectedIndexChanged += NetworkAdapter_SelectedIndexChanged;
            // 
            // lblNetworkAdapter
            // 
            lblNetworkAdapter.AutoSize = true;
            lblNetworkAdapter.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblNetworkAdapter.ForeColor = Color.Gray;
            lblNetworkAdapter.Location = new Point(9, 221);
            lblNetworkAdapter.Name = "lblNetworkAdapter";
            lblNetworkAdapter.Size = new Size(150, 23);
            lblNetworkAdapter.TabIndex = 2;
            lblNetworkAdapter.Text = "Network adapter";
            lblNetworkAdapter.UseMnemonic = false;
            lblNetworkAdapter.Visible = false;
            // 
            // lblConnection
            // 
            lblConnection.AutoSize = true;
            lblConnection.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            lblConnection.Location = new Point(3, 0);
            lblConnection.Name = "lblConnection";
            lblConnection.Size = new Size(116, 25);
            lblConnection.TabIndex = 1;
            lblConnection.Text = "Connection";
            lblConnection.UseMnemonic = false;
            // 
            // tabUpdater
            // 
            tabUpdater.BackColor = Color.FromArgb(45, 45, 45);
            tabUpdater.Controls.Add(label2);
            tabUpdater.Controls.Add(checkAutoUpdate);
            tabUpdater.Controls.Add(checkInstallBetaVersions);
            tabUpdater.Controls.Add(btnCheckUpdates);
            tabUpdater.Controls.Add(lblInstalledVersion);
            tabUpdater.Controls.Add(lblInstalledVersionLabel);
            tabUpdater.Controls.Add(lblUpdates);
            tabUpdater.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            tabUpdater.ForeColor = Color.White;
            tabUpdater.Location = new Point(204, 4);
            tabUpdater.Name = "tabUpdater";
            tabUpdater.Size = new Size(923, 526);
            tabUpdater.TabIndex = 1;
            tabUpdater.Text = "Updates";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            label2.ForeColor = Color.Gray;
            label2.Location = new Point(3, 63);
            label2.Name = "label2";
            label2.Size = new Size(82, 23);
            label2.TabIndex = 18;
            label2.Text = "Behavior";
            label2.UseMnemonic = false;
            // 
            // checkAutoUpdate
            // 
            checkAutoUpdate.AutoSize = true;
            checkAutoUpdate.Location = new Point(13, 89);
            checkAutoUpdate.Name = "checkAutoUpdate";
            checkAutoUpdate.Size = new Size(253, 23);
            checkAutoUpdate.TabIndex = 17;
            checkAutoUpdate.Text = "Automatically check for updates";
            checkAutoUpdate.UseMnemonic = false;
            checkAutoUpdate.UseVisualStyleBackColor = true;
            // 
            // checkInstallBetaVersions
            // 
            checkInstallBetaVersions.AutoSize = true;
            checkInstallBetaVersions.Location = new Point(13, 118);
            checkInstallBetaVersions.Name = "checkInstallBetaVersions";
            checkInstallBetaVersions.Size = new Size(169, 23);
            checkInstallBetaVersions.TabIndex = 16;
            checkInstallBetaVersions.Text = "Install Beta versions";
            checkInstallBetaVersions.UseMnemonic = false;
            checkInstallBetaVersions.UseVisualStyleBackColor = true;
            // 
            // btnCheckUpdates
            // 
            btnCheckUpdates.BorderRadius = 8;
            btnCheckUpdates.Cursor = Cursors.Hand;
            btnCheckUpdates.FlatAppearance.BorderSize = 0;
            btnCheckUpdates.FlatStyle = FlatStyle.Flat;
            btnCheckUpdates.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnCheckUpdates.ForeColor = Color.White;
            btnCheckUpdates.HoverColor = Color.FromArgb(0, 89, 184);
            btnCheckUpdates.Icon = null;
            btnCheckUpdates.Location = new Point(354, 265);
            btnCheckUpdates.Name = "btnCheckUpdates";
            btnCheckUpdates.Progress = 0;
            btnCheckUpdates.ProgressColor = Color.FromArgb(0, 46, 94);
            btnCheckUpdates.Size = new Size(215, 32);
            btnCheckUpdates.TabIndex = 9;
            btnCheckUpdates.Text = "Check for updates now";
            btnCheckUpdates.UseMnemonic = false;
            btnCheckUpdates.UseVisualStyleBackColor = false;
            btnCheckUpdates.UseWindowsAccentColor = true;
            btnCheckUpdates.WriteProgress = true;
            btnCheckUpdates.Click += BtnCheckUpdates_Click;
            // 
            // lblInstalledVersion
            // 
            lblInstalledVersion.Location = new Point(501, 243);
            lblInstalledVersion.Name = "lblInstalledVersion";
            lblInstalledVersion.Size = new Size(229, 19);
            lblInstalledVersion.TabIndex = 8;
            lblInstalledVersion.Text = "2.0.0";
            lblInstalledVersion.TextAlign = ContentAlignment.MiddleLeft;
            lblInstalledVersion.UseMnemonic = false;
            // 
            // lblInstalledVersionLabel
            // 
            lblInstalledVersionLabel.Location = new Point(266, 243);
            lblInstalledVersionLabel.Name = "lblInstalledVersionLabel";
            lblInstalledVersionLabel.Size = new Size(229, 19);
            lblInstalledVersionLabel.TabIndex = 7;
            lblInstalledVersionLabel.Text = "Installed Version:";
            lblInstalledVersionLabel.TextAlign = ContentAlignment.MiddleRight;
            lblInstalledVersionLabel.UseMnemonic = false;
            // 
            // lblUpdates
            // 
            lblUpdates.AutoSize = true;
            lblUpdates.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            lblUpdates.Location = new Point(3, 0);
            lblUpdates.Name = "lblUpdates";
            lblUpdates.Size = new Size(88, 25);
            lblUpdates.TabIndex = 2;
            lblUpdates.Text = "Updates";
            lblUpdates.UseMnemonic = false;
            // 
            // tabBackups
            // 
            tabBackups.BackColor = Color.FromArgb(45, 45, 45);
            tabBackups.Controls.Add(btnCreateBackup);
            tabBackups.Controls.Add(backupsPanel);
            tabBackups.Controls.Add(lblBackups);
            tabBackups.ForeColor = Color.White;
            tabBackups.Location = new Point(204, 4);
            tabBackups.Name = "tabBackups";
            tabBackups.Size = new Size(923, 526);
            tabBackups.TabIndex = 4;
            tabBackups.Text = "Backups";
            // 
            // btnCreateBackup
            // 
            btnCreateBackup.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCreateBackup.BorderRadius = 8;
            btnCreateBackup.Cursor = Cursors.Hand;
            btnCreateBackup.FlatAppearance.BorderSize = 0;
            btnCreateBackup.FlatStyle = FlatStyle.Flat;
            btnCreateBackup.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnCreateBackup.ForeColor = Color.White;
            btnCreateBackup.HoverColor = Color.FromArgb(0, 89, 184);
            btnCreateBackup.Icon = null;
            btnCreateBackup.Location = new Point(770, 493);
            btnCreateBackup.Name = "btnCreateBackup";
            btnCreateBackup.Progress = 0;
            btnCreateBackup.ProgressColor = Color.FromArgb(0, 46, 94);
            btnCreateBackup.Size = new Size(150, 30);
            btnCreateBackup.TabIndex = 5;
            btnCreateBackup.Text = "Create backup";
            btnCreateBackup.UseMnemonic = false;
            btnCreateBackup.UseVisualStyleBackColor = false;
            btnCreateBackup.UseWindowsAccentColor = true;
            btnCreateBackup.WriteProgress = true;
            btnCreateBackup.Click += BtnCreateBackup_Click;
            // 
            // backupsPanel
            // 
            backupsPanel.AutoScroll = true;
            backupsPanel.Location = new Point(3, 28);
            backupsPanel.Name = "backupsPanel";
            backupsPanel.Size = new Size(917, 459);
            backupsPanel.TabIndex = 4;
            // 
            // lblBackups
            // 
            lblBackups.AutoSize = true;
            lblBackups.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            lblBackups.Location = new Point(3, 0);
            lblBackups.Name = "lblBackups";
            lblBackups.Size = new Size(89, 25);
            lblBackups.TabIndex = 3;
            lblBackups.Text = "Backups";
            lblBackups.UseMnemonic = false;
            // 
            // tabAbout
            // 
            tabAbout.BackColor = Color.FromArgb(45, 45, 45);
            tabAbout.Controls.Add(btnGitHub);
            tabAbout.Controls.Add(label1);
            tabAbout.Controls.Add(lblTranslationBy);
            tabAbout.Controls.Add(btnLicenses);
            tabAbout.Controls.Add(lblPluginAPIVersion);
            tabAbout.Controls.Add(lblWebsocketAPIVersion);
            tabAbout.Controls.Add(lblPluginAPILabel);
            tabAbout.Controls.Add(lblWebSocketAPILabel);
            tabAbout.Controls.Add(lblInstalledPlugins);
            tabAbout.Controls.Add(lblInstalledPluginsLabel);
            tabAbout.Controls.Add(lblDeveloped);
            tabAbout.Controls.Add(lblMacroDeck);
            tabAbout.Controls.Add(pictureBox1);
            tabAbout.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            tabAbout.ForeColor = Color.White;
            tabAbout.Location = new Point(204, 4);
            tabAbout.Name = "tabAbout";
            tabAbout.Size = new Size(923, 526);
            tabAbout.TabIndex = 2;
            tabAbout.Text = "About";
            // 
            // btnGitHub
            // 
            btnGitHub.BackColor = Color.Transparent;
            btnGitHub.BackgroundImage = Resources.GitHub_Mark_Light;
            btnGitHub.BackgroundImageLayout = ImageLayout.Stretch;
            btnGitHub.Cursor = Cursors.Hand;
            btnGitHub.HoverImage = null;
            btnGitHub.Location = new Point(436, 63);
            btnGitHub.Name = "btnGitHub";
            btnGitHub.Size = new Size(50, 50);
            btnGitHub.TabIndex = 18;
            btnGitHub.TabStop = false;
            btnGitHub.Click += BtnGitHub_Click;
            // 
            // label1
            // 
            label1.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            label1.Location = new Point(219, 226);
            label1.Name = "label1";
            label1.Size = new Size(485, 18);
            label1.TabIndex = 15;
            label1.Text = "Licensed under Apache-2.0";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            label1.UseMnemonic = false;
            // 
            // lblTranslationBy
            // 
            lblTranslationBy.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblTranslationBy.Location = new Point(219, 164);
            lblTranslationBy.Name = "lblTranslationBy";
            lblTranslationBy.Size = new Size(485, 18);
            lblTranslationBy.TabIndex = 14;
            lblTranslationBy.Text = "English translation by Macro Deck";
            lblTranslationBy.TextAlign = ContentAlignment.MiddleCenter;
            lblTranslationBy.UseMnemonic = false;
            // 
            // btnLicenses
            // 
            btnLicenses.BorderRadius = 8;
            btnLicenses.Cursor = Cursors.Hand;
            btnLicenses.FlatAppearance.BorderSize = 0;
            btnLicenses.FlatStyle = FlatStyle.Flat;
            btnLicenses.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnLicenses.ForeColor = Color.White;
            btnLicenses.HoverColor = Color.FromArgb(0, 89, 184);
            btnLicenses.Icon = null;
            btnLicenses.Location = new Point(361, 297);
            btnLicenses.Name = "btnLicenses";
            btnLicenses.Progress = 0;
            btnLicenses.ProgressColor = Color.FromArgb(0, 46, 94);
            btnLicenses.Size = new Size(200, 27);
            btnLicenses.TabIndex = 13;
            btnLicenses.Text = "3rd party licenses";
            btnLicenses.UseMnemonic = false;
            btnLicenses.UseVisualStyleBackColor = false;
            btnLicenses.UseWindowsAccentColor = true;
            btnLicenses.WriteProgress = true;
            btnLicenses.Click += BtnLicenses_Click;
            // 
            // lblPluginAPIVersion
            // 
            lblPluginAPIVersion.AutoSize = true;
            lblPluginAPIVersion.Location = new Point(564, 434);
            lblPluginAPIVersion.Name = "lblPluginAPIVersion";
            lblPluginAPIVersion.Size = new Size(27, 19);
            lblPluginAPIVersion.TabIndex = 12;
            lblPluginAPIVersion.Text = "20";
            lblPluginAPIVersion.UseMnemonic = false;
            // 
            // lblWebsocketAPIVersion
            // 
            lblWebsocketAPIVersion.AutoSize = true;
            lblWebsocketAPIVersion.Location = new Point(564, 407);
            lblWebsocketAPIVersion.Name = "lblWebsocketAPIVersion";
            lblWebsocketAPIVersion.Size = new Size(27, 19);
            lblWebsocketAPIVersion.TabIndex = 11;
            lblWebsocketAPIVersion.Text = "20";
            lblWebsocketAPIVersion.UseMnemonic = false;
            // 
            // lblPluginAPILabel
            // 
            lblPluginAPILabel.AutoSize = true;
            lblPluginAPILabel.Location = new Point(332, 434);
            lblPluginAPILabel.Name = "lblPluginAPILabel";
            lblPluginAPILabel.Size = new Size(146, 19);
            lblPluginAPILabel.TabIndex = 10;
            lblPluginAPILabel.Text = "Plugin API version:";
            lblPluginAPILabel.UseMnemonic = false;
            // 
            // lblWebSocketAPILabel
            // 
            lblWebSocketAPILabel.AutoSize = true;
            lblWebSocketAPILabel.Location = new Point(332, 407);
            lblWebSocketAPILabel.Name = "lblWebSocketAPILabel";
            lblWebSocketAPILabel.Size = new Size(177, 19);
            lblWebSocketAPILabel.TabIndex = 9;
            lblWebSocketAPILabel.Text = "Websocket API version:";
            lblWebSocketAPILabel.UseMnemonic = false;
            // 
            // lblInstalledPlugins
            // 
            lblInstalledPlugins.AutoSize = true;
            lblInstalledPlugins.Location = new Point(564, 461);
            lblInstalledPlugins.Name = "lblInstalledPlugins";
            lblInstalledPlugins.Size = new Size(18, 19);
            lblInstalledPlugins.TabIndex = 7;
            lblInstalledPlugins.Text = "0";
            lblInstalledPlugins.UseMnemonic = false;
            // 
            // lblInstalledPluginsLabel
            // 
            lblInstalledPluginsLabel.AutoSize = true;
            lblInstalledPluginsLabel.Location = new Point(332, 461);
            lblInstalledPluginsLabel.Name = "lblInstalledPluginsLabel";
            lblInstalledPluginsLabel.Size = new Size(131, 19);
            lblInstalledPluginsLabel.TabIndex = 4;
            lblInstalledPluginsLabel.Text = "Installed plugins:";
            lblInstalledPluginsLabel.UseMnemonic = false;
            // 
            // lblDeveloped
            // 
            lblDeveloped.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblDeveloped.Location = new Point(219, 206);
            lblDeveloped.Name = "lblDeveloped";
            lblDeveloped.Size = new Size(485, 18);
            lblDeveloped.TabIndex = 2;
            lblDeveloped.Text = "Developed by Manuel Mayer (SuchByte) in Germany";
            lblDeveloped.TextAlign = ContentAlignment.MiddleCenter;
            lblDeveloped.UseMnemonic = false;
            // 
            // lblMacroDeck
            // 
            lblMacroDeck.Font = new Font("Tahoma", 21.75F, FontStyle.Regular, GraphicsUnit.Point);
            lblMacroDeck.ForeColor = Color.LightGray;
            lblMacroDeck.Location = new Point(3, 3);
            lblMacroDeck.Name = "lblMacroDeck";
            lblMacroDeck.Size = new Size(917, 41);
            lblMacroDeck.TabIndex = 1;
            lblMacroDeck.Text = "Macro Deck";
            lblMacroDeck.TextAlign = ContentAlignment.MiddleCenter;
            lblMacroDeck.UseMnemonic = false;
            // 
            // pictureBox1
            // 
            pictureBox1.BackgroundImage = Resources.Icon;
            pictureBox1.BackgroundImageLayout = ImageLayout.Stretch;
            pictureBox1.Location = new Point(3, 47);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(200, 187);
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // tabIcons
            // 
            tabIcons.ColorDepth = ColorDepth.Depth4Bit;
            tabIcons.ImageStream = (ImageListStreamer)resources.GetObject("tabIcons.ImageStream");
            tabIcons.TransparentColor = Color.Transparent;
            tabIcons.Images.SetKeyName(0, "Cog.png");
            tabIcons.Images.SetKeyName(1, "Ethernet.png");
            tabIcons.Images.SetKeyName(2, "Update.png");
            tabIcons.Images.SetKeyName(3, "Backup-Restore.png");
            tabIcons.Images.SetKeyName(4, "Informationpng.png");
            // 
            // SettingsView
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(45, 45, 45);
            Controls.Add(verticalTabControl);
            Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Name = "SettingsView";
            Size = new Size(1137, 540);
            Load += Settings_Load;
            verticalTabControl.ResumeLayout(false);
            tabGeneral.ResumeLayout(false);
            tabGeneral.PerformLayout();
            tabConnection.ResumeLayout(false);
            tabConnection.PerformLayout();
            groupConnectionInfo.ResumeLayout(false);
            ((ISupportInitialize)port).EndInit();
            tabUpdater.ResumeLayout(false);
            tabUpdater.PerformLayout();
            tabBackups.ResumeLayout(false);
            tabBackups.PerformLayout();
            tabAbout.ResumeLayout(false);
            tabAbout.PerformLayout();
            ((ISupportInitialize)btnGitHub).EndInit();
            ((ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
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
        private CheckBox checkIconCache;
        private ImageList tabIcons;
        private Label label1;
        private CheckBox checkInstallBetaVersions;
        private CheckBox checkAutoUpdate;
        private TabPage tabBackups;
        private Label lblBackups;
        private FlowLayoutPanel backupsPanel;
        private ButtonPrimary btnCreateBackup;
        private PictureButton btnGitHub;
        private Label label2;
    }
}
