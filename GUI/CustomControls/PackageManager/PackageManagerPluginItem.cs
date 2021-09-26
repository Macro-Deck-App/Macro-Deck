using Newtonsoft.Json.Linq;
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

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class PackageManagerPluginItem : UserControl
    {

        public event EventHandler OnInstallationFinished;

        private JObject _jsonObject;

        public JObject JsonObject { get { return this._jsonObject; } }

        public bool Installed = false;

        private bool _update = false;
        public bool UpdateAvailable { get { return this._update; } }

        private bool _silentInstall = false;

        public PackageManagerPluginItem(JObject jsonObject)
        {
            InitializeComponent();
            this.lblType.Text = Language.LanguageManager.Strings.Plugin;
            this.btnInstall.Text = Language.LanguageManager.Strings.Install;

            this._jsonObject = jsonObject;

            this.UpdateItem();

            this.MouseEnter += this.MouseEnterEvent;
            this.MouseLeave += this.MouseLeaveEvent;
        }

        private void UpdateItem()
        {
            this.lblName.Text = this._jsonObject["name"].ToString();
            this.lblAuthor.Text = this._jsonObject["author"].ToString();
            this.lblDescription.Text = this._jsonObject["description"].ToString();
            this.lblDownloads.Text = String.Format(Language.LanguageManager.Strings.XDownloads, this._jsonObject["downloads"].ToString());
            this.icon.BackgroundImage = Utils.Base64.GetImageFromBase64(this._jsonObject["icon"].ToString());
            this.lblVersion.Text = this._jsonObject["version"].ToString();

            if (PluginManager.Plugins.ContainsKey(this._jsonObject["name"].ToString()) || PluginManager.PluginsNotLoaded.ContainsKey(this._jsonObject["name"].ToString()))
            {
                this.Installed = true;
                if ((PluginManager.Plugins.ContainsKey(this._jsonObject["name"].ToString()) && PluginManager.Plugins[this._jsonObject["name"].ToString()].Version != this._jsonObject["version"].ToString()) ||
                    (PluginManager.PluginsNotLoaded.ContainsKey(this._jsonObject["name"].ToString()) && PluginManager.PluginsNotLoaded[this._jsonObject["name"].ToString()].Version.ToString() != this._jsonObject["version"].ToString()))
                {
                    this.btnInstall.Enabled = true;
                    this.btnInstall.Text = Language.LanguageManager.Strings.Update;
                    this.btnInstall.BackColor = Color.FromArgb(20, 153, 0);
                    this.lblVersion.ForeColor = Color.Red;
                    this._update = true;
                }
                else
                {
                    this.lblVersion.ForeColor = Color.Green;
                    this.btnInstall.Enabled = false;
                    this.btnInstall.Text = Language.LanguageManager.Strings.Installed;
                }
            }
        }


        private void MouseClickEvent(object sender, EventArgs e)
        {
        }

        private void MouseEnterEvent(object sender, EventArgs e)
        {
            this.BackColor = Color.FromArgb(65,65,65);
        }

        private void MouseLeaveEvent(object sender, EventArgs e)
        {
            if (!this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)))
            {
                this.BackColor = Color.FromArgb(45, 45, 45);
            }
            
        }

        public void SilentInstall()
        {
            this._silentInstall = true;
            this.Install();
        }
        
        public void Install()
        {
            this.btnInstall.Enabled = false;
            this.progressBar.Visible = true;
            
            try
            {
                if (!this._update)
                {
                    using (WebClient wc = new WebClient())
                    {
                        Task.Run(() => { wc.DownloadString("https://macrodeck.org/files/packagemanager/plugins.php?action=countdl&uuid=" + this._jsonObject["uuid"].ToString()); });
                    }
                }
            } catch {}

            using (var webClient = new WebClient())
            {
                webClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(this.WebClient_DownloadProgressChanged);
                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(this.WebClient_DownloadComplete);
                webClient.DownloadFileAsync(new Uri("https://macrodeck.org/files/packagemanager/plugins/" + this._jsonObject["uuid"] + "/" + this._jsonObject["filename"]), MacroDeck.TempDirectoryPath + this._jsonObject["filename"]);
            }
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            this.Install();
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

                ZipFile.ExtractToDirectory(MacroDeck.TempDirectoryPath + this._jsonObject["filename"], MacroDeck.TempDirectoryPath + "\\" + this._jsonObject["name"], true);
                foreach (var dll in Directory.GetFiles(MacroDeck.TempDirectoryPath + this._jsonObject["name"]))
                {
                    PluginManager.InstallPlugin(Path.GetFullPath(dll), this._silentInstall);
                }
                try
                {
                    Directory.Delete(MacroDeck.TempDirectoryPath + this._jsonObject["name"], true);
                    File.Delete(MacroDeck.TempDirectoryPath + this._jsonObject["name"]);
                }
                catch
                {

                }
                if (this._update && !this._silentInstall)
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
                if (PluginManager.PluginsUpdateAvailable.Find(plugin => plugin.Name.Equals(this._jsonObject["name"])) != null)
                {
                    PluginManager.PluginsUpdateAvailable.Remove(PluginManager.PluginsUpdateAvailable.Find(plugin => plugin.Name.Equals(this._jsonObject["name"])));
                }
                if (this.OnInstallationFinished != null)
                {
                    this.OnInstallationFinished(this, EventArgs.Empty);
                }
            }
            catch
            {
               
            }

            this.btnInstall.Enabled = true;
            this.progressBar.Visible = false;
            this.UpdateItem();
        }

        public void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            this.progressBar.Value = e.ProgressPercentage;
        }

    }
}
