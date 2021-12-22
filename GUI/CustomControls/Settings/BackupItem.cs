using SuchByte.MacroDeck.Backups;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.Settings
{
    public partial class BackupItem : RoundedUserControl
    {
        MacroDeckBackupInfo macroDeckBackupInfo;

        public BackupItem(MacroDeckBackupInfo macroDeckBackupInfo)
        {
            this.macroDeckBackupInfo = macroDeckBackupInfo;
            InitializeComponent();
        }

        private void BackupItem_Load(object sender, EventArgs e)
        {
            this.lblFileName.Text = new FileInfo(this.macroDeckBackupInfo.FileName).Name;
            this.lblDateCreated.Text = LanguageManager.Strings.Created + ": " + this.macroDeckBackupInfo.BackupCreated.ToString("d") + " - " + this.macroDeckBackupInfo.BackupCreated.ToString("t");
            this.lblSize.Text = this.macroDeckBackupInfo.SizeMb.ToString("0.##") + "MB";
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            using (var restoreBackupDialog = new RestoreBackupDialog())
            {
                if (restoreBackupDialog.ShowDialog() == DialogResult.OK)
                {
                    using (var msgBox = new MessageBox())
                    {
                        if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, LanguageManager.Strings.ActionCannotBeReversed, MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            Task.Run(() =>
                            {
                                BackupManager.RestoreBackup(this.macroDeckBackupInfo.FileName, restoreBackupDialog.RestoreBackupInfo);
                                SpinnerDialog.SetVisisble(false, this.ParentForm);
                            });
                            SpinnerDialog.SetVisisble(true, this.ParentForm);
                        }
                    }
                }
            }
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            using (var msgBox = new MessageBox())
            {
                if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, LanguageManager.Strings.ThisWillDeleteBackupPermanently, MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    BackupManager.DeleteBackup(this.macroDeckBackupInfo.FileName);
                }
            }
        }
    }
}
