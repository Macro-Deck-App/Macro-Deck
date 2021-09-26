using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.Dialogs;
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
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            this.UpdateTranslation();
        }

        private void UpdateTranslation()
        {
            this.Name = Language.LanguageManager.Strings.SettingsTitle;
            this.tabGeneral.Text = Language.LanguageManager.Strings.General;
            this.tabConnection.Text = Language.LanguageManager.Strings.Connection;
            this.tabBackup.Text = Language.LanguageManager.Strings.Backup;
            this.tabUpdater.Text = Language.LanguageManager.Strings.Updates;
            this.tabAbout.Text = Language.LanguageManager.Strings.About;
            this.lblGeneral.Text = Language.LanguageManager.Strings.General;
            this.lblBehaviour.Text = Language.LanguageManager.Strings.Behaviour;
            this.checkStartWindows.Text = Language.LanguageManager.Strings.AutomaticallyStartWithWindows;
            this.lblLanguage.Text = Language.LanguageManager.Strings.Language;
            this.lblConnection.Text = Language.LanguageManager.Strings.Connection;
            this.lblNetworkAdapter.Text = Language.LanguageManager.Strings.NetworkAdapter;
            this.lblIpAddessLabel.Text = Language.LanguageManager.Strings.IPAddress;
            this.lblPort.Text = Language.LanguageManager.Strings.Port;
            this.btnChangePort.Text = Language.LanguageManager.Strings.Ok;
            this.groupConnectionInfo.Text = Language.LanguageManager.Strings.Info;
            this.lblConnectionInfo.Text = Language.LanguageManager.Strings.ConfigureNetworkInfo;
            this.lblBackup.Text = Language.LanguageManager.Strings.Backup;
            this.lblUpdates.Text = Language.LanguageManager.Strings.Updates;
            this.checkAutoUpdate.Text = Language.LanguageManager.Strings.AutomaticallyCheckUpdates;
            this.lblInstalledVersionLabel.Text = Language.LanguageManager.Strings.InstalledVersion;
            this.lblUpdateChannelLabel.Text = Language.LanguageManager.Strings.UpdateChannel;
            this.btnCheckUpdates.Text = Language.LanguageManager.Strings.CheckForUpdatesNow;
            this.lblVersionLabel.Text = Language.LanguageManager.Strings.Version;
            this.lblWebSocketAPILabel.Text = Language.LanguageManager.Strings.WebSocketAPIVersion;
            this.lblPluginAPILabel.Text = Language.LanguageManager.Strings.PluginAPIVersion;
            this.lblInstalledPluginsLabel.Text = Language.LanguageManager.Strings.InstalledPlugins;
            this.lblOSLabel.Text = Language.LanguageManager.Strings.OperatingSystem;
            this.lblDeveloped.Text = String.Format(Language.LanguageManager.Strings.DevelopedByX, "Manuel Mayer (SuchByte)");
            this.lblTranslationBy.Text = String.Format(Language.LanguageManager.Strings.XTranslationByX, Language.LanguageManager.Strings.__Language__, Language.LanguageManager.Strings.__Author__);
        }

        private void Settings_Load(object sender, EventArgs e)
        {
            this.LoadAutoStart();
            this.LoadLanguage();
            this.LoadNetworkAdapters();
            this.LoadUpdateChannel();
            this.LoadAutoUpdate();

            this.lblInstalledVersion.Text = MacroDeck.VersionString;
            this.lblWebsocketAPIVersion.Text = MacroDeck.ApiVersion.ToString();
            this.lblPluginAPIVersion.Text = MacroDeck.PluginApiVersion.ToString();
            this.lblVersion.Text = MacroDeck.VersionString;
            this.lblInstalledPlugins.Text = PluginManager.Plugins.Count.ToString();
            this.lblOS.Text = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString() + " " + (Environment.Is64BitOperatingSystem == true ? "64 bit" : "32 bit");
        }

        private void LoadLanguage()
        {
            this.language.SelectedIndexChanged -= Language_SelectedIndexChanged;
            this.language.Items.Clear();
            foreach (Language.Strings strings in Language.LanguageManager.Languages)
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

        private void LoadUpdateChannel()
        {
            this.updateChannel.SelectedIndexChanged -= this.UpdateChannel_SelectedIndexChanged;
            switch (MacroDeck.Configuration.UpdateChannel)
            {
                case 0:
                    this.updateChannel.Text = "Dev";
                    break;
                case 1:
                    this.updateChannel.Text = "Beta";
                    break;
                case 2:
                    this.updateChannel.Text = "Stable";
                    break;
            }
            this.updateChannel.SelectedIndexChanged += new EventHandler(this.UpdateChannel_SelectedIndexChanged);
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
            this.Invoke(new Action(() =>
                this.progressCheckUpdates.Visible = true
            ));
            MacroDeck.Updater.OnLatestVersionInstalled += OnLatestVersion;
            MacroDeck.Updater.OnUpdateAvailable += OnUpdateAvailable;
            MacroDeck.Updater.CheckForUpdatesAsync();
        }

        private void OnLatestVersion(object sender, EventArgs e)
        {
            MacroDeck.Updater.OnLatestVersionInstalled -= OnLatestVersion;
            this.Invoke(new Action(() =>
            {
                this.progressCheckUpdates.Visible = false;
                using (var msgBox = new CustomControls.MessageBox())
                {
                    msgBox.ShowDialog(Language.LanguageManager.Strings.NoUpdatesAvailable, Language.LanguageManager.Strings.LatestVersionInstalled, MessageBoxButtons.OK);
                }
            }));

            
        }

        private void OnUpdateAvailable(object sender, EventArgs e)
        {
            MacroDeck.Updater.OnUpdateAvailable -= OnUpdateAvailable;
            this.Invoke(new Action(() =>
            {
                this.progressCheckUpdates.Visible = false;
                using (var downloadUpdateDialog = new DownloadUpdateDialog((JObject)sender))
                {
                    downloadUpdateDialog.ShowDialog();
                }
            }));
            
        }

        private void UpdateChannel_SelectedIndexChanged(object sender, EventArgs e)
        {
            MacroDeck.Updater.OnUpdateAvailable -= OnUpdateAvailable;
            switch (this.updateChannel.SelectedItem)
            {
                case "Dev":
                    using (var msgBox = new CustomControls.MessageBox())
                    {
                        if (msgBox.ShowDialog(Language.LanguageManager.Strings.Warning, Language.LanguageManager.Strings.WarningDevVersions, MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            MacroDeck.Configuration.UpdateChannel = 0;
                            MacroDeck.SaveConfiguration();
                            MacroDeck.Updater.OnUpdateAvailable += OnUpdateAvailable;
                            MacroDeck.Updater.CheckForUpdatesAsync();
                        } else
                        {
                            this.LoadUpdateChannel();
                        }
                    }
                    
                    break;
                case "Beta":
                    using (var msgBox = new CustomControls.MessageBox())
                    {
                        if (msgBox.ShowDialog(Language.LanguageManager.Strings.Warning, Language.LanguageManager.Strings.WarningBetaVersions, MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            MacroDeck.Configuration.UpdateChannel = 1;
                            MacroDeck.SaveConfiguration();
                            MacroDeck.Updater.OnUpdateAvailable += OnUpdateAvailable;
                            MacroDeck.Updater.CheckForUpdatesAsync();
                        }
                        else
                        {
                            this.LoadUpdateChannel();
                        }
                    }
                    break;
                case "Stable":
                    MacroDeck.Configuration.UpdateChannel = 2;
                    MacroDeck.SaveConfiguration();
                    MacroDeck.Updater.OnUpdateAvailable += OnUpdateAvailable;
                    MacroDeck.Updater.CheckForUpdatesAsync();
                    break;
            }
                        
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
            Language.LanguageManager.SetLanguage(MacroDeck.Configuration.Language);
            this.UpdateTranslation();
        }
    }
}
