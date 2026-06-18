using SuchByte.MacroDeck.Extension;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;

public partial class InstalledExtensionsView : UserControl
{
    public event EventHandler RequestExtensionStore;
    public event EventHandler RequestZipInstaller;

    private List<ExtensionCard> _allCards = new();

    public InstalledExtensionsView()
    {
        InitializeComponent();
        Dock = DockStyle.Fill;
        btnAddViaZip.Text = LanguageManager.Strings.InstallFromFile;
        btnCheckUpdates.Text = LanguageManager.Strings.CheckForUpdatesNow;
    }

    private void BtnAddExtensions_Click(object sender, EventArgs e)
    {
        RequestExtensionStore?.Invoke(this, EventArgs.Empty);
    }

    private void BtnAddViaZip_Click(object sender, EventArgs e)
    {
        RequestZipInstaller?.Invoke(this, EventArgs.Empty);
    }

    public void ListInstalledExtensions()
    {
        if (IsDisposed)
        {
            return;
        }

        var cards = new List<ExtensionCard>();

        foreach (var macroDeckPlugin in PluginManager.Plugins.Values)
        {
            if (PluginManager.ProtectedPlugins.Contains(macroDeckPlugin))
            {
                continue;
            }

            cards.Add(BuildCard(new PluginExtension(macroDeckPlugin),
                PluginManager.PluginsUpdateAvailable.Contains(macroDeckPlugin) &&
                !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin)));
        }

        foreach (var macroDeckPlugin in PluginManager.PluginsNotLoaded.Values)
        {
            if (PluginManager.ProtectedPlugins.Contains(macroDeckPlugin))
            {
                continue;
            }

            cards.Add(BuildCard(new PluginExtension(macroDeckPlugin),
                PluginManager.PluginsUpdateAvailable.Contains(macroDeckPlugin) &&
                !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin)));
        }

        foreach (var iconPack in IconManager.IconPacks)
        {
            cards.Add(BuildCard(new IconPackExtension(iconPack),
                IconManager.IconPacksUpdateAvailable.Contains(iconPack)));
        }

        _allCards = cards;
        ApplyFilter();
    }

    private ExtensionCard BuildCard(IMacroDeckExtension extension, bool updateAvailable)
    {
        var card = new ExtensionCard
        {
            BadgeText = extension.ExtensionTypeDisplayName,
            BackgroundTint = updateAvailable ? Color.FromArgb(222, 170, 27) : Color.FromArgb(65, 65, 65)
        };

        switch (extension.ExtensionType)
        {
            case ExtensionType.Plugin:
                card.BadgeColor = Color.FromArgb(0, 95, 173);
                var macroDeckPlugin = extension.ExtensionObject as MacroDeckPlugin;
                ApplyPluginStatus(card, macroDeckPlugin, updateAvailable);
                card.Title = macroDeckPlugin.Name;
                card.Subtitle = $"{LanguageManager.Strings.InstalledVersion}: {macroDeckPlugin.Version}";
                card.SearchText = $"{macroDeckPlugin.Name} {macroDeckPlugin.Author}".ToLowerInvariant();
                var pluginIcon = macroDeckPlugin.PluginIcon ?? Resources.Icon;
                card.DisposableIcon = false;
                card.LoadIconAsync = _ => Task.FromResult<Image>(pluginIcon);
                break;
            case ExtensionType.IconPack:
                card.BadgeColor = Color.FromArgb(0, 173, 14);
                var iconPack = extension.ExtensionObject as IconPack;
                card.StatusText = updateAvailable
                    ? LanguageManager.Strings.UpdateAvailable
                    : LanguageManager.Strings.Enabled;
                card.StatusColor = updateAvailable ? Color.White : Color.FromArgb(0, 192, 0);
                card.Title = iconPack.Name;
                card.Subtitle = $"{LanguageManager.Strings.InstalledVersion}: {iconPack.Version}";
                card.SearchText = iconPack.Name.ToLowerInvariant();
                card.DisposableIcon = true;
                card.LoadIconAsync = _ => Task.Run<Image>(() => LoadIconPackPreview(iconPack));
                break;
        }

        AddActions(card, extension, updateAvailable);
        return card;
    }

    private static void ApplyPluginStatus(ExtensionCard card, MacroDeckPlugin macroDeckPlugin, bool updateAvailable)
    {
        if (PluginManager.PluginsNotLoaded.ContainsValue(macroDeckPlugin) &&
            !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin) &&
            !updateAvailable)
        {
            card.StatusText = LanguageManager.Strings.Disabled;
            card.StatusColor = Color.IndianRed;
        }
        else if (PluginManager.UpdatedPlugins.Contains(macroDeckPlugin))
        {
            card.StatusText = LanguageManager.Strings.PendingRestart;
            card.StatusColor = Color.Yellow;
        }
        else if (PluginManager.PluginsUpdateAvailable.Contains(macroDeckPlugin) &&
            !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin))
        {
            card.StatusText = LanguageManager.Strings.UpdateAvailable;
            card.StatusColor = Color.White;
        }
        else
        {
            card.StatusText = LanguageManager.Strings.Enabled;
            card.StatusColor = Color.FromArgb(0, 192, 0);
        }
    }

    private void AddActions(ExtensionCard card, IMacroDeckExtension extension, bool updateAvailable)
    {
        var configurable = extension.Configurable || extension.GetType() == typeof(IconPackExtension);
        if (configurable)
        {
            card.Actions.Add(new CardAction
            {
                Label = LanguageManager.Strings.Configure,
                Style = CardActionStyle.Primary,
                OnClick = () => Configure(extension)
            });
        }

        if (updateAvailable)
        {
            card.Actions.Add(new CardAction
            {
                Label = LanguageManager.Strings.Update,
                Style = CardActionStyle.Accent,
                OnClick = () => Update(extension)
            });
        }

        card.Actions.Add(new CardAction
        {
            Label = LanguageManager.Strings.Uninstall,
            Style = CardActionStyle.Link,
            OnClick = () => Uninstall(extension)
        });
    }

    private static Image LoadIconPackPreview(IconPack iconPack)
    {
        // Clone the official icon so the grid owns (and may dispose) the bitmap it draws.
        if (iconPack.IconPackIcon != null)
        {
            return new Bitmap(iconPack.IconPackIcon);
        }

        return IconPackPreview.GeneratePreviewImage(iconPack);
    }

    private void Configure(IMacroDeckExtension extension)
    {
        switch (extension.ExtensionType)
        {
            case ExtensionType.Plugin:
                var macroDeckPlugin = extension.ExtensionObject as MacroDeckPlugin;
                if (!macroDeckPlugin.CanConfigure)
                {
                    return;
                }

                macroDeckPlugin.OpenConfigurator();
                break;
            case ExtensionType.IconPack:
                var iconPack = extension.ExtensionObject as IconPack;
                using (var iconSelector = new IconSelector(iconPack))
                {
                    iconSelector.ShowDialog();
                }

                break;
        }
    }

    private void Update(IMacroDeckExtension extension)
    {
        switch (extension.ExtensionType)
        {
            case ExtensionType.Plugin:
                var macroDeckPlugin = extension.ExtensionObject as MacroDeckPlugin;
                ExtensionStoreHelper.InstallPluginById(ExtensionStoreHelper.GetPackageId(macroDeckPlugin));
                break;
            case ExtensionType.IconPack:
                var iconPack = extension.ExtensionObject as IconPack;
                ExtensionStoreHelper.InstallPluginById(iconPack.PackageId);
                break;
        }
    }

    private void Uninstall(IMacroDeckExtension extension)
    {
        switch (extension.ExtensionType)
        {
            case ExtensionType.Plugin:
                var macroDeckPlugin = extension.ExtensionObject as MacroDeckPlugin;
                using (var msgBox = new MessageBox())
                {
                    if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure,
                            string.Format(LanguageManager.Strings.XWillBeUninstalled, macroDeckPlugin.Name),
                            MessageBoxButtons.YesNo) ==
                        DialogResult.Yes)
                    {
                        PluginManager.DeletePlugin(macroDeckPlugin.Name);
                        RefreshList();

                        if (msgBox.ShowDialog("",
                                string.Format(LanguageManager.Strings.XSuccessfullyUninstalled, macroDeckPlugin.Name),
                                MessageBoxButtons.YesNo) ==
                            DialogResult.Yes)
                        {
                            MacroDeck.RestartMacroDeck("--show");
                        }

                        var btnRestart = new ButtonPrimary
                        {
                            AutoSize = true,
                            Text = LanguageManager.Strings.Restart
                        };
                        btnRestart.Click += (sender, e) => { MacroDeck.RestartMacroDeck("--show"); };
                        NotificationManager.SystemNotification("Extension Manager",
                            string.Format(LanguageManager.Strings.XSuccessfullyUninstalled, macroDeckPlugin.Name),
                            true,
                            new List<Control> { btnRestart });
                    }
                }

                break;
            case ExtensionType.IconPack:
                var iconPack = extension.ExtensionObject as IconPack;
                using (var msgBox = new MessageBox())
                {
                    if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure,
                            string.Format(LanguageManager.Strings.XWillBeUninstalled, iconPack.Name),
                            MessageBoxButtons.YesNo) ==
                        DialogResult.Yes)
                    {
                        IconManager.DeleteIconPack(iconPack);
                        RefreshList();
                    }
                }

                break;
        }
    }

    private void RefreshList()
    {
        ListInstalledExtensions();
        UpdateUpdateLabelInfo();
    }

    private void TxtSearch_TextChanged(object sender, EventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = txtSearch.Text.Trim().ToLowerInvariant();
        var filtered = string.IsNullOrEmpty(query)
            ? _allCards
            : _allCards.Where(c => c.SearchText.Contains(query)).ToList();
        installedExtensionsGrid.SetCards(filtered);
    }

    private void UpdateUpdateLabelInfo()
    {
        lblUpdateState.Text
            = PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 ||
            IconManager.IconPacksUpdateAvailable.Count > 0
                ? string.Format(LanguageManager.Strings.XUpdatesAvailable,
                    PluginManager.PluginsUpdateAvailable.Count -
                    PluginManager.UpdatedPlugins.Count +
                    IconManager.IconPacksUpdateAvailable.Count)
                : LanguageManager.Strings.AllExtensionsUpToDate;
        btnCheckUpdates.Text
            = PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 ||
            IconManager.IconPacksUpdateAvailable.Count > 0
                ? LanguageManager.Strings.UpdateAll
                : LanguageManager.Strings.CheckForUpdatesNow;
        lblUpdateState.ForeColor
            = PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 ||
            IconManager.IconPacksUpdateAvailable.Count > 0
                ? Color.FromArgb(222, 170, 27)
                : Color.Silver;
    }

    private void InstalledExtensionsView_Load(object sender, EventArgs e)
    {
        ListInstalledExtensions();
        UpdateUpdateLabelInfo();
        ExtensionStoreHelper.OnUpdateCheckFinished += UpdateCheckFinished;
        ExtensionStoreHelper.OnInstallationFinished += ExtensionStoreHelper_OnInstallationFinished;
    }

    private void ExtensionStoreHelper_OnInstallationFinished(object sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        Invoke(() =>
        {
            try
            {
                ListInstalledExtensions();
                UpdateUpdateLabelInfo();
            }
            catch
            {
            }
        });
    }

    private void UpdateCheckFinished(object sender, EventArgs e)
    {
        Invoke(() =>
        {
            btnCheckUpdates.Spinner = false;
            btnCheckUpdates.Enabled = true;
            UpdateUpdateLabelInfo();
            if (PluginManager.PluginsUpdateAvailable.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0)
            {
                ListInstalledExtensions();
            }
        });
    }

    private void BtnCheckUpdates_Click(object sender, EventArgs e)
    {
        if (PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 ||
            IconManager.IconPacksUpdateAvailable.Count > 0)
        {
            ExtensionStoreHelper.UpdateAllPackages();
        }
        else
        {
            btnCheckUpdates.Spinner = true;
            btnCheckUpdates.Enabled = false;
            ExtensionStoreHelper.SearchUpdatesAsync();
        }
    }
}
