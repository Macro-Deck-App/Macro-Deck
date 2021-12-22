using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Icons;
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
    public partial class PackageManagerIconPackItem : RoundedUserControl
    {
        public event EventHandler OnInstallationFinished;

        private JObject _jsonObject;

        public JObject JsonObject { get { return this._jsonObject; } }

        public bool Installed = false;

        private bool _update = false;
        public bool UpdateAvailable { get { return this._update; } }

        private bool _silentInstall = false;

        private IconPack _iconPack;

        public PackageManagerIconPackItem(JObject jsonObject)
        {
            InitializeComponent();
            this.lblType.Text = Language.LanguageManager.Strings.IconPack;
            this.btnInstall.Text = Language.LanguageManager.Strings.Install;


            this._jsonObject = jsonObject;

            this.UpdateItem();
        }

        public PackageManagerIconPackItem(IconPack iconPack)
        {
            InitializeComponent();
            this.lblType.Text = Language.LanguageManager.Strings.IconPack;
            this.btnInstall.Text = Language.LanguageManager.Strings.Install;

            this._iconPack = iconPack;

            this._jsonObject = new JObject
            {
                ["name"] = iconPack.Name,
                ["author"] = iconPack.Author,
                ["version"] = iconPack.Version,
                ["description"] = iconPack.Description,
                ["license"] = iconPack.License,
                ["preview"] = iconPack.IconPreviewBase64,
            };
            this.UpdateItem();
        }

        private void UpdateItem()
        {
            this.lblName.Text = this._jsonObject["name"].ToString();
            this.lblAuthor.Text = Language.LanguageManager.Strings.By + " " +  this._jsonObject["author"].ToString();
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
            }
            else
            {
                this.lblDownloads.Visible = false;
                this.iconDownloads.Visible = false;
            }
            if (this._jsonObject["category"] != null)
            {
                this.lblCategory.Text = this._jsonObject["category"].ToString();
            }
            else
            {
                this.lblCategory.Text = "";
            }
            if (this._jsonObject["license"] != null && !String.IsNullOrWhiteSpace(this._jsonObject["license"].ToString()))
            {
                this.lblLicense.Visible = true;
            } else
            {
                this.lblLicense.Visible = false;
            }
            if (this._jsonObject["preview"] != null && !String.IsNullOrWhiteSpace(this._jsonObject["preview"].ToString()))
            {
                this.preview.BackgroundImage = Utils.Base64.GetImageFromBase64(this._jsonObject["preview"].ToString());
            } else
            {
                try
                {
                    this.preview.BackgroundImage = Utils.IconPackPreview.GeneratePreviewImage(this._iconPack);
                } catch { }
            }
            this.lblVersion.Text = this._jsonObject["version"].ToString();

            if (IconManager.GetIconPackByName(this._jsonObject["name"].ToString()) != null)
            {
                this.Installed = true;
                IconPack iconPack = IconManager.GetIconPackByName(this._jsonObject["name"].ToString());
                if (iconPack.PackageManagerManaged && iconPack.Author.Equals(this._jsonObject["author"].ToString()))
                {
                    this.Installed = true;
                    if (iconPack.Version != this._jsonObject["version"].ToString())
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
                        this.btnInstall.Enabled = true;
                        this.btnInstall.Text = Language.LanguageManager.Strings.Uninstall;
                        this.btnInstall.BackColor = Color.FromArgb(65, 65, 65);
                        this._update = false;
                    }

                }
            } else
            {
                this.Installed = false;
                this.btnInstall.Enabled = true;
                this.btnInstall.Text = Language.LanguageManager.Strings.Install;
                this.btnInstall.BackColor = Color.FromArgb(0, 123, 255);
                this.lblVersion.ForeColor = Color.White;
                this._update = false;
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

        public void Install()
        {
            this.btnInstall.Enabled = false;

            try
            {
                if (!this._update)
                {
                    using (WebClient wc = new WebClient())
                    {
                        Task.Run(() => { wc.DownloadString("https://macrodeck.org/files/packagemanager/iconpacks.php?action=countdl&uuid=" + this._jsonObject["uuid"].ToString()); });
                    }
                }
            }
            catch { }

            using (var webClient = new WebClient())
            {
                webClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(this.WebClient_DownloadProgressChanged);
                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(this.WebClient_DownloadComplete);
                webClient.DownloadFileAsync(new Uri("https://macrodeck.org/files/packagemanager/iconpacks/" + this._jsonObject["uuid"] + "/" + this._jsonObject["filename"]), Path.Combine(MacroDeck.TempDirectoryPath, this._jsonObject["filename"].ToString()));
            }
        }

        public void Uninstall()
        {
            try
            {
                using (var msgBox = new MessageBox())
                {
                    if (msgBox.ShowDialog(Language.LanguageManager.Strings.AreYouSure, String.Format(Language.LanguageManager.Strings.XWillBeUninstalled, this._jsonObject["name"].ToString()), MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        IconManager.DeleteIconPack(IconManager.GetIconPackByName(this._jsonObject["name"].ToString()));
                        this.UpdateItem();
                    }
                }

            } catch { }
        }

        private void WebClient_DownloadComplete(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                if (!File.Exists(Path.Combine(MacroDeck.TempDirectoryPath, this._jsonObject["filename"].ToString())))
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

                using (var stream = File.OpenRead(Path.Combine(MacroDeck.TempDirectoryPath, this._jsonObject["filename"].ToString())))
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
                                }
                            }
                            if (this.OnInstallationFinished != null)
                            {
                                this.OnInstallationFinished(this, EventArgs.Empty);
                            }
                            this.btnInstall.Enabled = true;
                            return;
                        }
                    }
                }
                                
                try
                {
                    File.Copy(Path.Combine(MacroDeck.TempDirectoryPath, this._jsonObject["filename"].ToString()), Path.Combine(MacroDeck.IconPackDirectoryPath, this._jsonObject["name"].ToString() + ".iconpack"), true);
                    File.Delete(Path.Combine(MacroDeck.TempDirectoryPath, this._jsonObject["name"].ToString()));
                }
                catch
                {
                }

                IconManager.LoadIconPacks();
                if (IconManager.GetIconPackByName(this._jsonObject["name"].ToString()) != null)
                {
                    IconPack iconPack = IconManager.GetIconPackByName(this._jsonObject["name"].ToString());
                    iconPack.PackageManagerManaged = true;
                    IconManager.SaveIconPack(iconPack);

                    if (IconManager.IconPacksUpdateAvailable.Contains(iconPack))
                    {
                        IconManager.IconPacksUpdateAvailable.Remove(iconPack);
                    }
                }
                if (this.OnInstallationFinished != null)
                {
                    this.OnInstallationFinished(this, EventArgs.Empty);
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

        private void lblLicense_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (this._jsonObject["license"] != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = this._jsonObject["license"].ToString(),
                    UseShellExecute = true
                });
            }
        }
    }
}
