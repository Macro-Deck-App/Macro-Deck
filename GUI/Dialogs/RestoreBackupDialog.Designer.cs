
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class RestoreBackupDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

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
            this.checkRestoreConfig = new System.Windows.Forms.CheckBox();
            this.checkRestoreProfiles = new System.Windows.Forms.CheckBox();
            this.checkRestoreDevices = new System.Windows.Forms.CheckBox();
            this.checkRestoreVariables = new System.Windows.Forms.CheckBox();
            this.checkRestorePlugins = new System.Windows.Forms.CheckBox();
            this.checkRestorePluginConfigs = new System.Windows.Forms.CheckBox();
            this.checkRestorePluginCredentials = new System.Windows.Forms.CheckBox();
            this.checkRestoreIconPacks = new System.Windows.Forms.CheckBox();
            this.lblWhatToRestore = new System.Windows.Forms.Label();
            this.btnRestore = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.SuspendLayout();
            // 
            // checkRestoreConfig
            // 
            this.checkRestoreConfig.AutoSize = true;
            this.checkRestoreConfig.Location = new System.Drawing.Point(25, 70);
            this.checkRestoreConfig.Name = "checkRestoreConfig";
            this.checkRestoreConfig.Size = new System.Drawing.Size(102, 20);
            this.checkRestoreConfig.TabIndex = 2;
            this.checkRestoreConfig.Text = "Configuration";
            this.checkRestoreConfig.UseVisualStyleBackColor = true;
            // 
            // checkRestoreProfiles
            // 
            this.checkRestoreProfiles.AutoSize = true;
            this.checkRestoreProfiles.Location = new System.Drawing.Point(25, 96);
            this.checkRestoreProfiles.Name = "checkRestoreProfiles";
            this.checkRestoreProfiles.Size = new System.Drawing.Size(68, 20);
            this.checkRestoreProfiles.TabIndex = 3;
            this.checkRestoreProfiles.Text = "Profiles";
            this.checkRestoreProfiles.UseVisualStyleBackColor = true;
            // 
            // checkRestoreDevices
            // 
            this.checkRestoreDevices.AutoSize = true;
            this.checkRestoreDevices.Location = new System.Drawing.Point(25, 122);
            this.checkRestoreDevices.Name = "checkRestoreDevices";
            this.checkRestoreDevices.Size = new System.Drawing.Size(69, 20);
            this.checkRestoreDevices.TabIndex = 4;
            this.checkRestoreDevices.Text = "Devices";
            this.checkRestoreDevices.UseVisualStyleBackColor = true;
            // 
            // checkRestoreVariables
            // 
            this.checkRestoreVariables.AutoSize = true;
            this.checkRestoreVariables.Location = new System.Drawing.Point(25, 148);
            this.checkRestoreVariables.Name = "checkRestoreVariables";
            this.checkRestoreVariables.Size = new System.Drawing.Size(79, 20);
            this.checkRestoreVariables.TabIndex = 5;
            this.checkRestoreVariables.Text = "Variables";
            this.checkRestoreVariables.UseVisualStyleBackColor = true;
            // 
            // checkRestorePlugins
            // 
            this.checkRestorePlugins.AutoSize = true;
            this.checkRestorePlugins.Location = new System.Drawing.Point(25, 174);
            this.checkRestorePlugins.Name = "checkRestorePlugins";
            this.checkRestorePlugins.Size = new System.Drawing.Size(118, 20);
            this.checkRestorePlugins.TabIndex = 6;
            this.checkRestorePlugins.Text = "Installed plugins";
            this.checkRestorePlugins.UseVisualStyleBackColor = true;
            // 
            // checkRestorePluginConfigs
            // 
            this.checkRestorePluginConfigs.AutoSize = true;
            this.checkRestorePluginConfigs.Location = new System.Drawing.Point(25, 200);
            this.checkRestorePluginConfigs.Name = "checkRestorePluginConfigs";
            this.checkRestorePluginConfigs.Size = new System.Drawing.Size(144, 20);
            this.checkRestorePluginConfigs.TabIndex = 7;
            this.checkRestorePluginConfigs.Text = "Plugin configurations";
            this.checkRestorePluginConfigs.UseVisualStyleBackColor = true;
            // 
            // checkRestorePluginCredentials
            // 
            this.checkRestorePluginCredentials.AutoSize = true;
            this.checkRestorePluginCredentials.Location = new System.Drawing.Point(25, 226);
            this.checkRestorePluginCredentials.Name = "checkRestorePluginCredentials";
            this.checkRestorePluginCredentials.Size = new System.Drawing.Size(126, 20);
            this.checkRestorePluginCredentials.TabIndex = 8;
            this.checkRestorePluginCredentials.Text = "Plugin credentials";
            this.checkRestorePluginCredentials.UseVisualStyleBackColor = true;
            // 
            // checkRestoreIconPacks
            // 
            this.checkRestoreIconPacks.AutoSize = true;
            this.checkRestoreIconPacks.Location = new System.Drawing.Point(25, 252);
            this.checkRestoreIconPacks.Name = "checkRestoreIconPacks";
            this.checkRestoreIconPacks.Size = new System.Drawing.Size(137, 20);
            this.checkRestoreIconPacks.TabIndex = 9;
            this.checkRestoreIconPacks.Text = "Installed icon packs";
            this.checkRestoreIconPacks.UseVisualStyleBackColor = true;
            // 
            // lblWhatToRestore
            // 
            this.lblWhatToRestore.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblWhatToRestore.Location = new System.Drawing.Point(25, 19);
            this.lblWhatToRestore.Name = "lblWhatToRestore";
            this.lblWhatToRestore.Size = new System.Drawing.Size(353, 47);
            this.lblWhatToRestore.TabIndex = 10;
            this.lblWhatToRestore.Text = "What do you want to restore from the backup?";
            // 
            // btnRestore
            // 
            this.btnRestore.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnRestore.BorderRadius = 8;
            this.btnRestore.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRestore.FlatAppearance.BorderSize = 0;
            this.btnRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRestore.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnRestore.ForeColor = System.Drawing.Color.White;
            this.btnRestore.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnRestore.Icon = null;
            this.btnRestore.Location = new System.Drawing.Point(132, 294);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Progress = 0;
            this.btnRestore.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnRestore.Size = new System.Drawing.Size(150, 30);
            this.btnRestore.TabIndex = 11;
            this.btnRestore.Text = "Restore";
            this.btnRestore.UseVisualStyleBackColor = false;
            this.btnRestore.Click += new System.EventHandler(this.BtnRestore_Click);
            // 
            // RestoreBackupDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(414, 343);
            this.Controls.Add(this.btnRestore);
            this.Controls.Add(this.lblWhatToRestore);
            this.Controls.Add(this.checkRestoreIconPacks);
            this.Controls.Add(this.checkRestorePluginCredentials);
            this.Controls.Add(this.checkRestorePluginConfigs);
            this.Controls.Add(this.checkRestorePlugins);
            this.Controls.Add(this.checkRestoreVariables);
            this.Controls.Add(this.checkRestoreDevices);
            this.Controls.Add(this.checkRestoreProfiles);
            this.Controls.Add(this.checkRestoreConfig);
            this.Name = "RestoreBackupDialog";
            this.Text = "RestoreBackupDialog";
            this.Controls.SetChildIndex(this.checkRestoreConfig, 0);
            this.Controls.SetChildIndex(this.checkRestoreProfiles, 0);
            this.Controls.SetChildIndex(this.checkRestoreDevices, 0);
            this.Controls.SetChildIndex(this.checkRestoreVariables, 0);
            this.Controls.SetChildIndex(this.checkRestorePlugins, 0);
            this.Controls.SetChildIndex(this.checkRestorePluginConfigs, 0);
            this.Controls.SetChildIndex(this.checkRestorePluginCredentials, 0);
            this.Controls.SetChildIndex(this.checkRestoreIconPacks, 0);
            this.Controls.SetChildIndex(this.lblWhatToRestore, 0);
            this.Controls.SetChildIndex(this.btnRestore, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox checkRestoreConfig;
        private System.Windows.Forms.CheckBox checkRestoreProfiles;
        private System.Windows.Forms.CheckBox checkRestoreDevices;
        private System.Windows.Forms.CheckBox checkRestoreVariables;
        private System.Windows.Forms.CheckBox checkRestorePlugins;
        private System.Windows.Forms.CheckBox checkRestorePluginConfigs;
        private System.Windows.Forms.CheckBox checkRestorePluginCredentials;
        private System.Windows.Forms.CheckBox checkRestoreIconPacks;
        private System.Windows.Forms.Label lblWhatToRestore;
        private CustomControls.ButtonPrimary btnRestore;
    }
}