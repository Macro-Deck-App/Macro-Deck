
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class RestoreBackupDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            checkRestoreConfig = new CheckBox();
            checkRestoreProfiles = new CheckBox();
            checkRestoreDevices = new CheckBox();
            checkRestoreVariables = new CheckBox();
            checkRestorePlugins = new CheckBox();
            checkRestorePluginConfigs = new CheckBox();
            checkRestorePluginCredentials = new CheckBox();
            checkRestoreIconPacks = new CheckBox();
            lblWhatToRestore = new Label();
            btnRestore = new ButtonPrimary();
            SuspendLayout();
            // 
            // checkRestoreConfig
            // 
            checkRestoreConfig.AutoSize = true;
            checkRestoreConfig.Location = new Point(25, 70);
            checkRestoreConfig.Name = "checkRestoreConfig";
            checkRestoreConfig.Size = new Size(102, 20);
            checkRestoreConfig.TabIndex = 2;
            checkRestoreConfig.Text = "Configuration";
            checkRestoreConfig.UseMnemonic = false;
            checkRestoreConfig.UseVisualStyleBackColor = true;
            // 
            // checkRestoreProfiles
            // 
            checkRestoreProfiles.AutoSize = true;
            checkRestoreProfiles.Location = new Point(25, 96);
            checkRestoreProfiles.Name = "checkRestoreProfiles";
            checkRestoreProfiles.Size = new Size(68, 20);
            checkRestoreProfiles.TabIndex = 3;
            checkRestoreProfiles.Text = "Profiles";
            checkRestoreProfiles.UseMnemonic = false;
            checkRestoreProfiles.UseVisualStyleBackColor = true;
            // 
            // checkRestoreDevices
            // 
            checkRestoreDevices.AutoSize = true;
            checkRestoreDevices.Location = new Point(25, 122);
            checkRestoreDevices.Name = "checkRestoreDevices";
            checkRestoreDevices.Size = new Size(69, 20);
            checkRestoreDevices.TabIndex = 4;
            checkRestoreDevices.Text = "Devices";
            checkRestoreDevices.UseMnemonic = false;
            checkRestoreDevices.UseVisualStyleBackColor = true;
            // 
            // checkRestoreVariables
            // 
            checkRestoreVariables.AutoSize = true;
            checkRestoreVariables.Location = new Point(25, 148);
            checkRestoreVariables.Name = "checkRestoreVariables";
            checkRestoreVariables.Size = new Size(79, 20);
            checkRestoreVariables.TabIndex = 5;
            checkRestoreVariables.Text = "Variables";
            checkRestoreVariables.UseMnemonic = false;
            checkRestoreVariables.UseVisualStyleBackColor = true;
            // 
            // checkRestorePlugins
            // 
            checkRestorePlugins.AutoSize = true;
            checkRestorePlugins.Location = new Point(25, 174);
            checkRestorePlugins.Name = "checkRestorePlugins";
            checkRestorePlugins.Size = new Size(118, 20);
            checkRestorePlugins.TabIndex = 6;
            checkRestorePlugins.Text = "Installed plugins";
            checkRestorePlugins.UseMnemonic = false;
            checkRestorePlugins.UseVisualStyleBackColor = true;
            // 
            // checkRestorePluginConfigs
            // 
            checkRestorePluginConfigs.AutoSize = true;
            checkRestorePluginConfigs.Location = new Point(25, 200);
            checkRestorePluginConfigs.Name = "checkRestorePluginConfigs";
            checkRestorePluginConfigs.Size = new Size(144, 20);
            checkRestorePluginConfigs.TabIndex = 7;
            checkRestorePluginConfigs.Text = "Plugin configurations";
            checkRestorePluginConfigs.UseMnemonic = false;
            checkRestorePluginConfigs.UseVisualStyleBackColor = true;
            // 
            // checkRestorePluginCredentials
            // 
            checkRestorePluginCredentials.AutoSize = true;
            checkRestorePluginCredentials.Location = new Point(25, 226);
            checkRestorePluginCredentials.Name = "checkRestorePluginCredentials";
            checkRestorePluginCredentials.Size = new Size(126, 20);
            checkRestorePluginCredentials.TabIndex = 8;
            checkRestorePluginCredentials.Text = "Plugin credentials";
            checkRestorePluginCredentials.UseMnemonic = false;
            checkRestorePluginCredentials.UseVisualStyleBackColor = true;
            // 
            // checkRestoreIconPacks
            // 
            checkRestoreIconPacks.AutoSize = true;
            checkRestoreIconPacks.Location = new Point(25, 252);
            checkRestoreIconPacks.Name = "checkRestoreIconPacks";
            checkRestoreIconPacks.Size = new Size(137, 20);
            checkRestoreIconPacks.TabIndex = 9;
            checkRestoreIconPacks.Text = "Installed icon packs";
            checkRestoreIconPacks.UseMnemonic = false;
            checkRestoreIconPacks.UseVisualStyleBackColor = true;
            // 
            // lblWhatToRestore
            // 
            lblWhatToRestore.Font = new Font("Tahoma", 12F);
            lblWhatToRestore.Location = new Point(25, 19);
            lblWhatToRestore.Name = "lblWhatToRestore";
            lblWhatToRestore.Size = new Size(353, 47);
            lblWhatToRestore.TabIndex = 10;
            lblWhatToRestore.Text = "What do you want to restore from the backup?";
            lblWhatToRestore.UseMnemonic = false;
            // 
            // btnRestore
            // 
            btnRestore.BorderRadius = 8;
            btnRestore.Cursor = Cursors.Hand;
            btnRestore.FlatAppearance.BorderSize = 0;
            btnRestore.FlatStyle = FlatStyle.Flat;
            btnRestore.Font = new Font("Tahoma", 9.75F);
            btnRestore.ForeColor = Color.White;
            btnRestore.HoverColor = Color.FromArgb(0, 89, 184);
            btnRestore.Icon = null;
            btnRestore.Location = new Point(132, 294);
            btnRestore.Name = "btnRestore";
            btnRestore.Progress = 0;
            btnRestore.ProgressColor = Color.FromArgb(0, 46, 94);
            btnRestore.Size = new Size(150, 30);
            btnRestore.TabIndex = 11;
            btnRestore.Text = "Restore";
            btnRestore.UseMnemonic = false;
            btnRestore.UseVisualStyleBackColor = false;
            btnRestore.UseWindowsAccentColor = true;
            btnRestore.WriteProgress = true;
            btnRestore.Click += BtnRestore_Click;
            // 
            // RestoreBackupDialog
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(414, 343);
            Controls.Add(btnRestore);
            Controls.Add(lblWhatToRestore);
            Controls.Add(checkRestoreIconPacks);
            Controls.Add(checkRestorePluginCredentials);
            Controls.Add(checkRestorePluginConfigs);
            Controls.Add(checkRestorePlugins);
            Controls.Add(checkRestoreVariables);
            Controls.Add(checkRestoreDevices);
            Controls.Add(checkRestoreProfiles);
            Controls.Add(checkRestoreConfig);
            Name = "RestoreBackupDialog";
            Text = "Restore backup";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private CheckBox checkRestoreConfig;
        private CheckBox checkRestoreProfiles;
        private CheckBox checkRestoreDevices;
        private CheckBox checkRestoreVariables;
        private CheckBox checkRestorePlugins;
        private CheckBox checkRestorePluginConfigs;
        private CheckBox checkRestorePluginCredentials;
        private CheckBox checkRestoreIconPacks;
        private Label lblWhatToRestore;
        private ButtonPrimary btnRestore;
    }
}