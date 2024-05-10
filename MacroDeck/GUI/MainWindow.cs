using System.Drawing;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using SuchByte.MacroDeck.DataTypes.Updater;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.GUI.MainWindowContents;
using SuchByte.MacroDeck.GUI.MainWindowViews;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Services;
using SuchByte.MacroDeck.Startup;
using Form = SuchByte.MacroDeck.GUI.CustomControls.Form;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI;

public partial class MainWindow : Form
{
    private NotificationsList? _notificationsList;

    private DeckView? _deckView;
    public DeckView DeckView
    {
        get
        {
            if (_deckView is null || _deckView.IsDisposed || !_deckView.IsHandleCreated)
            {
                _deckView = new DeckView();
            }
            return _deckView;
        }
    }


    public MainWindow()
    {
        InitializeComponent();
        btnNotifications.BackColor = Color.Transparent;
        UpdateTranslation();
        LanguageManager.LanguageChanged += LanguageChanged;
        UpdateService.Instance().UpdateAvailable += UpdateAvailable;
        Shown += MainWindowShown;

        _deckView = new DeckView();
    }

    private void UpdateAvailable(object? sender, UpdateApiVersionInfo e)
    {
        using var updateAvailableDialog = new UpdateAvailableDialog(e);
        updateAvailableDialog.ShowDialog();
    }

    private void UpdateTranslation()
    {
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        UpdateTranslation();
        DeckView?.UpdateTranslation();
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
            case DeckView _:
                SelectContentButton(btnDeck);
                break;
            case DeviceManagerView:
                SelectContentButton(btnDeviceManager);
                break;
            case ExtensionsView:
                SelectContentButton(btnExtensions);
                break;
            case SettingsView:
                SelectContentButton(btnSettings);
                break;
            case VariablesView:
                SelectContentButton(btnVariables);
                break;
        }
    }

    private void MainWindowShown(object? sender, EventArgs e)
    {
        Application.DoEvents();
        RefreshPluginsLabels();

        if (MacroDeck.SafeMode)
        {
            BackColor = Color.FromArgb(99, 0, 0);
            lblSafeMode.Visible = true;
            using var msgBox = new MessageBox();
            msgBox.ShowDialog("Safe mode", "Macro Deck was started in safe mode! This means no changes on the action buttons will be saved to prevent damage.", MessageBoxButtons.OK);
        }

        SetView(DeckView);

        btnSettings.SetNotification(UpdateService.Instance().VersionInfo != null);
        ExtensionStoreHelper.SearchUpdatesAsync();

        btnNotifications.NotificationCount = NotificationManager.Notifications.Count;

        var updateApiVersionInfo = UpdateService.Instance().VersionInfo;
        if (updateApiVersionInfo != null)
        {
            using var updateAvailableDialog = new UpdateAvailableDialog(updateApiVersionInfo);
            updateAvailableDialog.ShowDialog();
        }

        this.qrCodeBox.BackgroundImage = QrCodeService.Instance.GetQuickSetupQrCode();
    }

    private void MainWindow_Load(object? sender, EventArgs e)
    {
        lblVersion.Text = $@"Macro Deck {MacroDeck.Version}";

        PluginManager.OnPluginsChange += OnPluginsChanged;
        IconManager.OnIconPacksChanged += OnPluginsChanged;
        IconManager.OnUpdateCheckFinished += OnPackageManagerUpdateCheckFinished;

        MacroDeckServer.OnDeviceConnectionStateChanged += OnClientsConnectedChanged;
        OnClientsConnectedChanged(null, EventArgs.Empty);

        NotificationManager.OnNotification += NotificationsChanged;
        NotificationManager.OnNotificationRemoved += NotificationsChanged;
        ExtensionStoreHelper.OnInstallationFinished += ExtensionStoreHelper_OnInstallationFinished;

        CenterToScreen();
    }

    private void NotificationsChanged(object? sender, EventArgs e)
    {
        btnNotifications.NotificationCount = NotificationManager.Notifications.Count;
    }

    private void ExtensionStoreHelper_OnInstallationFinished(object? sender, EventArgs e)
    {
        RefreshPluginsLabels();
    }

    private void OnPackageManagerUpdateCheckFinished(object? sender, EventArgs e)
    {
        RefreshPluginsLabels();
    }

    private void OnPluginsChanged(object? sender, EventArgs e)
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

    private void OnClientsConnectedChanged(object? sender, EventArgs e)
    {
        Invoke(new Action(() =>
            lblNumClientsConnected.Text = string.Format(LanguageManager.Strings.XClientsConnected, MacroDeckServer.Clients.Count)
        ));
    }

    private void BtnDeck_Click(object? sender, EventArgs e)
    {
        SetView(DeckView);
        DeckView.UpdateButtons();
    }

    private void BtnExtensions_Click(object? sender, EventArgs e)
    {
        SetView(new ExtensionsView());
    }

    private void BtnSettings_Click(object? sender, EventArgs e)
    {
        SetView(new SettingsView());
    }

    private void BtnDeviceManager_Click(object? sender, EventArgs e)
    {
        SetView(new DeviceManagerView());
    }

    public void OnFormClosing(object? sender, EventArgs e)
    {
        foreach (Control control in contentPanel.Controls)
        {
            control.Dispose();
        }
    }

    private void BtnVariables_Click(object? sender, EventArgs e)
    {
        SetView(new VariablesView());
    }

    private void BtnNotifications_Click(object? sender, EventArgs e)
    {
        if (_notificationsList == null || _notificationsList.IsDisposed)
        {
            _notificationsList = new NotificationsList
            {
                Location = btnNotifications.Location with { Y = btnNotifications.Location.Y + btnNotifications.Height, X = btnNotifications.Location.X + btnNotifications.Size.Width + 20 }
            };
            _notificationsList.OnCloseRequested += (_, _) =>
            {
                Controls.Remove(_notificationsList);
            };
        }

        if (Controls.Contains(_notificationsList))
        {
            Controls.Remove(_notificationsList);
        }
        else
        {
            Controls.Add(_notificationsList);
            _notificationsList.BringToFront();
        }
    }
}