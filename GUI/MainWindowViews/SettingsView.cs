using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Backups;
using SuchByte.MacroDeck.GUI.CustomControls.Settings;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            this.Dock = DockStyle.Fill;
            this.UpdateTranslation();
            Updater.Updater.OnUpdateAvailable += UpdateAvailable;
            BackupManager.BackupSaved += BackupManager_BackupSaved;
            BackupManager.BackupFailed += BackupManager_BackupFailed;
            BackupManager.DeleteSuccess += BackupManager_DeleteSuccess;
        }

        private void UpdateAvailable(object sender, EventArgs e)
        {
            this.verticalTabControl.SetNotification(2, Updater.Updater.UpdateAvailable);
        }

        private void UpdateTranslation()
        {
            this.Name = LanguageManager.Strings.SettingsTitle;
            this.tabGeneral.Text = LanguageManager.Strings.General;
            this.tabConnection.Text = LanguageManager.Strings.Connection;
            this.tabUpdater.Text = LanguageManager.Strings.Updates;
            this.tabAbout.Text = LanguageManager.Strings.About;
            this.lblGeneral.Text = LanguageManager.Strings.General;
            this.lblBehaviour.Text = LanguageManager.Strings.Behaviour;
            this.checkStartWindows.Text = LanguageManager.Strings.AutomaticallyStartWithWindows;
            this.checkIconCache.Text = LanguageManager.Strings.EnableIconCache;
            this.lblLanguage.Text = LanguageManager.Strings.Language;
            this.lblConnection.Text = LanguageManager.Strings.Connection;
            this.lblNetworkAdapter.Text = LanguageManager.Strings.NetworkAdapter;
            this.lblIpAddessLabel.Text = LanguageManager.Strings.IPAddress;
            this.lblPort.Text = LanguageManager.Strings.Port;
            this.btnChangePort.Text = LanguageManager.Strings.Ok;
            this.groupConnectionInfo.Text = LanguageManager.Strings.Info;
            this.lblConnectionInfo.Text = LanguageManager.Strings.ConfigureNetworkInfo;
            this.lblUpdates.Text = LanguageManager.Strings.Updates;
            this.checkAutoUpdate.Text = LanguageManager.Strings.AutomaticallyCheckUpdates;
            this.lblInstalledVersionLabel.Text = LanguageManager.Strings.InstalledVersion;
            this.tabBackups.Text = LanguageManager.Strings.Backups;
            this.lblBackups.Text = LanguageManager.Strings.Backups;
            this.btnCreateBackup.Text = LanguageManager.Strings.CreateBackup;
            //this.lblUpdateChannelLabel.Text = LanguageManager.Strings.UpdateChannel;
            this.checkInstallDevVersions.Text = LanguageManager.Strings.InstallDevVersions;
            this.checkInstallBetaVersions.Text = LanguageManager.Strings.InstallBetaVersions;
            this.btnCheckUpdates.Text = LanguageManager.Strings.CheckForUpdatesNow;
            this.lblWebSocketAPILabel.Text = LanguageManager.Strings.WebSocketAPIVersion;
            this.lblPluginAPILabel.Text = LanguageManager.Strings.PluginAPIVersion;
            this.lblInstalledPluginsLabel.Text = LanguageManager.Strings.InstalledPlugins;
            this.lblOSLabel.Text = LanguageManager.Strings.OperatingSystem;
            this.lblTranslationBy.Text = String.Format(LanguageManager.Strings.XTranslationByX, LanguageManager.Strings.__Language__, LanguageManager.Strings.__Author__);
            Updater.Updater.OnUpdateAvailable += OnUpdateAvailable;
        }

        private void Settings_Load(object sender, EventArgs e)
        {
            this.LoadAutoStart();
            this.LoadLanguage();
            this.LoadNetworkAdapters();
            this.LoadUpdateChannel();
            this.LoadAutoUpdate();
            this.LoadIconCache();
            this.LoadBackups();

            this.lblInstalledVersion.Text = MacroDeck.VersionString;
            this.lblWebsocketAPIVersion.Text = MacroDeck.ApiVersion.ToString();
            this.lblPluginAPIVersion.Text = MacroDeck.PluginApiVersion.ToString();
            this.lblMacroDeck.Text = "Macro Deck " + MacroDeck.VersionString;
            this.lblInstalledPlugins.Text = PluginManager.Plugins.Count.ToString();
            this.lblOS.Text = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString() + " " + (Environment.Is64BitOperatingSystem == true ? "64 bit" : "32 bit");

            this.verticalTabControl.SetNotification(2, Updater.Updater.UpdateAvailable);

            if (Updater.Updater.UpdateAvailable)
            {
                this.AddUpdateAvailableControl();
            }

        }

        private void LoadLanguage()
        {
            this.language.SelectedIndexChanged -= Language_SelectedIndexChanged;
            this.language.Items.Clear();
            foreach (Strings strings in LanguageManager.Languages)
            {
                this.language.Items.Add(strings.__Language__);
            }
            this.language.Text = MacroDeck.Configuration.Language;
            this.language.SelectedIndexChanged += Language_SelectedIndexChanged;
        }

        private void LoadAutoUpdate()
        {
            this.checkAutoUpdate.CheckedChanged -= this.CheckAutoUpdate_CheckedChanged;
            this.checkAutoUpdate.Checked = MacroDeck.Configuration.AutoUpdates;
            this.checkAutoUpdate.CheckedChanged += new EventHandler(this.CheckAutoUpdate_CheckedChanged);
        }

        private void LoadAutoStart()
        {
            this.checkStartWindows.CheckedChanged -= this.CheckStartWindows_CheckedChanged;
            this.checkStartWindows.Checked = MacroDeck.Configuration.AutoStart;
            this.checkStartWindows.CheckedChanged += new EventHandler(this.CheckStartWindows_CheckedChanged);
        }

        private void LoadIconCache()
        {
            this.checkIconCache.CheckedChanged -= this.CheckIconCache_CheckedChanged;
            this.checkIconCache.Checked = MacroDeck.Configuration.CacheIcons;
            this.checkIconCache.CheckedChanged += this.CheckIconCache_CheckedChanged;

        }

        private void LoadUpdateChannel()
        {
            this.checkInstallDevVersions.CheckedChanged -= CheckInstallDevVersions_CheckedChanged;
            this.checkInstallBetaVersions.CheckedChanged -= CheckInstallBetaVersions_CheckedChanged;
            this.checkInstallDevVersions.Checked = MacroDeck.Configuration.UpdateDevVersions;
            this.checkInstallBetaVersions.Checked = MacroDeck.Configuration.UpdateBetaVersions;
            this.checkInstallDevVersions.CheckedChanged += CheckInstallDevVersions_CheckedChanged;
            this.checkInstallBetaVersions.CheckedChanged += CheckInstallBetaVersions_CheckedChanged;
        }

        private void LoadBackups()
        {
            this.backupsPanel.Controls.Clear();
            foreach (MacroDeckBackupInfo macroDeckBackupInfo in BackupManager.GetBackups().ToArray())
            {
                BackupItem backupItem = new BackupItem(macroDeckBackupInfo);
                this.backupsPanel.Controls.Add(backupItem);
            }
        }

        private void CheckInstallDevVersions_CheckedChanged(object sender, EventArgs e)
        {
            if (this.checkInstallDevVersions.Checked)
            {
                using (var msgBox = new CustomControls.MessageBox())
                {
                    if (msgBox.ShowDialog(LanguageManager.Strings.Warning, LanguageManager.Strings.WarningDevVersions, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        this.updaterPanel.Controls.Clear();
                        MacroDeck.Configuration.UpdateDevVersions = true;
                        MacroDeck.SaveConfiguration();
                        Updater.Updater.CheckForUpdatesAsync();
                    }
                    else
                    {
                        this.LoadUpdateChannel();
                    }
                }
            } 
            else
            {
                this.updaterPanel.Controls.Clear();
                MacroDeck.Configuration.UpdateDevVersions = false;
                MacroDeck.SaveConfiguration();
                Updater.Updater.CheckForUpdatesAsync();
            }
        }

        private void CheckInstallBetaVersions_CheckedChanged(object sender, EventArgs e)
        {
            if (this.checkInstallBetaVersions.Checked)
            {
                using (var msgBox = new CustomControls.MessageBox())
                {
                    if (msgBox.ShowDialog(LanguageManager.Strings.Warning, LanguageManager.Strings.WarningBetaVersions, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        this.updaterPanel.Controls.Clear();
                        MacroDeck.Configuration.UpdateBetaVersions = true;
                        MacroDeck.SaveConfiguration();
                        Updater.Updater.CheckForUpdatesAsync();
                    }
                    else
                    {
                        this.LoadUpdateChannel();
                    }
                }
            }
            else
            {
                this.updaterPanel.Controls.Clear();
                MacroDeck.Configuration.UpdateBetaVersions = false;
                MacroDeck.SaveConfiguration();
                Updater.Updater.CheckForUpdatesAsync();
            }
        }

        private void LoadNetworkAdapters()
        {
            this.networkAdapter.SelectedIndexChanged -= this.NetworkAdapter_SelectedIndexChanged;
            this.networkAdapter.Items.Clear();
            try
            {
                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface adapter in adapters)
                {
                    this.networkAdapter.Items.Add(adapter.Name);
                }
            }
            catch { }
            this.networkAdapter.Text = this.GetAdapterFromIPAddress(MacroDeck.Configuration.Host_Address);
            this.lblIpAddress.Text = MacroDeck.Configuration.Host_Address;
            this.networkAdapter.SelectedIndexChanged += new EventHandler(this.NetworkAdapter_SelectedIndexChanged);
        }

        private string GetAdapterFromIPAddress(string address)
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in adapters)
            {
                foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.Address.ToString().Equals(address))
                    {
                        return adapter.Name;
                    }
                }
            }

            return "";
        }

        public IPAddress GetDefaultIPAddress()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return null;
            }
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());

            return host
                .AddressList
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
        }

        public string GetIPAddressFromAdapter(string adapterName)
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in adapters)
            {
                if (adapter.Name.Equals(adapterName))
                {
                    foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ip.Address.ToString();
                    }
                }
            }
            return "0.0.0.0";
        }

        private void NetworkAdapter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (networkAdapter.SelectedItem.ToString().Equals("All"))
            {
                this.lblIpAddress.Text = "0.0.0.0";
            }
            else
            {
                this.lblIpAddress.Text = this.GetIPAddressFromAdapter(networkAdapter.SelectedItem.ToString());
            }
            MacroDeck.Configuration.Host_Address = this.lblIpAddress.Text;
            MacroDeck.SaveConfiguration();
            MacroDeckServer.Start(MacroDeck.Configuration.Host_Address, MacroDeck.Configuration.Host_Port);
        }

        private void BtnChangePort_Click(object sender, EventArgs e)
        {
            if (this.port.Value == MacroDeck.Configuration.Host_Port) return;
            MacroDeck.Configuration.Host_Port = (int)this.port.Value;
            MacroDeck.SaveConfiguration();
            MacroDeckServer.Start(MacroDeck.Configuration.Host_Address, MacroDeck.Configuration.Host_Port);
        }

        private void CheckStartWindows_CheckedChanged(object sender, EventArgs e)
        {
            MacroDeck.Configuration.AutoStart = checkStartWindows.Checked;
            MacroDeck.SaveConfiguration();
        }

        private void CheckAutoUpdate_CheckedChanged(object sender, EventArgs e)
        {
            MacroDeck.Configuration.AutoUpdates = checkAutoUpdate.Checked;
            MacroDeck.SaveConfiguration();
        }

        private void BtnCheckUpdates_Click(object sender, EventArgs e)
        {
            this.Invoke(new Action(() => {
                this.btnCheckUpdates.Enabled = false;
                this.btnCheckUpdates.Spinner = true;
                }
            ));
            Updater.Updater.OnLatestVersionInstalled += OnLatestVersion;
            Updater.Updater.OnUpdateAvailable += OnUpdateAvailable;
            Updater.Updater.CheckForUpdatesAsync();
        }

        private void OnLatestVersion(object sender, EventArgs e)
        {
            Updater.Updater.OnLatestVersionInstalled -= OnLatestVersion;
            this.Invoke(new Action(() =>
            {
                this.btnCheckUpdates.Enabled = true;
                this.btnCheckUpdates.Spinner = false;
                using (var msgBox = new CustomControls.MessageBox())
                {
                    msgBox.ShowDialog(LanguageManager.Strings.NoUpdatesAvailable, LanguageManager.Strings.LatestVersionInstalled, MessageBoxButtons.OK);
                }
            }));

            
        }

        private void OnUpdateAvailable(object sender, EventArgs e)
        {
            Updater.Updater.OnUpdateAvailable -= OnUpdateAvailable;
            this.Invoke(new Action(() =>
            {
                this.AddUpdateAvailableControl();
            }));
        }

        private void AddUpdateAvailableControl()
        {
            this.btnCheckUpdates.Enabled = true;
            this.btnCheckUpdates.Spinner = false;
            if (this.updaterPanel.Controls.Count != 0)
            {
                this.updaterPanel.Controls.Clear();
            }

            this.updaterPanel.Controls.Add(new UpdateAvailableControl());
        }

        private void BtnLicenses_Click(object sender, EventArgs e)
        {
            using (var licensesDialog = new LicensesDialog())
            {
                licensesDialog.ShowDialog();
            }
        }

        private void Language_SelectedIndexChanged(object sender, EventArgs e)
        {
            MacroDeck.Configuration.Language = this.language.Text;
            MacroDeck.SaveConfiguration();
            LanguageManager.SetLanguage(MacroDeck.Configuration.Language);
            this.UpdateTranslation();
        }

        private void CheckIconCache_CheckedChanged(object sender, EventArgs e)
        {
            MacroDeck.Configuration.CacheIcons = this.checkIconCache.Checked;
            MacroDeck.SaveConfiguration();
        }

        private void BackupManager_BackupFailed(object sender, BackupFailedEventArgs e)
        {
            this.Invoke(new Action(() =>
            {
                this.btnCreateBackup.Spinner = false;
                using (var msgBox = new CustomControls.MessageBox())
                {
                    msgBox.ShowDialog(LanguageManager.Strings.Backup, LanguageManager.Strings.BackupFailed + ": " + Environment.NewLine + e.Message, MessageBoxButtons.OK);
                }
            }));
        }

        private void BackupManager_BackupSaved(object sender, EventArgs e)
        {
            this.Invoke(new Action(() =>
            {
                this.btnCreateBackup.Spinner = false;
                using (var msgBox = new CustomControls.MessageBox())
                {
                    msgBox.ShowDialog(LanguageManager.Strings.Backup, LanguageManager.Strings.BackupSuccessfullyCreated, MessageBoxButtons.OK);
                }
                this.LoadBackups();
            }));
        }

        private void BtnCreateBackup_Click(object sender, EventArgs e)
        {
            this.btnCreateBackup.Spinner = true;
            Task.Run(() =>
            {
                BackupManager.CreateBackup();
            });
        }

        private void BackupManager_DeleteSuccess(object sender, EventArgs e)
        {
            this.LoadBackups();
        }
    }
}
