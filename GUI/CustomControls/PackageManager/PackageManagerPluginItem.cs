using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class PackageManagerPluginItem : RoundedUserControl
    {

        public event EventHandler OnInstallationFinished;

        private JObject _jsonObject;

        public JObject JsonObject { get { return this._jsonObject; } }

        public bool Installed = false;

        private bool _update = false;
        public bool UpdateAvailable { get { return this._update; } }

        private bool _silentInstall = false;

        private MacroDeckPlugin _macroDeckPlugin;

        public PackageManagerPluginItem(JObject jsonObject)
        {
            InitializeComponent();
            this.lblType.Text = Language.LanguageManager.Strings.Plugin;
            this.btnInstall.Text = Language.LanguageManager.Strings.Install;
            this.btnConfigure.Text = Language.LanguageManager.Strings.Configure;

            this._jsonObject = jsonObject;
            if (PluginManager.Plugins.ContainsKey(jsonObject["name"].ToString()))
            {
                this._macroDeckPlugin = PluginManager.Plugins[jsonObject["name"].ToString()];
            }
            if (PluginManager.PluginsNotLoaded.ContainsKey(jsonObject["name"].ToString()))
            {
                this._macroDeckPlugin = PluginManager.PluginsNotLoaded[jsonObject["name"].ToString()];
            }

            this.UpdateItem();
        }

        public PackageManagerPluginItem(MacroDeckPlugin plugin)
        {
            InitializeComponent();
            this.lblType.Text = Language.LanguageManager.Strings.Plugin;
            this.btnInstall.Text = Language.LanguageManager.Strings.Install;
            this.btnConfigure.Text = Language.LanguageManager.Strings.Configure;

            this._jsonObject = new JObject
            {
                ["name"] = plugin.Name,
                ["author"] = plugin.Author,
                ["version"] = plugin.Version,
                ["description"] = plugin.Description,
                ["category"] = "",
            };

            this._macroDeckPlugin = plugin;

            this.UpdateItem();
        }



        private void UpdateItem()
        {
            this.lblName.Text = this._jsonObject["name"].ToString();
            this.lblAuthor.Text = Language.LanguageManager.Strings.By + " " + this._jsonObject["author"].ToString();
            this.lblDescription.Text = this._jsonObject["description"].ToString();
            if (this._jsonObject["downloads"] != null)
            {
                this.lblDownloads.Visible = true;
                this.iconDownloads.Visible = true;
                int downloads = Int32.Parse(this._jsonObject["downloads"].ToString());
                string downloadsString = downloads.ToString();
                if (downloads >= 1000)
                {
                    downloadsString = ((float)downloads / 1000.0).ToString("0.0") + "k";
                }
                if (downloads >= 1000000)
                {
                    downloadsString = ((float)downloads / 1000000.0).ToString("0.0") + "m";
                }
                this.lblDownloads.Text = downloadsString;
            } else
            {
                this.lblDownloads.Visible = false;
                this.iconDownloads.Visible = false;
            }
            if (this._macroDeckPlugin != null)
            {
                this.icon.BackgroundImage = this._macroDeckPlugin.Icon == null ? Properties.Resources.Icon : this._macroDeckPlugin.Icon;
            } else
            {
                this.icon.BackgroundImage = Base64.GetImageFromBase64(this._jsonObject["icon"].ToString());
            }
            if (this._jsonObject["category"] != null)
            {
                this.lblCategory.Text = this._jsonObject["category"].ToString();
            } else
            {
                this.lblCategory.Text = "";
            }
            
            this.lblVersion.Text = this._jsonObject["version"].ToString();

            if (PluginManager.Plugins.ContainsKey(this._jsonObject["name"].ToString()) || PluginManager.PluginsNotLoaded.ContainsKey(this._jsonObject["name"].ToString()))
            {
                this.Installed = true;
                if (((PluginManager.Plugins.ContainsKey(this._jsonObject["name"].ToString()) && PluginManager.Plugins[this._jsonObject["name"].ToString()].Version != this._jsonObject["version"].ToString()) ||
                    PluginManager.PluginsNotLoaded.ContainsKey(this._jsonObject["name"].ToString()) && PluginManager.PluginsNotLoaded[this._jsonObject["name"].ToString()].Version.ToString() != this._jsonObject["version"].ToString()) &&
                    !PluginManager.UpdatedPlugins.Contains(this._macroDeckPlugin)
                    )
                {
                    this.btnInstall.Enabled = true;
                    this.btnInstall.Text = Language.LanguageManager.Strings.Update;
                    this.btnInstall.BackColor = Color.FromArgb(20, 153, 0);
                    this.lblVersion.ForeColor = Color.Red;
                    this.lblVersion.Text = String.Format("↑ {0}", this._jsonObject["version"].ToString());
                    this._update = true;
                }
                else
                {
                    this.lblVersion.ForeColor = Color.Green;
                    this.btnInstall.Enabled = true;
                    this.btnInstall.Text = Language.LanguageManager.Strings.Uninstall;
                    this.btnInstall.BackColor = Color.FromArgb(65,65,65);
                    this._update = false;
                }

                if (PluginManager.PluginsNotLoaded.ContainsKey(this._jsonObject["name"].ToString()))
                {
                    this.lblName.ForeColor = Color.Red;
                    this.btnConfigure.Visible = false;
                }
            }
            else
            {
                this.Installed = false;
                this.btnInstall.Enabled = true;
                this.btnInstall.Text = Language.LanguageManager.Strings.Install;
                this.btnInstall.BackColor = Color.FromArgb(0, 123, 255);
                this.lblVersion.ForeColor = Color.White;
                this._update = false;
            }

            if (this.Installed)
            {
                try
                {
                    MacroDeckPlugin plugin = PluginManager.Plugins[this._jsonObject["name"].ToString()];
                    this.btnConfigure.Visible = plugin.CanConfigure;
                } catch { }
            } else
            {
                this.btnConfigure.Visible = false;
            }

            this.lblRepository.Visible = this._jsonObject["repository"] != null;

        }


        public void SilentInstall()
        {
            this._silentInstall = true;
            this.Install();
        }
        
        public void Install()
        {
            this.btnInstall.Enabled = false;
            
            try
            {
                using (WebClient wc = new WebClient())
                {
                    Task.Run(() => { wc.DownloadString("https://macrodeck.org/files/packagemanager/plugins.php?action=countdl&uuid=" + this._jsonObject["uuid"].ToString()); });
                }
            } catch {}

            using (var webClient = new WebClient())
            {
                webClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(this.WebClient_DownloadProgressChanged);
                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(this.WebClient_DownloadComplete);
                webClient.DownloadFileAsync(new Uri("https://macrodeck.org/files/packagemanager/plugins/" + this._jsonObject["uuid"] + "/" + this._jsonObject["filename"]), MacroDeck.TempDirectoryPath + this._jsonObject["filename"]);
            }
        }

        public void Uninstall()
        {
            using (var msgBox = new MessageBox())
            {
                if (msgBox.ShowDialog(Language.LanguageManager.Strings.AreYouSure, String.Format(Language.LanguageManager.Strings.XWillBeUninstalled, this._jsonObject["name"].ToString()), MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    PluginManager.DeletePlugin(this._jsonObject["name"].ToString());

                    if (msgBox.ShowDialog("", String.Format(Language.LanguageManager.Strings.XSuccessfullyUninstalled, this._jsonObject["name"].ToString()), MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        Process.Start(MacroDeck.MainDirectoryPath + AppDomain.CurrentDomain.FriendlyName, "--show");
                        Environment.Exit(0);
                    }
                }
            }
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            if (this.Installed && !this._update)
            {
                this.Uninstall();
            }
            else
            {
                this.Install();
            }
        }

        private void WebClient_DownloadComplete(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                if (!File.Exists(MacroDeck.TempDirectoryPath + this._jsonObject["filename"]))
                {
                    if (!this._silentInstall)
                    {
                        using (var msgBox = new MessageBox())
                        {
                            msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.FileNotFound, MessageBoxButtons.OK);
                            msgBox.Dispose();
                        }
                    }
                    if (this.OnInstallationFinished != null)
                    {
                        this.OnInstallationFinished(this, EventArgs.Empty);
                    }
                    return;
                }

                using (var stream = File.OpenRead(MacroDeck.TempDirectoryPath + this._jsonObject["filename"]))
                {
                    using (var md5 = MD5.Create())
                    {
                        var hash = md5.ComputeHash(stream);
                        var checksumString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        if (!checksumString.Equals(this._jsonObject["md5"].ToString()))
                        {
                            if (!this._silentInstall)
                            {
                                using (var msgBox = new MessageBox())
                                {
                                    msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.MD5NotValid, MessageBoxButtons.OK);
                                    msgBox.Dispose();
                                    
                                }
                            }
                            if (this.OnInstallationFinished != null)
                            {
                                this.OnInstallationFinished(this, EventArgs.Empty);
                            }
                            return;
                        }
                    }
                }

                var extractedDirectory = MacroDeck.TempDirectoryPath + this._jsonObject["uuid"];
                ZipFile.ExtractToDirectory(MacroDeck.TempDirectoryPath + this._jsonObject["filename"], extractedDirectory, true);

                if (PluginManager.PluginsUpdateAvailable.Contains(this._macroDeckPlugin))
                {
                    PluginManager.PluginsUpdateAvailable.Remove(this._macroDeckPlugin);
                }

                PluginManager.InstallPlugin(extractedDirectory, this._jsonObject["uuid"].ToString(), this._update);

                try
                {
                    Directory.Delete(MacroDeck.TempDirectoryPath + this._jsonObject["name"], true);
                    File.Delete(MacroDeck.TempDirectoryPath + this._jsonObject["name"]);
                }
                catch
                {

                }

                
                if (this.OnInstallationFinished != null)
                {
                    this.OnInstallationFinished(this, EventArgs.Empty);
                }

                if (this._update)
                {
                    PluginManager.UpdatedPlugins.Add(this._macroDeckPlugin);
                    if (!this._silentInstall)
                    {
                        using (var msgBox = new MessageBox())
                        {
                            if (msgBox.ShowDialog(Language.LanguageManager.Strings.MacroDeckNeedsARestart, Language.LanguageManager.Strings.MacroDeckMustBeRestartedForTheChanges, MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                Process.Start(MacroDeck.MainDirectoryPath + AppDomain.CurrentDomain.FriendlyName, "--show");
                                Environment.Exit(0);
                            }

                            msgBox.Dispose();
                        }
                    }
                }
                
                
            }
            catch
            {
               
            }
            this.btnInstall.Progress = 0;
            this.btnInstall.Enabled = true;
            this.UpdateItem();
        }

        public void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            this.btnInstall.Progress = e.ProgressPercentage;
        }

        private void BtnConfigure_Click(object sender, EventArgs e)
        {
            if (!this.Installed) return;
            try
            {
                MacroDeckPlugin plugin = PluginManager.Plugins[this._jsonObject["name"].ToString()];
                plugin.OpenConfigurator();
            } catch { }
        }

        private void lblRepository_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (this._jsonObject["repository"] != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = this._jsonObject["repository"].ToString(),
                    UseShellExecute = true
                });
            }
        }
    }
}
