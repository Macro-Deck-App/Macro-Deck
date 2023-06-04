using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using SuchByte.MacroDeck.Backups;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Updater;

namespace SuchByte.MacroDeck.GUI.CustomControls.Settings;

public partial class UpdateAvailableControl : UserControl
{
    public UpdateAvailableControl()
    {
        InitializeComponent();

        lblVersion.Text = string.Format(LanguageManager.Strings.VersionXIsNowAvailable, Updater.Updater.UpdateObject["version"], Updater.Updater.UpdateObject["channel"]);
        btnInstall.Text = LanguageManager.Strings.DownloadAndInstall;
        lblSize.Text = Updater.Updater.UpdateSizeMb.ToString("0.##") + "MB";
        //this.lblStatus.Text = Language.LanguageManager.Strings.ReadyToDownloadUpdate;

        Updater.Updater.OnProgressChanged += ProgressChanged;
        Updater.Updater.OnError += Error;

        var changelogXml = Updater.Updater.UpdateObject["changelog"].ToString();

        if (!string.IsNullOrWhiteSpace(changelogXml))
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(changelogXml);
                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.HasChildNodes)
                    {
                        var title = new Label
                        {
                            AutoSize = true,
                            MinimumSize = new Size(880, 0),
                            MaximumSize = new Size(880, 0),
                            Font = new Font("Tahoma", 14F, FontStyle.Bold, GraphicsUnit.Point),
                            ForeColor = Color.White,
                            Text = node.Name
                        };
                        changelogPanel.Controls.Add(title);
                        foreach (XmlNode childNode in node.ChildNodes)
                        {
                            if (childNode.Name == "Text")
                            {
                                var text = new Label
                                {
                                    AutoSize = true,
                                    MinimumSize = new Size(880, 0),
                                    MaximumSize = new Size(880, 0),
                                    Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point),
                                    ForeColor = Color.White,
                                    Text = "● " + childNode.InnerText
                                };
                                changelogPanel.Controls.Add(text);
                            }
                        }
                        var spacer = new Panel
                        {
                            AutoSize = true,
                            MinimumSize = new Size(880, 15),
                            MaximumSize = new Size(880, 15)
                        };
                        changelogPanel.Controls.Add(spacer);
                    }
                }
            } catch { }
        }

            

        if (Updater.Updater.Downloading)
        {
            btnInstall.Enabled = false;
            btnInstall.Progress = Updater.Updater.ProgressPercentage;
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void BtnInstall_Click(object sender, EventArgs e)
    {
        btnInstall.Enabled = false;
        btnInstall.Spinner = true;
        var createBackup = false;
        using (var msgBox = new MessageBox())
        {
            if (msgBox.ShowDialog(LanguageManager.Strings.Backup, LanguageManager.Strings.CreateBackupBeforeUpdate, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                createBackup = true;
            }
        }
        if (createBackup)
        {
            btnInstall.Text = LanguageManager.Strings.CreatingBackup;
            BackupManager.CreateBackup();
        }
        Updater.Updater.DownloadUpdate();
           
    }

    private void ProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        btnInstall.Enabled = false;
        btnInstall.Spinner = true;
        btnInstall.Progress = e.ProgressPercentage;
    }

    private void Error(object sender, EventArgs e)
    {
        btnInstall.Progress = 0;
        btnInstall.Enabled = true;
        btnInstall.Spinner = false;
    }
        

        
}