using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLite;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class PluginDownloader : DialogForm
    {

        private int _pluginsToInstall = 0;
        private int _pluginsInstalled = 0;

        public PluginDownloader()
        {
            InitializeComponent();
            this.lblCurrentProgress.Text = Language.LanguageManager.Strings.CurrentProgress;
            this.lblTotalProgress.Text = Language.LanguageManager.Strings.TotalProgress;
            this.SetCloseIconVisible(false);
        }

        public void InstallAll(List<JObject> plugins)
        {
            this._pluginsToInstall = plugins.Count;
            this.totalProgress.Maximum = plugins.Count;
            this.lblDownloadingPlugins.Text = String.Format(Language.LanguageManager.Strings.DownloadingXPackages, plugins.Count);
            foreach (JObject pluginObject in plugins)
            {
                this.statusBox.Text += "Downloading " + pluginObject["name"] + " version " + pluginObject["version"] + Environment.NewLine;
                MacroDeckLogger.Info("Downloading " + pluginObject["name"] + " version " + pluginObject["version"]);

                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        switch (pluginObject["type"].ToString())
                        {
                            case "plugin":
                                Task.Run(() => { wc.DownloadString("https://macrodeck.org/files/packagemanager/plugins.php?action=countdl&uuid=" + pluginObject["uuid"].ToString()); });
                                break;
                            case "icon-pack":
                                Task.Run(() => { wc.DownloadString("https://macrodeck.org/files/packagemanager/iconpacks.php?action=countdl&uuid=" + pluginObject["uuid"].ToString()); });
                                break;
                        }
                        
                        
                    }

                } catch {}
                
                using (var webClient = new WebClient())
                {
                    webClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(this.WebClient_DownloadProgressChanged);
                    webClient.DownloadFileCompleted += this.WebClient_DownloadCompleted(pluginObject);
                    string url = "";
                    switch (pluginObject["type"].ToString())
                    {
                        case "plugin":
                            url = "https://macrodeck.org/files/packagemanager/plugins/" + pluginObject["uuid"] + "/" + pluginObject["filename"];
                            break;
                        case "icon-pack":
                            url = "https://macrodeck.org/files/packagemanager/iconpacks/" + pluginObject["uuid"] + "/" + pluginObject["filename"];
                            break;
                    }

                    webClient.DownloadFileAsync(new Uri(url), Path.Combine(MacroDeck.TempDirectoryPath, pluginObject["filename"].ToString()));
                }
            }
        }

        private AsyncCompletedEventHandler WebClient_DownloadCompleted(JObject jsonObject)
        {
            Action<object, AsyncCompletedEventArgs> action = (sender, e) =>
            {
                MacroDeckLogger.Info("Initial setup packages download completed");
                if (e.Error != null)
                {
                    MacroDeckLogger.Error("Plugin download failed: " + e.Error.Message);
                }

                this.statusBox.Text += "Finished download of " + jsonObject["name"] + Environment.NewLine;

                if (!File.Exists(Path.Combine(MacroDeck.TempDirectoryPath, jsonObject["filename"].ToString())))
                {
                    this.statusBox.Text += "Download of " + jsonObject["name"] + " failed" + Environment.NewLine;
                    MacroDeckLogger.Error("Initial setup downloaded package not found");
                    return;
                }

                using (var stream = File.OpenRead(Path.Combine(MacroDeck.TempDirectoryPath, jsonObject["filename"].ToString())))
                {
                    using (var md5 = MD5.Create())
                    {
                        var hash = md5.ComputeHash(stream);
                        var checksumString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        if (!checksumString.Equals(jsonObject["md5"].ToString()))
                        {
                            this.statusBox.Text += "The md5 checksum of the downloaded file of " + jsonObject["name"] + " does not match the md5 checksum on the server. Installation aborted!" + Environment.NewLine;
                            MacroDeckLogger.Error("md5 checksum of initial setup downloaded package not matching!" + Environment.NewLine +
                                                    "md5 checksum on server: " + jsonObject["md5"].ToString() + Environment.NewLine +
                                                    "md5 checksum of downloaded file: " + checksumString);
                            return;
                        }
                    }
                }

                this.statusBox.Text += "Start installation of " + jsonObject["name"] + " version " + jsonObject["version"] + Environment.NewLine;
                MacroDeckLogger.Info("Start installation of " + jsonObject["name"] + " version " + jsonObject["version"]);
                switch (jsonObject["type"].ToString())
                {
                    case "plugin":
                        try
                        {
                            var extractedDirectory = Path.Combine(MacroDeck.TempDirectoryPath, jsonObject["uuid"].ToString());
                            ZipFile.ExtractToDirectory(Path.Combine(MacroDeck.TempDirectoryPath, jsonObject["filename"].ToString()), extractedDirectory, true);

                            PluginManager.InstallPlugin(extractedDirectory, jsonObject["uuid"].ToString());
                        }
                        catch (Exception ex)
                        {
                            MacroDeckLogger.Error("Error while installing plugin after initial setup: " + ex.Message + Environment.NewLine + ex.StackTrace);
                        }
                        break;
                    case "icon-pack":
                        try
                        {
                            if (!Directory.Exists(MacroDeck.IconPackDirectoryPath))
                            {
                                Directory.CreateDirectory(MacroDeck.IconPackDirectoryPath);
                            }
                            File.Copy(Path.Combine(MacroDeck.TempDirectoryPath, jsonObject["filename"].ToString()), Path.Combine(MacroDeck.IconPackDirectoryPath, jsonObject["name"].ToString() + ".db"), true);
                            IconManager.LoadIconPacks();
                            if (IconManager.GetIconPackByName(jsonObject["name"].ToString()) != null)
                            {
                                IconPack iconPack = IconManager.GetIconPackByName(jsonObject["name"].ToString());
                                iconPack.PackageManagerManaged = true;
                                IconManager.SaveIconPack(iconPack);
                            }

                        }
                        catch (Exception ex)
                        {
                            MacroDeckLogger.Error("Error while installing icon pack after initial setup: " + ex.Message + Environment.NewLine + ex.StackTrace);
                        }
                        break;
                }

                try
                {
                    if (Directory.Exists(Path.Combine(MacroDeck.TempDirectoryPath, jsonObject["name"].ToString())))
                    {
                        Directory.Delete(Path.Combine(MacroDeck.TempDirectoryPath, jsonObject["name"].ToString()), true);
                    }
                    if (File.Exists(Path.Combine(MacroDeck.TempDirectoryPath, jsonObject["filename"].ToString())))
                    {
                        File.Delete(Path.Combine(MacroDeck.TempDirectoryPath, jsonObject["filename"].ToString()));
                    }
                }
                catch { }
                this.statusBox.Text += "Installation of " + jsonObject["name"] + " version " + jsonObject["version"] + " completed" + Environment.NewLine;
                MacroDeckLogger.Info("Installation of " + jsonObject["name"] + " version " + jsonObject["version"] + " completed");

                this._pluginsInstalled++;
                this.totalProgress.Value = this._pluginsInstalled;
                if (this._pluginsInstalled == this._pluginsToInstall)
                {
                    this.statusBox.Text += Environment.NewLine + "*** Installation of " + _pluginsToInstall + " packages done ***" + Environment.NewLine;
                    MacroDeckLogger.Info("*** Installation of " + _pluginsToInstall + " packages done ***");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }

            };
            
            return new AsyncCompletedEventHandler(action);
        }


        public void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            this.currentProgress.Value = e.ProgressPercentage;
        }

        private void BtnFinish_Click(object sender, EventArgs e)
        {
            
        }
    }

}
