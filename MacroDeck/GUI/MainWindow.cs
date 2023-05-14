using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.MainWindowContents;
using SuchByte.MacroDeck.GUI.MainWindowViews;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Startup;
using Form = SuchByte.MacroDeck.GUI.CustomControls.Form;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI;

public partial class MainWindow : Form
{
    private DeckView _deckView { get; set; }
        
    public DeckView DeckView
    {
        get
        {
            if (_deckView == null || _deckView.IsDisposed)
            {
                _deckView = new DeckView();
            }
            return _deckView;
        }
    }

    private NotificationsList notificationsList;

    public MainWindow()
    {
        InitializeComponent();
        btnNotifications.BackColor = Color.Transparent;
        UpdateTranslation();
        LanguageManager.LanguageChanged += LanguageChanged;
        Updater.Updater.OnUpdateAvailable += UpdateAvailable;
        _deckView ??= new DeckView();
    }


    private void UpdateTranslation()
    {
        lblIpAddressHostname.Text = LanguageManager.Strings.IpAddressHostNamePort;
    }

    private void LanguageChanged(object sender, EventArgs e)
    {
        UpdateTranslation();
        DeckView?.UpdateTranslation();
    }


    private void UpdateAvailable(object sender, EventArgs e)
    {
        btnSettings.SetNotification(Updater.Updater.UpdateAvailable);
    }

    public void SelectContentButton(Control control)
    {
        foreach (var contentButton in contentButtonPanel.Controls.OfType<ContentSelectorButton>().Where(x => x != control && x.Selected))
        {
            contentButton.Selected = false;
        }
        btnSettings.Selected = false;
        ((ContentSelectorButton)control).Selected = true;
    }

    public void SetView(Control view)
    {
        if (contentPanel.Controls.Contains(view)) return;

        foreach (var control in contentPanel.Controls.OfType<Control>().Where(x => x != view && x != DeckView))
        {
            control.Dispose();
        }
        foreach (var control in contentPanel.Controls.OfType<Control>().Where(x => x != view))
        {
            contentPanel.Controls.Remove(control);
        }
        contentPanel.Controls.Add(view);

        switch (view)
        {
            case DeckView deck:
                SelectContentButton(btnDeck);
                break;
            case DeviceManagerView deviceManager:
                SelectContentButton(btnDeviceManager);
                break;
            case ExtensionsView extensions:
                SelectContentButton(btnExtensions);
                break;
            case SettingsView settings:
                SelectContentButton(btnSettings);
                break;
            case VariablesView variables:
                SelectContentButton(btnVariables);
                break;
        }
    }


    private void MainWindow_Load(object sender, EventArgs e)
    {

        lblVersion.Text = "Macro Deck " + MacroDeck.Version.VersionString;
            
        PluginManager.OnPluginsChange += OnPluginsChanged;
        IconManager.OnIconPacksChanged += OnPluginsChanged;
        IconManager.OnUpdateCheckFinished += OnPackageManagerUpdateCheckFinished;
            
        MacroDeckServer.OnDeviceConnectionStateChanged += OnClientsConnectedChanged;
        MacroDeckServer.OnServerStateChanged += OnServerStateChanged;
        OnClientsConnectedChanged(null, EventArgs.Empty);
        OnServerStateChanged(null, EventArgs.Empty);
        RefreshPluginsLabels();
        LoadHosts();

        if (MacroDeck.SafeMode)
        {
            BackColor = Color.FromArgb(99, 0, 0);
            lblSafeMode.Visible = true;
            using var msgBox = new MessageBox();
            msgBox.ShowDialog("Safe mode", "Macro Deck was started in safe mode! This means no changes on the action buttons will be saved to prevent damage.", MessageBoxButtons.OK);
        }

        btnExtensions.SetNotification(PluginManager.PluginsUpdateAvailable.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0);

        navigation.Visible = true;
        btnSettings.SetNotification(Updater.Updater.UpdateAvailable);


        SetView(DeckView);
        NotificationManager.OnNotification += NotificationsChanged;
        NotificationManager.OnNotificationRemoved += NotificationsChanged;
        ExtensionStoreHelper.SearchUpdatesAsync();
        ExtensionStoreHelper.OnInstallationFinished += ExtensionStoreHelper_OnInstallationFinished;
        CenterToScreen();
        btnNotifications.NotificationCount = NotificationManager.Notifications.Count;
    }

    private void LoadHosts()
    {
        hosts.SelectedIndexChanged -= Hosts_SelectedIndexChanged;
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            hosts.Items.Add(networkInterface.GetIPProperties().UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault().Address.ToString());
        }
        hosts.Text = MacroDeck.Configuration.HostAddress;
        hosts.SelectedIndexChanged += Hosts_SelectedIndexChanged;
    }

    private void NotificationsChanged(object sender, EventArgs e)
    {
        btnNotifications.NotificationCount = NotificationManager.Notifications.Count;
    }

    private void ExtensionStoreHelper_OnInstallationFinished(object sender, EventArgs e)
    {
        RefreshPluginsLabels();
    }

    private void OnPackageManagerUpdateCheckFinished(object sender, EventArgs e)
    {
        RefreshPluginsLabels();
    }

    private void OnPluginsChanged(object sender, EventArgs e)
    {
        RefreshPluginsLabels();
            
    }

    private void RefreshPluginsLabels()
    {
        if (!IsHandleCreated || IsDisposed) return;
        Invoke(() =>
        {
            btnExtensions.SetNotification(PluginManager.PluginsUpdateAvailable.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0);
        });
            
    }

    private void OnServerStateChanged(object sender, EventArgs e)
    {
        Invoke(() =>
        {
            if (MacroDeckServer.WebSocketServer.ListenerSocket == null)
            {
                lblServerStatus.Text = LanguageManager.Strings.ServerOffline;
            }
            else
            {
                lblServerStatus.Text = LanguageManager.Strings.ServerRunning;
                hosts.Text = MacroDeck.Configuration.HostAddress;
                lblPort.Text = MacroDeckServer.WebSocketServer.Port.ToString();
            }
        });
    }

    private void OnClientsConnectedChanged(object sender, EventArgs e)
    {
        Invoke(new Action(() =>
            lblNumClientsConnected.Text = string.Format(LanguageManager.Strings.XClientsConnected, MacroDeckServer.Clients.Count)
        ));
    }

    private void BtnDeck_Click(object sender, EventArgs e)
    {
        SetView(DeckView);
        DeckView.UpdateButtons();
    }

    private void BtnExtensions_Click(object sender, EventArgs e)
    {
        SetView(new ExtensionsView());
    }

    private void BtnSettings_Click(object sender, EventArgs e)
    {
        SetView(new SettingsView());
    }

    private void BtnDeviceManager_Click(object sender, EventArgs e)
    {
        SetView(new DeviceManagerView());
    }

    public void OnFormClosing(object sender, EventArgs e)
    {
        foreach (Control control in contentPanel.Controls)
        {
            control.Dispose();
        }
    }

    private void BtnVariables_Click(object sender, EventArgs e)
    {
        SetView(new VariablesView());
    }

    private void LblErrorsWarnings_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(MacroDeckLogger.CurrentFilename)
            {
                UseShellExecute = true,
            }
        };
        p.Start();
    }


    private void BtnNotifications_Click(object sender, EventArgs e)
    {
        if (notificationsList == null || notificationsList.IsDisposed)
        {
            notificationsList = new NotificationsList
            {
                Location = new Point(btnNotifications.Location.X, btnNotifications.Location.Y + btnNotifications.Height)
            };
            notificationsList.OnCloseRequested += (sender, e) =>
            {
                Controls.Remove(notificationsList);
            };
        }

        if (Controls.Contains(notificationsList))
        {
            Controls.Remove(notificationsList);
        } else
        {
            Controls.Add(notificationsList);
            notificationsList.BringToFront();
        }


    }

    private void Hosts_SelectedIndexChanged(object sender, EventArgs e)
    {
        MacroDeck.Configuration.HostAddress = hosts.Text;
        MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
    }
}