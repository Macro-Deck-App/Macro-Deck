using SuchByte.MacroDeck.Extension;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
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
            this.Dock = DockStyle.Fill;
            this.btnAddViaZip.Text = LanguageManager.Strings.InstallFromFile;
            this.btnCheckUpdates.Text = LanguageManager.Strings.CheckForUpdatesNow;
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
            this.lblUpdateState.Text = (PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0) ? string.Format(LanguageManager.Strings.XUpdatesAvailable, PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count + IconManager.IconPacksUpdateAvailable.Count) : LanguageManager.Strings.AllExtensionsUpToDate;
            this.btnCheckUpdates.Text = (PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0) ? LanguageManager.Strings.UpdateAll : LanguageManager.Strings.CheckForUpdatesNow;
            this.lblUpdateState.ForeColor = (PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0) ? Color.FromArgb(222, 170, 27) : Color.Silver;
        }

        private void InstalledExtensionsView_Load(object sender, EventArgs e)
        {
            ListInstalledExtensions();
            UpdateUpdateLabelInfo();
            //PluginManager.OnPluginsChange += PluginManager_OnPluginsChange;
            //IconManager.InstallationFinished += PluginManager_OnPluginsChange;
            ExtensionStoreHelper.OnUpdateCheckFinished += UpdateCheckFinished;
            ExtensionStoreHelper.OnInstallationFinished += ExtensionStoreHelper_OnInstallationFinished;
        }

        private void ExtensionStoreHelper_OnInstallationFinished(object sender, EventArgs e)
        {
            if (!this.IsHandleCreated || this.IsDisposed) return;
            this.Invoke(new Action(() => {
                try
                {
                    ListInstalledExtensions();
                    UpdateUpdateLabelInfo();
                }
                catch { }
            }));
        }

        private void UpdateCheckFinished(object sender, EventArgs e)
        {
            this.Invoke(new Action(() =>
            {
                this.btnCheckUpdates.Spinner = false;
                this.btnCheckUpdates.Enabled = true;
                UpdateUpdateLabelInfo();
                if (PluginManager.PluginsUpdateAvailable.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0)
                {
                    ListInstalledExtensions();
                }
            }));
        } 

        private void BtnCheckUpdates_Click(object sender, EventArgs e)
        {
            if (PluginManager.PluginsUpdateAvailable.Count - PluginManager.UpdatedPlugins.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0)
            {
                ExtensionStoreHelper.UpdateAllPackages();
            } else
            {
                this.btnCheckUpdates.Spinner = true;
                this.btnCheckUpdates.Enabled = false;
                ExtensionStoreHelper.SearchUpdatesAsync();
            }
        }
    }
}
