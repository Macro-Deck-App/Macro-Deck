using SuchByte.MacroDeck.Extension;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    public partial class InstalledExtensionsView : UserControl
    {

        public event EventHandler RequestExtensionStore;
        public event EventHandler RequestZipInstaller;
        public InstalledExtensionsView()
        {
            InitializeComponent();
        }

        private void BtnAddExtensions_Click(object sender, EventArgs e)
        {
            if (RequestExtensionStore != null)
            {
                RequestExtensionStore(this, EventArgs.Empty);
            }
        }

        private void BtnAddViaZip_Click(object sender, EventArgs e)
        {
            if (RequestZipInstaller != null)
            {
                RequestZipInstaller(this, EventArgs.Empty);
            }
        }

        public void ListInstalledExtensions()
        {
            if (this.IsDisposed) return;
            foreach (var control in this.installedExtensionsList.Controls)
            {
                (control as ExtensionItemView).ExtensionRemoved -= ExtensionItemView_ExtensionRemoved;
            }
            this.installedExtensionsList.Controls.Clear();
            foreach (var macroDeckPlugin in PluginManager.Plugins.Values)
            {
                if (PluginManager.ProtectedPlugins.Contains(macroDeckPlugin)) continue;
                ExtensionItemView extensionItemView = new ExtensionItemView(new PluginExtension(macroDeckPlugin), PluginManager.PluginsUpdateAvailable.Contains(macroDeckPlugin) && !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin));
                extensionItemView.ExtensionRemoved += ExtensionItemView_ExtensionRemoved;
                this.installedExtensionsList.Controls.Add(extensionItemView);
            }
            foreach (var macroDeckPlugin in PluginManager.PluginsNotLoaded.Values)
            {
                if (PluginManager.ProtectedPlugins.Contains(macroDeckPlugin)) continue;
                ExtensionItemView extensionItemView = new ExtensionItemView(new PluginExtension(macroDeckPlugin), PluginManager.PluginsUpdateAvailable.Contains(macroDeckPlugin) && !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin));
                extensionItemView.ExtensionRemoved += ExtensionItemView_ExtensionRemoved;
                this.installedExtensionsList.Controls.Add(extensionItemView);
            }

            foreach (var iconPack in IconManager.IconPacks)
            {
                ExtensionItemView extensionItemView = new ExtensionItemView(new IconPackExtension(iconPack), IconManager.IconPacksUpdateAvailable.Contains(iconPack));
                extensionItemView.ExtensionRemoved += ExtensionItemView_ExtensionRemoved;
                this.installedExtensionsList.Controls.Add(extensionItemView);
            }
        }


        private void ExtensionItemView_ExtensionRemoved(object sender, EventArgs e)
        {
            if (this.installedExtensionsList.Controls.Contains(sender as ExtensionItemView))
            {
                (sender as ExtensionItemView).Dispose();
                this.installedExtensionsList.Controls.Remove(sender as ExtensionItemView);
            }
        }

        private void UpdateUpdateLabelInfo()
        {
            this.lblUpdateState.Text = (PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0) ? $"{PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count + IconManager.IconPacksUpdateAvailable.Count} update(s) available" : "All extensions are up-to-date";
            this.btnCheckUpdates.Text = (PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0) ? "Update all" : "Check for updates";
            this.lblUpdateState.ForeColor = (PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0) ? Color.FromArgb(222, 170, 27) : Color.Silver;
        }

        private void InstalledExtensionsView_Load(object sender, EventArgs e)
        {
            ListInstalledExtensions();
            UpdateUpdateLabelInfo();
            PluginManager.OnPluginsChange += PluginManager_OnPluginsChange;
            IconManager.IconPacksLoaded += PluginManager_OnPluginsChange;
            ExtensionStoreHelper.OnUpdateCheckFinished += UpdateCheckFinished;
        }

        private void UpdateCheckFinished(object sender, EventArgs e)
        {
            this.Invoke(new Action(() =>
            {
                this.btnCheckUpdates.Spinner = false;
                this.btnCheckUpdates.Enabled = true;
                UpdateUpdateLabelInfo();
                ListInstalledExtensions();
            }));
        }

        private void PluginManager_OnPluginsChange(object sender, EventArgs e)
        {
            this.Invoke(new Action(() => {
                ListInstalledExtensions();
                UpdateUpdateLabelInfo();
            }));
        }
        private void BtnCheckUpdates_Click(object sender, EventArgs e)
        {
            if (PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0)
            {
                List<ExtensionStoreDownloaderPackageInfoModel> packages = new List<ExtensionStoreDownloaderPackageInfoModel>();
                foreach (var updatePlugin in PluginManager.PluginsUpdateAvailable)
                {
                    if (PluginManager.UpdatedPlugins.Contains(updatePlugin)) continue;
                    packages.Add(new ExtensionStoreDownloaderPackageInfoModel()
                    {
                        ExtensionType = ExtensionType.Plugin,
                        PackageId = ExtensionStoreHelper.GetPackageId(updatePlugin)
                    });
                }
                foreach (var updateIconPack in IconManager.IconPacksUpdateAvailable)
                {
                    packages.Add(new ExtensionStoreDownloaderPackageInfoModel()
                    {
                        ExtensionType = ExtensionType.IconPack,
                        PackageId = updateIconPack.PackageId,
                    });
                }
                ExtensionStoreHelper.InstallPackages(packages);
            } else
            {
                this.btnCheckUpdates.Spinner = true;
                this.btnCheckUpdates.Enabled = false;
                ExtensionStoreHelper.SearchUpdatesAsync();
            }
        }
    }
}
