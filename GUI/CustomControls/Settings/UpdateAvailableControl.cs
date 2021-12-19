using Newtonsoft.Json.Linq;
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

namespace SuchByte.MacroDeck.GUI.CustomControls.Settings
{
    public partial class UpdateAvailableControl : UserControl
    {
        public UpdateAvailableControl()
        {
            InitializeComponent();

            this.lblVersion.Text = String.Format(Language.LanguageManager.Strings.VersionXIsNowAvailable, Updater.Updater.UpdateObject["version"], Updater.Updater.UpdateObject["channel"]);
            this.btnInstall.Text = Language.LanguageManager.Strings.DownloadAndInstall;
            this.lblSize.Text = Updater.Updater.UpdateSizeMb.ToString("0.##") + "MB";
            //this.lblStatus.Text = Language.LanguageManager.Strings.ReadyToDownloadUpdate;

            Updater.Updater.OnProgressChanged += ProgressChanged;
            Updater.Updater.OnError += Error;

            string changelogXml = Updater.Updater.UpdateObject["changelog"].ToString();

            if (!string.IsNullOrWhiteSpace(changelogXml))
            {
                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(changelogXml);
                    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    {
                        if (node.HasChildNodes == true)
                        {
                            Label title = new Label
                            {
                                AutoSize = true,
                                MinimumSize = new Size(880, 0),
                                MaximumSize = new Size(880, 0),
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
                                        MinimumSize = new Size(880, 0),
                                        MaximumSize = new Size(880, 0),
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
                                MinimumSize = new Size(880, 15),
                                MaximumSize = new Size(880, 15)
                            };
                            this.changelogPanel.Controls.Add(spacer);
                        }
                    }
                } catch { }
            }

            

            if (Updater.Updater.Downloading)
            {
                this.btnInstall.Enabled = false;
                this.btnInstall.Progress = Updater.Updater.ProgressPercentage;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            this.btnInstall.Enabled = false;
            Updater.Updater.DownloadUpdate();
           
        }

        private void ProgressChanged(object sender, Updater.ProgressChangedEventArgs e)
        {
            this.btnInstall.Enabled = false;
            this.btnInstall.Progress = e.ProgressPercentage;
        }

        private void Error(object sender, EventArgs e)
        {
            this.btnInstall.Progress = 0;
            this.btnInstall.Enabled = true;
        }
        

        
    }
}
