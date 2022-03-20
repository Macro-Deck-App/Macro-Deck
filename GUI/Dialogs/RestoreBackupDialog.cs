using SuchByte.MacroDeck.Backup;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class RestoreBackupDialog : DialogForm
    {
        public RestoreBackupDialog()
        {
            InitializeComponent();

            this.lblWhatToRestore.Text = LanguageManager.Strings.WhatDoYouWantToRestore;
            this.checkRestoreConfig.Text = LanguageManager.Strings.Configuration;
            this.checkRestoreProfiles.Text = LanguageManager.Strings.Profiles;
            this.checkRestoreDevices.Text = LanguageManager.Strings.KnownDevices;
            this.checkRestoreVariables.Text = LanguageManager.Strings.Variables;
            this.checkRestorePlugins.Text = LanguageManager.Strings.InstalledPlugins;
            this.checkRestorePluginConfigs.Text = LanguageManager.Strings.PluginConfigurations;
            this.checkRestorePluginCredentials.Text = LanguageManager.Strings.PluginCredentials;
            this.checkRestoreIconPacks.Text = LanguageManager.Strings.InstalledIconPacks;
            this.btnRestore.Text = LanguageManager.Strings.Restore;
        }


        public RestoreBackupInfo RestoreBackupInfo
        {
            get
            {
                RestoreBackupInfo restoreBackupInfo = new RestoreBackupInfo()
                {
                    RestoreConfig = this.checkRestoreConfig.Checked,
                    RestoreProfiles = this.checkRestoreProfiles.Checked,
                    RestoreDevices = this.checkRestoreDevices.Checked,
                    RestoreVariables = this.checkRestoreVariables.Checked,
                    RestorePlugins = this.checkRestorePlugins.Checked,
                    RestorePluginConfigs = this.checkRestorePluginConfigs.Checked,
                    RestorePluginCredentials = this.checkRestorePluginCredentials.Checked,
                    RestoreIconPacks = this.checkRestoreIconPacks.Checked
                };

                return restoreBackupInfo;
            }
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
