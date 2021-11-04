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
            //this.tabAvailable.Text = Language.LanguageManager.Strings.PackageManagerTabAvailable;
            //this.tabInstalled.Text = Language.LanguageManager.Strings.PackageManagerTabInstalled;
            this.radioInstalled.Text = Language.LanguageManager.Strings.PackageManagerTabInstalled;
            this.checkPlugins.Text = Language.LanguageManager.Strings.Plugins;
            this.checkIconPacks.Text = Language.LanguageManager.Strings.IconPacks;
            this.radioAll.Text = Language.LanguageManager.Strings.Online;
            this.radioOnlyUpdates.Text = Language.LanguageManager.Strings.Updates;
            this.searchBox.PlaceHolderText = Language.LanguageManager.Strings.Search;
            //his.seachBoxInstalledPlugins.PlaceHolderText = Language.LanguageManager.Strings.Search;
            //this.btnOpenPluginsFolder.Text = Language.LanguageManager.Strings.InstallDLL;
        }

        private void PackageManager_Load(object sender, EventArgs e)
        {
            //this.RefreshInstalledPluginsList();
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
                this.radioInstalled.Text = String.Format(Language.LanguageManager.Strings.PackageManagerTabInstalled + " ({0})", PluginManager.PluginsNotLoaded.Count + PluginManager.Plugins.Count - PluginManager.ProtectedPlugins.Count + IconManager.IconPacks.FindAll(iP => iP.PackageManagerManaged).Count);
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
                    if (!this.radioInstalled.Checked)
                    {
                        using (WebClient wc = new WebClient())
                        {
                            var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/plugins.php?target-api=" + MacroDeck.PluginApiVersion);

                            JArray jsonArray = JArray.Parse(jsonString);
                            
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
                    } else
                    {
                        foreach (MacroDeckPlugin macroDeckPlugin in PluginManager.Plugins.Values)
                        {
                            if (PluginManager.ProtectedPlugins.Contains(macroDeckPlugin)) continue;
                            PackageManagerPluginItem packageManagerItem = new PackageManagerPluginItem(macroDeckPlugin);
                            this.Invoke(new Action(() =>
                                this.AddPackageManagerItem(packageManagerItem)
                            ));
                        }
                        foreach (MacroDeckPlugin macroDeckPlugin in PluginManager.PluginsNotLoaded.Values)
                        {
                            if (PluginManager.ProtectedPlugins.Contains(macroDeckPlugin)) continue;
                            PackageManagerPluginItem packageManagerItem = new PackageManagerPluginItem(macroDeckPlugin);
                            this.Invoke(new Action(() =>
                                this.AddPackageManagerItem(packageManagerItem)
                            ));
                        }
                    }
                }
                catch { }
            }

            if (this.checkIconPacks.Checked)
            {
                try
                {
                    if (!this.radioInstalled.Checked)
                    {
                        using (WebClient wc = new WebClient())
                        {
                            var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/iconpacks.php?target-api=" + MacroDeck.PluginApiVersion);

                            JArray jsonArray = JArray.Parse(jsonString);


                            foreach (JObject jsonObject in jsonArray)
                            {
                                PackageManagerIconPackItem packageManagerIconPackItem = new PackageManagerIconPackItem(jsonObject);
                                if ((this.radioAll.Checked) || (this.radioInstalled.Checked && packageManagerIconPackItem.Installed) || (this.radioOnlyUpdates.Checked && packageManagerIconPackItem.UpdateAvailable))
                                {
                                    this.Invoke(new Action(() =>
                                            this.AddPackageManagerIconPackItem(packageManagerIconPackItem)
                                    ));
                                    packageManagerIconPackItem.OnInstallationFinished += this.OnInstallationFinished;
                                }
                            }
                        }
                    } else
                    {
                        foreach (IconPack iconPack in IconManager.IconPacks.FindAll(iP => iP.PackageManagerManaged))
                        {
                            PackageManagerIconPackItem packageManagerIconPackItem = new PackageManagerIconPackItem(iconPack);
                            this.Invoke(new Action(() =>
                                this.AddPackageManagerIconPackItem(packageManagerIconPackItem)
                            ));
                        }
                    }
                }
                catch { }
            }
            this.Invoke(new Action(() =>
                progressBar.Visible = false
            ));
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
            });
        }

        private void OnPluginsChanged(object sender, EventArgs e)
        {
            try
            {
                Task.Run(() =>
                {
                    this.LoadAvailablePlugins();
                });
            } catch { }
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
                        plugin.Visible = StringSearch.StringContains(plugin.JsonObject["category"].ToString() + plugin.JsonObject["name"].ToString() + plugin.JsonObject["description"].ToString(), searchBox.Text);
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

        private void Type_CheckedChanged(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                this.LoadAvailablePlugins();
            });
        }

        private void searchBox_Load(object sender, EventArgs e)
        {

        }
    }
}
