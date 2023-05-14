using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using SuchByte.MacroDeck.Backups;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.CustomControls.Settings;

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
        lblFileName.Text = new FileInfo(macroDeckBackupInfo.FileName).Name;
        lblDateCreated.Text = LanguageManager.Strings.Created + ": " + macroDeckBackupInfo.BackupCreated.ToString("d") + " - " + macroDeckBackupInfo.BackupCreated.ToString("t");
        lblSize.Text = macroDeckBackupInfo.SizeMb.ToString("0.##") + "MB";
    }

    private void BtnRestore_Click(object sender, EventArgs e)
    {
        using var restoreBackupDialog = new RestoreBackupDialog();
        if (restoreBackupDialog.ShowDialog() == DialogResult.OK)
        {
            using var msgBox = new MessageBox();
            if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, LanguageManager.Strings.ActionCannotBeReversed, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                Task.Run(() =>
                {
                    BackupManager.RestoreBackup(macroDeckBackupInfo.FileName, restoreBackupDialog.RestoreBackupInfo);
                    SpinnerDialog.SetVisisble(false, ParentForm);
                });
                SpinnerDialog.SetVisisble(true, ParentForm);
            }
        }
    }

    private void btnDelete_Click(object sender, EventArgs e)
    {
        using var msgBox = new MessageBox();
        if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, LanguageManager.Strings.ThisWillDeleteBackupPermanently, MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            BackupManager.DeleteBackup(macroDeckBackupInfo.FileName);
        }
    }
}