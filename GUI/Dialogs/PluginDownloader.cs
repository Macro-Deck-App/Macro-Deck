using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLite;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.JSON;
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

                }
                catch { }
                
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

                    webClient.DownloadFileAsync(new Uri(url), MacroDeck.TempDirectoryPath + pluginObject["filename"]);
                }
            }
        }

        private AsyncCompletedEventHandler WebClient_DownloadCompleted(JObject jsonObject)
        {
            Action<object, AsyncCompletedEventArgs> action = (sender, e) =>
            {
                if (e.Error != null)
                {
                    Debug.WriteLine(e.Error);
                }

                

                this.statusBox.Text += "Finished download of " + jsonObject["name"] + Environment.NewLine;

                try
                {
                    if (!File.Exists(MacroDeck.TempDirectoryPath + jsonObject["filename"]))
                    {
                        this.statusBox.Text += "Download of " + jsonObject["name"] + " failed" + Environment.NewLine;

                        return;
                    }

                    using (var stream = File.OpenRead(MacroDeck.TempDirectoryPath + jsonObject["filename"]))
                    {
                        using (var md5 = MD5.Create())
                        {
                            var hash = md5.ComputeHash(stream);
                            var checksumString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                            if (!checksumString.Equals(jsonObject["md5"].ToString()))
                            {
                                this.statusBox.Text += "The md5 checksum of the downloaded file of " + jsonObject["name"] + " does not match the md5 checksum on the server. Installation aborted!" + Environment.NewLine;
                                return;
                            }
                        }
                    }

                    this.statusBox.Text += "Start installation of " + jsonObject["name"] + " version " + jsonObject["version"] + Environment.NewLine;
                    if (jsonObject["type"].ToString() == "plugin")
                    {
                        try
                        {
                            /*if (!Directory.Exists(MacroDeck.PluginsDirectoryPath))
                            {
                                Directory.CreateDirectory(MacroDeck.PluginsDirectoryPath);
                            }*/
                            var extractedDirectory = MacroDeck.TempDirectoryPath + jsonObject["uuid"];
                            ZipFile.ExtractToDirectory(MacroDeck.TempDirectoryPath + jsonObject["filename"], extractedDirectory, true);

                            PluginManager.InstallPlugin(extractedDirectory, jsonObject["uuid"].ToString(), false);

                            /*foreach (var dll in Directory.GetFiles(MacroDeck.TempDirectoryPath + jsonObject["name"]))
                            {
                                PluginManager.InstallPlugin(Path.GetFullPath(dll), true);
                            }*/
                        } catch { }
                    } else if (jsonObject["type"].ToString() == "icon-pack")
                    {                       
                        try
                        {
                            if (!Directory.Exists(MacroDeck.IconPackDirectoryPath))
                            {
                                Directory.CreateDirectory(MacroDeck.IconPackDirectoryPath);
                            }
                            File.Copy(MacroDeck.TempDirectoryPath + jsonObject["filename"], MacroDeck.IconPackDirectoryPath + jsonObject["name"] + ".db", true);
                            IconManager.LoadIconPacks();
                            if (IconManager.GetIconPackByName(jsonObject["name"].ToString()) != null)
                            {
                                IconPack iconPack = IconManager.GetIconPackByName(jsonObject["name"].ToString());
                                iconPack.PackageManagerManaged = true;
                                IconManager.SaveIconPack(iconPack);
                            }

                        } catch { }
                    }

                    try
                    {
                        if (Directory.Exists(MacroDeck.TempDirectoryPath + jsonObject["name"]))
                        {
                            Directory.Delete(MacroDeck.TempDirectoryPath + jsonObject["name"], true);
                        }
                        if (File.Exists(MacroDeck.TempDirectoryPath + jsonObject["filename"]))
                        {
                            File.Delete(MacroDeck.TempDirectoryPath + jsonObject["filename"]);
                        }
                    }
                    catch { }
                    this.statusBox.Text += "Installation of " + jsonObject["name"] + " version " + jsonObject["version"] + " completed" + Environment.NewLine;
                }
                catch
                {

                }

                this._pluginsInstalled++;
                this.totalProgress.Value = this._pluginsInstalled;
                if (this._pluginsInstalled == this._pluginsToInstall)
                {
                    this.statusBox.Text += Environment.NewLine + "*** Installation of " + _pluginsToInstall + " packages done ***" + Environment.NewLine;
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
