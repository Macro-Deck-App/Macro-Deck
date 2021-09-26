using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    public partial class PackageManagerView : UserControl
    {
        private bool loading = false;

        public PackageManagerView()
        {
            InitializeComponent();
            this.UpdateTranslation();
        }

        public void UpdateTranslation()
        {
            this.Name = Language.LanguageManager.Strings.PackageManagerTitle;
            this.tabAvailable.Text = Language.LanguageManager.Strings.PackageManagerTabAvailable;
            this.tabInstalled.Text = Language.LanguageManager.Strings.PackageManagerTabInstalled;
            this.checkPlugins.Text = Language.LanguageManager.Strings.Plugins;
            this.checkIconPacks.Text = Language.LanguageManager.Strings.IconPacks;
            this.radioAll.Text = Language.LanguageManager.Strings.All;
            this.radioOnlyUpdates.Text = Language.LanguageManager.Strings.Updates;
            this.searchBox.PlaceHolderText = Language.LanguageManager.Strings.Search;
            this.seachBoxInstalledPlugins.PlaceHolderText = Language.LanguageManager.Strings.Search;
            this.btnInstallDll.Text = Language.LanguageManager.Strings.InstallDLL;
        }

        private void PackageManager_Load(object sender, EventArgs e)
        {
            this.RefreshInstalledPluginsList();
            PluginManager.OnPluginsChange += new EventHandler(this.OnPluginsChanged);
            this.radioOnlyUpdates.Text = String.Format(Language.LanguageManager.Strings.Updates + " ({0})", PluginManager.PluginsUpdateAvailable.Count + IconManager.IconPacksUpdateAvailable.Count);
            if (PluginManager.PluginsUpdateAvailable.Count + IconManager.IconPacksUpdateAvailable.Count > 0)
            {
                this.radioOnlyUpdates.Checked = true;
            } else
            {
                Task.Run(() =>
                {
                    this.LoadAvailablePlugins();
                });
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void LoadAvailablePlugins()
        {
            if (loading) return;
            loading = true;
            this.Invoke(new Action(() => {
                progressBar.Visible = true;
                this.radioOnlyUpdates.Text = String.Format(Language.LanguageManager.Strings.Updates + " ({0})", PluginManager.PluginsUpdateAvailable.Count + IconManager.IconPacksUpdateAvailable.Count);
            }
            ));

            foreach (Control control in this.panelAvailablePlugins.Controls)
            {
                if (control.GetType() == typeof(PackageManagerPluginItem))
                {
                    PackageManagerPluginItem packageManagerItem = control as PackageManagerPluginItem;
                    this.Invoke(new Action(() =>
                        packageManagerItem.OnInstallationFinished -= this.OnInstallationFinished
                    ));

                    this.Invoke(new Action(() =>
                        packageManagerItem.Dispose()
                    ));
                } else if (control.GetType() == typeof(PackageManagerIconPackItem))
                {
                    PackageManagerIconPackItem packageManagerItem = control as PackageManagerIconPackItem;
                    this.Invoke(new Action(() =>
                        packageManagerItem.OnInstallationFinished -= this.OnInstallationFinished
                    ));

                    this.Invoke(new Action(() =>
                        packageManagerItem.Dispose()
                    ));
                }

            }
            this.Invoke(new Action(() =>
                this.panelAvailablePlugins.Controls.Clear()
            ));

            if (this.checkPlugins.Checked)
            {
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/plugins.php?target-api=" + MacroDeck.PluginApiVersion);

                        JArray jsonArray = JArray.Parse(jsonString);
                        this.Invoke(new Action(() =>
                                 progressBar.Visible = false
                        ));
                        foreach (JObject jsonObject in jsonArray)
                        {
                            PackageManagerPluginItem packageManagerItem = new PackageManagerPluginItem(jsonObject);
                            if ((this.radioAll.Checked) || (this.radioOnlyUpdates.Checked && packageManagerItem.UpdateAvailable))
                            {
                                this.Invoke(new Action(() =>
                                        this.AddPackageManagerItem(packageManagerItem)
                                ));
                                packageManagerItem.OnInstallationFinished += this.OnInstallationFinished;
                            }
                        }
                    }
                }
                catch { }
            }

            if (this.checkIconPacks.Checked)
            {
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/iconpacks.php?target-api=" + MacroDeck.PluginApiVersion);

                        JArray jsonArray = JArray.Parse(jsonString);
                        this.Invoke(new Action(() =>
                                 progressBar.Visible = false
                        ));
                        foreach (JObject jsonObject in jsonArray)
                        {
                            PackageManagerIconPackItem packageManagerIconPackItem = new PackageManagerIconPackItem(jsonObject);
                            if ((this.radioAll.Checked) || (this.radioOnlyUpdates.Checked && packageManagerIconPackItem.UpdateAvailable))
                            {
                                this.Invoke(new Action(() =>
                                        this.AddPackageManagerIconPackItem(packageManagerIconPackItem)
                                ));
                                packageManagerIconPackItem.OnInstallationFinished += this.OnInstallationFinished;
                            }
                        }
                    }
                }
                catch { }
            }

            loading = false;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void AddPackageManagerItem(PackageManagerPluginItem packageManagerItem)
        {
            if (this != null && this.panelAvailablePlugins != null && this.panelAvailablePlugins.Controls != null)
            {
                this.panelAvailablePlugins.Controls.Add(packageManagerItem);
            }
        }

        private void AddPackageManagerIconPackItem(PackageManagerIconPackItem packageManagerIconPackItem)
        {
            if (this != null && this.panelAvailablePlugins != null && this.panelAvailablePlugins.Controls != null)
            {
                this.panelAvailablePlugins.Controls.Add(packageManagerIconPackItem);
            }
        }

        private void OnInstallationFinished(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                //this.LoadAvailablePlugins();
                //PluginManager.ScanUpdatesAsync();
                //IconManager.ScanUpdatesAsync();
            });
        }

        private void OnPluginsChanged(object sender, EventArgs e)
        {
            this.RefreshInstalledPluginsList();
            try
            {
                Task.Run(() =>
                {
                    this.LoadAvailablePlugins();
                });
            } catch { }
        }

        private void RefreshInstalledPluginsList()
        {
            foreach (PluginDetails plugin in this.installedPluginsPanel.Controls)
            {
                plugin.OnBtnDeleteClick -= this.OnBtnDeleteClick;
                plugin.Dispose();
            }
            this.installedPluginsPanel.Controls.Clear();

            foreach (IMacroDeckPlugin plugin in PluginManager.Plugins.Values)
            {
                PluginDetails pluginDetails = new PluginDetails(plugin);
                pluginDetails.OnBtnDeleteClick += new EventHandler(this.OnBtnDeleteClick);
                this.installedPluginsPanel.Controls.Add(pluginDetails);
            }

            foreach (IMacroDeckPlugin plugin in PluginManager.PluginsNotLoaded.Values)
            {
                PluginDetails pluginDetails = new PluginDetails(plugin);
                pluginDetails.OnBtnDeleteClick += new EventHandler(this.OnBtnDeleteClick);
                this.installedPluginsPanel.Controls.Add(pluginDetails);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }


        private void OnBtnDeleteClick(object sender, EventArgs e)
        {
            PluginDetails pluginDetails = (PluginDetails)sender;
            using (var msgBox = new CustomControls.MessageBox())
            {
                if (msgBox.ShowDialog(Language.LanguageManager.Strings.AreYouSure, String.Format(Language.LanguageManager.Strings.XWillBeUninstalled, pluginDetails.PluginName), MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    PluginManager.DeletePlugin(pluginDetails.PluginName);
                    this.installedPluginsPanel.Controls.Remove(pluginDetails);

                    if (msgBox.ShowDialog("", String.Format(Language.LanguageManager.Strings.XSuccessfullyUninstalled, pluginDetails.PluginName), MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        Process.Start(MacroDeck.MainDirectoryPath + AppDomain.CurrentDomain.FriendlyName, "--show");
                        Environment.Exit(0);
                    }
                }
            }
        }

        private void BtnInstallDll_Click(object sender, EventArgs e)
        {
            using (var msgBox = new CustomControls.MessageBox())
            {
                if (msgBox.ShowDialog(Language.LanguageManager.Strings.Warning, Language.LanguageManager.Strings.NeverInstallDLLFiles, MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    using(var openFileDialog = new OpenFileDialog
                    {
                        Title = "Import plugin",
                        CheckFileExists = true,
                        CheckPathExists = true,
                        DefaultExt = "dll",
                        Filter = "Plugins (*.dll;)|*.dll"
                    }) {
                        if (openFileDialog.ShowDialog() == DialogResult.OK)
                        {
                            PluginManager.InstallPlugin(Path.GetFullPath(openFileDialog.FileName));
                        }
                        openFileDialog.Dispose();
                    }

                }
            }
        }


        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            if (searchBox.Text.Length > 1)
            {
                foreach (Control control in panelAvailablePlugins.Controls)
                {
                    if (control.GetType() == typeof(PackageManagerPluginItem))
                    {
                        PackageManagerPluginItem plugin = control as PackageManagerPluginItem;
                        plugin.Visible = StringSearch.StringContains(plugin.JsonObject["name"].ToString() + plugin.JsonObject["description"].ToString(), searchBox.Text);
                    } else if (control.GetType() == typeof(PackageManagerIconPackItem))
                    {
                        PackageManagerIconPackItem iconPack = control as PackageManagerIconPackItem;
                        iconPack.Visible = StringSearch.StringContains(iconPack.JsonObject["name"].ToString() + iconPack.JsonObject["description"].ToString(), searchBox.Text);
                    }

                }
            } else
            {
                foreach (Control control in panelAvailablePlugins.Controls)
                {
                    control.Visible = true;

                }
            }
        }

        private void RadioAll_CheckedChanged(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                this.LoadAvailablePlugins();
            });
        }

        private void SeachBoxInstalledPlugins_TextChanged(object sender, EventArgs e)
        {
            if(seachBoxInstalledPlugins.Text.Length > 1)
            {
                foreach (PluginDetails plugin in installedPluginsPanel.Controls)
                {
                    plugin.Visible = StringSearch.StringContains(plugin.Plugin.Name.ToString() + plugin.Plugin.Description.ToString(), seachBoxInstalledPlugins.Text);
                }
            } else
            {
                foreach (PluginDetails plugin in installedPluginsPanel.Controls)
                {
                    plugin.Visible = true;
                }
            }
        }

        private void Type_CheckedChanged(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                this.LoadAvailablePlugins();
            });
        }

    }
}
