using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using SuchByte.MacroDeck.Backups;
using SuchByte.MacroDeck.DataTypes.Updater;
using SuchByte.MacroDeck.GUI.CustomControls.Settings;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Services;
using SuchByte.MacroDeck.Startup;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI.MainWindowContents;

public partial class SettingsView : UserControl
{
    public SettingsView(int page = 0)
    {
        InitializeComponent();
        verticalTabControl.SelectTab(page);
        if (!DesignMode)
        {
            verticalTabControl.SelectedTabColor = Colors.WindowsAccentColor;
        }
        Dock = DockStyle.Fill;
        UpdateTranslation();
        BackupManager.BackupSaved += BackupManager_BackupSaved;
        BackupManager.BackupFailed += BackupManager_BackupFailed;
        BackupManager.DeleteSuccess += BackupManager_DeleteSuccess;
    }

    private void UpdateAvailable(object sender, EventArgs e)
    {
        verticalTabControl.SetNotification(2, UpdateService.Instance().VersionInfo != null);
    }

    private void UpdateTranslation()
    {
        Name = LanguageManager.Strings.SettingsTitle;
        tabGeneral.Text = LanguageManager.Strings.General;
        tabConnection.Text = LanguageManager.Strings.Connection;
        tabUpdater.Text = LanguageManager.Strings.Updates;
        tabAbout.Text = LanguageManager.Strings.About;
        lblGeneral.Text = LanguageManager.Strings.General;
        lblBehaviour.Text = LanguageManager.Strings.Behaviour;
        checkStartWindows.Text = LanguageManager.Strings.AutomaticallyStartWithWindows;
        checkIconCache.Text = LanguageManager.Strings.EnableIconCache;
        lblLanguage.Text = LanguageManager.Strings.Language;
        lblConnection.Text = LanguageManager.Strings.Connection;
        lblNetworkAdapter.Text = LanguageManager.Strings.NetworkAdapter;
        lblIpAddessLabel.Text = LanguageManager.Strings.IPAddress;
        lblPort.Text = LanguageManager.Strings.Port;
        btnChangePort.Text = LanguageManager.Strings.Ok;
        groupConnectionInfo.Text = LanguageManager.Strings.Info;
        lblConnectionInfo.Text = LanguageManager.Strings.ConfigureNetworkInfo;
        lblUpdates.Text = LanguageManager.Strings.Updates;
        checkAutoUpdate.Text = LanguageManager.Strings.AutomaticallyCheckUpdates;
        lblInstalledVersionLabel.Text = LanguageManager.Strings.InstalledVersion;
        tabBackups.Text = LanguageManager.Strings.Backups;
        lblBackups.Text = LanguageManager.Strings.Backups;
        btnCreateBackup.Text = LanguageManager.Strings.CreateBackup;
        checkInstallBetaVersions.Text = LanguageManager.Strings.InstallBetaVersions;
        btnCheckUpdates.Text = LanguageManager.Strings.CheckForUpdatesNow;
        lblWebSocketAPILabel.Text = LanguageManager.Strings.WebSocketAPIVersion;
        lblPluginAPILabel.Text = LanguageManager.Strings.PluginAPIVersion;
        lblInstalledPluginsLabel.Text = LanguageManager.Strings.InstalledPlugins;
        lblTranslationBy.Text = string.Format(LanguageManager.Strings.XTranslationByX, LanguageManager.Strings.__Language__, LanguageManager.Strings.__Author__);
    }

    private void Settings_Load(object sender, EventArgs e)
    {
        LoadAutoStart();
        LoadLanguage();
        LoadNetworkAdapters();
        LoadUpdateChannel();
        LoadAutoUpdate();
        LoadIconCache();
        LoadBackups();

        lblInstalledVersion.Text = MacroDeck.Version.ToString();
        lblWebsocketAPIVersion.Text = MacroDeck.ApiVersion.ToString();
        lblPluginAPIVersion.Text = MacroDeck.PluginApiVersion.ToString();
        lblMacroDeck.Text = "Macro Deck " + MacroDeck.Version.ToString();
        lblInstalledPlugins.Text = PluginManager.Plugins.Count.ToString();
    }

    private void LoadLanguage()
    {
        language.SelectedIndexChanged -= Language_SelectedIndexChanged;
        language.Items.Clear();
        foreach (var strings in LanguageManager.Languages)
        {
            language.Items.Add(strings.__Language__);
        }
        language.Text = MacroDeck.Configuration.Language;
        language.SelectedIndexChanged += Language_SelectedIndexChanged;
    }

    private void LoadAutoUpdate()
    {
        checkAutoUpdate.CheckedChanged -= CheckAutoUpdate_CheckedChanged;
        checkAutoUpdate.Checked = MacroDeck.Configuration.AutoUpdates;
        checkAutoUpdate.CheckedChanged += CheckAutoUpdate_CheckedChanged;
    }

    private void LoadAutoStart()
    {
        checkStartWindows.CheckedChanged -= CheckStartWindows_CheckedChanged;
        checkStartWindows.Checked = MacroDeck.Configuration.AutoStart;
        checkStartWindows.CheckedChanged += CheckStartWindows_CheckedChanged;
    }

    private void LoadIconCache()
    {
        checkIconCache.CheckedChanged -= CheckIconCache_CheckedChanged;
        checkIconCache.Checked = MacroDeck.Configuration.CacheIcons;
        checkIconCache.CheckedChanged += CheckIconCache_CheckedChanged;

    }

    private void LoadUpdateChannel()
    {
        checkInstallBetaVersions.CheckedChanged -= CheckInstallBetaVersions_CheckedChanged;
        checkInstallBetaVersions.Checked = MacroDeck.Configuration.UpdateBetaVersions;
        checkInstallBetaVersions.CheckedChanged += CheckInstallBetaVersions_CheckedChanged;
    }

    private void LoadBackups()
    {
        backupsPanel.Controls.Clear();
        foreach (var macroDeckBackupInfo in BackupManager.GetBackups().ToArray())
        {
            var backupItem = new BackupItem(macroDeckBackupInfo);
            backupsPanel.Controls.Add(backupItem);
        }
    }

    private void CheckInstallBetaVersions_CheckedChanged(object sender, EventArgs e)
    {
        if (checkInstallBetaVersions.Checked)
        {
            using var msgBox = new MessageBox();
            if (msgBox.ShowDialog(LanguageManager.Strings.Warning, LanguageManager.Strings.WarningBetaVersions, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                MacroDeck.Configuration.UpdateBetaVersions = true;
                MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
            }
            else
            {
                LoadUpdateChannel();
            }
        }
        else
        {
            MacroDeck.Configuration.UpdateBetaVersions = false;
            MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
        }
    }

    private void LoadNetworkAdapters()
    {
        networkAdapter.SelectedIndexChanged -= NetworkAdapter_SelectedIndexChanged;
        networkAdapter.Items.Clear();
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var adapter in adapters)
            {
                networkAdapter.Items.Add(adapter.Name);
            }
        }
        catch { }
        networkAdapter.Text = GetAdapterFromIPAddress(MacroDeck.Configuration.HostAddress);
        lblIpAddress.Text = MacroDeck.Configuration.HostAddress;
        networkAdapter.SelectedIndexChanged += NetworkAdapter_SelectedIndexChanged;
    }

    private string GetAdapterFromIPAddress(string address)
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces();
        foreach (var adapter in adapters)
        {
            foreach (var ip in adapter.GetIPProperties().UnicastAddresses)
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
        var host = Dns.GetHostEntry(Dns.GetHostName());

        return host
            .AddressList
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
    }

    public string GetIPAddressFromAdapter(string adapterName)
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces();
        foreach (var adapter in adapters)
        {
            if (adapter.Name.Equals(adapterName))
            {
                foreach (var ip in adapter.GetIPProperties().UnicastAddresses)
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
        lblIpAddress.Text = GetIPAddressFromAdapter(networkAdapter.SelectedItem.ToString());
        MacroDeck.Configuration.HostAddress = lblIpAddress.Text;
        MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
    }

    private void BtnChangePort_Click(object sender, EventArgs e)
    {
        if (port.Value == MacroDeck.Configuration.HostPort) return;
        MacroDeck.Configuration.HostPort = (int)port.Value;
        MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
        MacroDeckServer.Start(MacroDeck.Configuration.HostPort);
    }

    private void CheckStartWindows_CheckedChanged(object sender, EventArgs e)
    {
        MacroDeck.Configuration.AutoStart = checkStartWindows.Checked;
        MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
    }

    private void CheckAutoUpdate_CheckedChanged(object sender, EventArgs e)
    {
        MacroDeck.Configuration.AutoUpdates = checkAutoUpdate.Checked;
        MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
    }

    private async void BtnCheckUpdates_Click(object sender, EventArgs e)
    {
        btnCheckUpdates.Enabled = false;
        btnCheckUpdates.Spinner = true;

        try
        {
            var availableUpdate = await UpdateService.Instance().CheckForUpdatesAsync(CancellationToken.None);
            if (availableUpdate == null)
            {
                using var msgBox = new MessageBox();
                msgBox.ShowDialog(
                    LanguageManager.Strings.NoUpdatesAvailable,
                    LanguageManager.Strings.LatestVersionInstalled,
                    MessageBoxButtons.OK);

                return;
            }

            using var updateAvailableDialog = new UpdateAvailableDialog(availableUpdate);
            updateAvailableDialog.ShowDialog();
        }
        catch (Exception ex)
        {
            using var msgBox = new MessageBox();
            msgBox.ShowDialog(
                "Check for updates failed",
                "Make sure you have a internet connection",
                MessageBoxButtons.OK);

            MacroDeckLogger.Error($"Failed to check for updates\n{ex}");
        }
        finally
        {
            Invoke(() =>
            {
                btnCheckUpdates.Enabled = true;
                btnCheckUpdates.Spinner = false;
            });
        }

    }

    private void BtnLicenses_Click(object sender, EventArgs e)
    {
        using var licensesDialog = new LicensesDialog();
        licensesDialog.ShowDialog();
    }

    private void Language_SelectedIndexChanged(object sender, EventArgs e)
    {
        MacroDeck.Configuration.Language = language.Text;
        MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
        LanguageManager.SetLanguage(MacroDeck.Configuration.Language);
        UpdateTranslation();
    }

    private void CheckIconCache_CheckedChanged(object sender, EventArgs e)
    {
        MacroDeck.Configuration.CacheIcons = checkIconCache.Checked;
        MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
    }

    private void BackupManager_BackupFailed(object sender, BackupFailedEventArgs e)
    {
        Invoke(() =>
        {
            btnCreateBackup.Spinner = false;
            using var msgBox = new MessageBox();
            msgBox.ShowDialog(LanguageManager.Strings.Backup, LanguageManager.Strings.BackupFailed + ": " + Environment.NewLine + e.Message, MessageBoxButtons.OK);
        });
    }

    private void BackupManager_BackupSaved(object sender, EventArgs e)
    {
        Invoke(() =>
        {
            btnCreateBackup.Spinner = false;
            using (var msgBox = new MessageBox())
            {
                msgBox.ShowDialog(LanguageManager.Strings.Backup, LanguageManager.Strings.BackupSuccessfullyCreated, MessageBoxButtons.OK);
            }
            LoadBackups();
        });
    }

    private void BtnCreateBackup_Click(object sender, EventArgs e)
    {
        btnCreateBackup.Spinner = true;
        Task.Run(() =>
        {
            BackupManager.CreateBackup();
        });
    }

    private void BackupManager_DeleteSuccess(object sender, EventArgs e)
    {
        LoadBackups();
    }

    private void BtnGitHub_Click(object sender, EventArgs e)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo("https://github.com/Macro-Deck-App/Macro-Deck")
            {
                UseShellExecute = true,
            }
        };
        p.Start();
    }
}