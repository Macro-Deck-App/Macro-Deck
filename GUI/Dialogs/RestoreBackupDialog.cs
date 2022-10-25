using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.Backup;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class RestoreBackupDialog : DialogForm
    {
        public RestoreBackupDialog()
        {
            InitializeComponent();

            lblWhatToRestore.Text = LanguageManager.Strings.WhatDoYouWantToRestore;
            checkRestoreConfig.Text = LanguageManager.Strings.Configuration;
            checkRestoreProfiles.Text = LanguageManager.Strings.Profiles;
            checkRestoreDevices.Text = LanguageManager.Strings.KnownDevices;
            checkRestoreVariables.Text = LanguageManager.Strings.Variables;
            checkRestorePlugins.Text = LanguageManager.Strings.InstalledPlugins;
            checkRestorePluginConfigs.Text = LanguageManager.Strings.PluginConfigurations;
            checkRestorePluginCredentials.Text = LanguageManager.Strings.PluginCredentials;
            checkRestoreIconPacks.Text = LanguageManager.Strings.InstalledIconPacks;
            btnRestore.Text = LanguageManager.Strings.Restore;
        }


        public RestoreBackupInfo RestoreBackupInfo
        {
            get
            {
                var restoreBackupInfo = new RestoreBackupInfo
                {
                    RestoreConfig = checkRestoreConfig.Checked,
                    RestoreProfiles = checkRestoreProfiles.Checked,
                    RestoreDevices = checkRestoreDevices.Checked,
                    RestoreVariables = checkRestoreVariables.Checked,
                    RestorePlugins = checkRestorePlugins.Checked,
                    RestorePluginConfigs = checkRestorePluginConfigs.Checked,
                    RestorePluginCredentials = checkRestorePluginCredentials.Checked,
                    RestoreIconPacks = checkRestoreIconPacks.Checked
                };

                return restoreBackupInfo;
            }
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
