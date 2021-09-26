using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.CustomControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class DownloadUpdateDialog : DialogForm
    {

        private JObject _jsonObject;

        public DownloadUpdateDialog(JObject updateObject)
        {
            this._jsonObject = updateObject;
            InitializeComponent();

            this.lblUpdateAvailable.Text = Language.LanguageManager.Strings.TheresAnUpdateForYou;
            this.lblVersion.Text = String.Format(Language.LanguageManager.Strings.VersionXIsNowAvailable, updateObject["version"], updateObject["channel"]);
            this.btnInstall.Text = Language.LanguageManager.Strings.DownloadAndInstall;
            this.lblStatus.Text = Language.LanguageManager.Strings.ReadyToDownloadUpdate;
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(updateObject["changelog"].ToString());
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.HasChildNodes == true)
                {
                    Label title = new Label
                    {
                        AutoSize = true,
                        MinimumSize = new Size(645, 0),
                        MaximumSize = new Size(645, 0),
                        Font = new Font("Tahoma", 14F, FontStyle.Bold, GraphicsUnit.Point),
                        ForeColor = Color.White,
                        Text = node.Name.ToString()
                    };
                    this.changelogPanel.Controls.Add(title);
                    foreach (XmlNode childNode in node.ChildNodes)
                    {
                        if (childNode.Name.ToString() == "Text")
                        {
                            Label text = new Label
                            {
                                AutoSize = true,
                                MinimumSize = new Size(645, 0),
                                MaximumSize = new Size(645, 0),
                                Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point),
                                ForeColor = Color.White,
                                Text = "● " + childNode.InnerText.ToString()
                            };
                            this.changelogPanel.Controls.Add(text);
                        }
                    }
                    Panel spacer = new Panel
                    {
                        AutoSize = true,
                        MinimumSize = new Size(645, 15),
                        MaximumSize = new Size(645, 15)
                    };
                    this.changelogPanel.Controls.Add(spacer);
                }                
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            this.btnInstall.Enabled = false;
            this.progressBar.Visible = true;
            using (var webClient = new WebClient())
            { 
                webClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(this.WebClient_DownloadProgressChanged);
                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(this.WebClient_DownloadComplete);
                webClient.DownloadFileAsync(new Uri("https://macrodeck.org/files/installer/" + this._jsonObject["filename"]), MacroDeck.TempDirectoryPath + this._jsonObject["filename"]);
            }
        }

        private void WebClient_DownloadComplete(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                this.lblStatus.Text = Language.LanguageManager.Strings.VerifyingUpdateFile;
                if (!File.Exists(MacroDeck.TempDirectoryPath + this._jsonObject["filename"]))
                {
                    using (var msgBox = new CustomControls.MessageBox())
                    {
                        msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.FileNotFound, MessageBoxButtons.OK);
                    }
                    this.lblStatus.Text = String.Format(Language.LanguageManager.Strings.FileNotFound);
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
                            using (var msgBox = new CustomControls.MessageBox())
                            {
                                msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.MD5NotValid, MessageBoxButtons.OK);
                            }
                            this.lblStatus.Text = String.Format(Language.LanguageManager.Strings.MD5NotValid);
                            return;
                        }
                    }
                }

                this.lblStatus.Text = String.Format(Language.LanguageManager.Strings.StartingInstaller);
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo(MacroDeck.TempDirectoryPath + this._jsonObject["filename"])
                    {
                        UseShellExecute = true
                    }
                };
                p.Start();
                Environment.Exit(0);
            }
            catch
            {
                using (var msgBox = new CustomControls.MessageBox())
                {
                    msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.TryAgainOrDownloadManually, MessageBoxButtons.OK);
                }
                this.lblStatus.Text = String.Format(Language.LanguageManager.Strings.TryAgainOrDownloadManually);
            }
        }

        public void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            this.progressBar.Value = e.ProgressPercentage;
            double megaBytesIn = Math.Round(double.Parse(e.BytesReceived.ToString()) / (1024 * 1024), 2);
            double totalMegaBytes = Math.Round(double.Parse(e.TotalBytesToReceive.ToString()) / (1024 * 1024), 2);
            this.lblStatus.Text = String.Format(Language.LanguageManager.Strings.DownloadingUpdate, e.ProgressPercentage, megaBytesIn, totalMegaBytes);
        }
    }
}
