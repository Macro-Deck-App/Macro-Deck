
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using ComboBox = SuchByte.MacroDeck.GUI.CustomControls.ComboBox;

namespace SuchByte.MacroDeck.GUI
{
    partial class AddFolder
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
            this.folderName = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.lblFolderName = new System.Windows.Forms.Label();
            this.btnCreateFolder = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.groupAutomaticallySwitchFolder = new System.Windows.Forms.GroupBox();
            this.applicationDeviceSettings = new System.Windows.Forms.Panel();
            this.btnReloadApplications = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.devicesList = new System.Windows.Forms.CheckedListBox();
            this.lblDevices = new System.Windows.Forms.Label();
            this.applicationList = new SuchByte.MacroDeck.GUI.CustomControls.ComboBox();
            this.lblApplication = new System.Windows.Forms.Label();
            this.radioOnFocus = new System.Windows.Forms.RadioButton();
            this.radioNever = new System.Windows.Forms.RadioButton();
            this.groupAutomaticallySwitchFolder.SuspendLayout();
            this.applicationDeviceSettings.SuspendLayout();
            this.SuspendLayout();
            // 
            // folderName
            // 
            this.folderName.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.folderName.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.folderName.ForeColor = System.Drawing.Color.White;
            this.folderName.Icon = null;
            this.folderName.Location = new System.Drawing.Point(128, 9);
            this.folderName.Multiline = false;
            this.folderName.Name = "folderName";
            this.folderName.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.folderName.PasswordChar = false;
            this.folderName.Size = new System.Drawing.Size(273, 25);
            this.folderName.TabIndex = 0;
            // 
            // lblFolderName
            // 
            this.lblFolderName.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblFolderName.ForeColor = System.Drawing.Color.White;
            this.lblFolderName.Location = new System.Drawing.Point(11, 9);
            this.lblFolderName.Name = "lblFolderName";
            this.lblFolderName.Size = new System.Drawing.Size(111, 23);
            this.lblFolderName.TabIndex = 1;
            this.lblFolderName.Text = "Folder name:";
            this.lblFolderName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblFolderName.UseMnemonic = false;
            // 
            // btnCreateFolder
            // 
            this.btnCreateFolder.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnCreateFolder.BorderRadius = 8;
            this.btnCreateFolder.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCreateFolder.FlatAppearance.BorderSize = 0;
            this.btnCreateFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCreateFolder.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnCreateFolder.ForeColor = System.Drawing.Color.White;
            this.btnCreateFolder.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnCreateFolder.Icon = null;
            this.btnCreateFolder.Location = new System.Drawing.Point(381, 333);
            this.btnCreateFolder.Name = "btnCreateFolder";
            this.btnCreateFolder.Progress = 0;
            this.btnCreateFolder.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnCreateFolder.Size = new System.Drawing.Size(75, 25);
            this.btnCreateFolder.TabIndex = 2;
            this.btnCreateFolder.Text = "Ok";
            this.btnCreateFolder.UseMnemonic = false;
            this.btnCreateFolder.UseVisualStyleBackColor = false;
            this.btnCreateFolder.Click += new System.EventHandler(this.BtnCreateFolder_Click);
            // 
            // groupAutomaticallySwitchFolder
            // 
            this.groupAutomaticallySwitchFolder.Controls.Add(this.applicationDeviceSettings);
            this.groupAutomaticallySwitchFolder.Controls.Add(this.radioOnFocus);
            this.groupAutomaticallySwitchFolder.Controls.Add(this.radioNever);
            this.groupAutomaticallySwitchFolder.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.groupAutomaticallySwitchFolder.ForeColor = System.Drawing.Color.White;
            this.groupAutomaticallySwitchFolder.Location = new System.Drawing.Point(12, 52);
            this.groupAutomaticallySwitchFolder.Name = "groupAutomaticallySwitchFolder";
            this.groupAutomaticallySwitchFolder.Size = new System.Drawing.Size(444, 275);
            this.groupAutomaticallySwitchFolder.TabIndex = 3;
            this.groupAutomaticallySwitchFolder.TabStop = false;
            this.groupAutomaticallySwitchFolder.Text = "Automatically switch to folder";
            // 
            // applicationDeviceSettings
            // 
            this.applicationDeviceSettings.Controls.Add(this.btnReloadApplications);
            this.applicationDeviceSettings.Controls.Add(this.devicesList);
            this.applicationDeviceSettings.Controls.Add(this.lblDevices);
            this.applicationDeviceSettings.Controls.Add(this.applicationList);
            this.applicationDeviceSettings.Controls.Add(this.lblApplication);
            this.applicationDeviceSettings.Enabled = false;
            this.applicationDeviceSettings.Location = new System.Drawing.Point(6, 54);
            this.applicationDeviceSettings.Name = "applicationDeviceSettings";
            this.applicationDeviceSettings.Size = new System.Drawing.Size(432, 215);
            this.applicationDeviceSettings.TabIndex = 2;
            // 
            // btnReloadApplications
            // 
            this.btnReloadApplications.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.btnReloadApplications.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.reload;
            this.btnReloadApplications.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnReloadApplications.BorderRadius = 8;
            this.btnReloadApplications.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnReloadApplications.FlatAppearance.BorderSize = 0;
            this.btnReloadApplications.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnReloadApplications.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnReloadApplications.ForeColor = System.Drawing.Color.White;
            this.btnReloadApplications.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnReloadApplications.Icon = global::SuchByte.MacroDeck.Properties.Resources.reload;
            this.btnReloadApplications.Location = new System.Drawing.Point(404, 2);
            this.btnReloadApplications.Name = "btnReloadApplications";
            this.btnReloadApplications.Progress = 0;
            this.btnReloadApplications.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnReloadApplications.Size = new System.Drawing.Size(25, 25);
            this.btnReloadApplications.TabIndex = 6;
            this.btnReloadApplications.UseMnemonic = false;
            this.btnReloadApplications.UseVisualStyleBackColor = false;
            this.btnReloadApplications.Click += new System.EventHandler(this.BtnReloadApplications_Click);
            // 
            // devicesList
            // 
            this.devicesList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.devicesList.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.devicesList.ForeColor = System.Drawing.Color.White;
            this.devicesList.FormattingEnabled = true;
            this.devicesList.Location = new System.Drawing.Point(6, 75);
            this.devicesList.Name = "devicesList";
            this.devicesList.Size = new System.Drawing.Size(420, 132);
            this.devicesList.TabIndex = 5;
            // 
            // lblDevices
            // 
            this.lblDevices.AutoSize = true;
            this.lblDevices.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDevices.ForeColor = System.Drawing.Color.White;
            this.lblDevices.Location = new System.Drawing.Point(3, 54);
            this.lblDevices.Name = "lblDevices";
            this.lblDevices.Size = new System.Drawing.Size(58, 18);
            this.lblDevices.TabIndex = 4;
            this.lblDevices.Text = "Devices";
            this.lblDevices.UseMnemonic = false;
            // 
            // applicationList
            // 
            this.applicationList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.applicationList.BorderRadius = 8;
            this.applicationList.Cursor = System.Windows.Forms.Cursors.Hand;
            this.applicationList.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.applicationList.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.applicationList.ForeColor = System.Drawing.Color.White;
            this.applicationList.FormattingEnabled = true;
            this.applicationList.Location = new System.Drawing.Point(102, 1);
            this.applicationList.Name = "applicationList";
            this.applicationList.Size = new System.Drawing.Size(296, 24);
            this.applicationList.TabIndex = 3;
            // 
            // lblApplication
            // 
            this.lblApplication.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblApplication.ForeColor = System.Drawing.Color.White;
            this.lblApplication.Location = new System.Drawing.Point(3, 3);
            this.lblApplication.Name = "lblApplication";
            this.lblApplication.Size = new System.Drawing.Size(93, 24);
            this.lblApplication.TabIndex = 2;
            this.lblApplication.Text = "Application:";
            this.lblApplication.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblApplication.UseMnemonic = false;
            // 
            // radioOnFocus
            // 
            this.radioOnFocus.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnFocus.Location = new System.Drawing.Point(108, 26);
            this.radioOnFocus.Name = "radioOnFocus";
            this.radioOnFocus.Size = new System.Drawing.Size(160, 22);
            this.radioOnFocus.TabIndex = 1;
            this.radioOnFocus.Text = "On application focus";
            this.radioOnFocus.UseMnemonic = false;
            this.radioOnFocus.UseVisualStyleBackColor = true;
            this.radioOnFocus.CheckedChanged += new System.EventHandler(this.RadioOnFocus_CheckedChanged);
            // 
            // radioNever
            // 
            this.radioNever.Checked = true;
            this.radioNever.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioNever.Location = new System.Drawing.Point(6, 26);
            this.radioNever.Name = "radioNever";
            this.radioNever.Size = new System.Drawing.Size(96, 22);
            this.radioNever.TabIndex = 0;
            this.radioNever.TabStop = true;
            this.radioNever.Text = "Never";
            this.radioNever.UseMnemonic = false;
            this.radioNever.UseVisualStyleBackColor = true;
            this.radioNever.CheckedChanged += new System.EventHandler(this.RadioOnFocus_CheckedChanged);
            // 
            // AddFolder
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(468, 368);
            this.Controls.Add(this.groupAutomaticallySwitchFolder);
            this.Controls.Add(this.btnCreateFolder);
            this.Controls.Add(this.lblFolderName);
            this.Controls.Add(this.folderName);
            this.Name = "AddFolder";
            this.Text = "Macro Deck :: Create folder";
            this.Load += new System.EventHandler(this.AddFolder_Load);
            this.Controls.SetChildIndex(this.folderName, 0);
            this.Controls.SetChildIndex(this.lblFolderName, 0);
            this.Controls.SetChildIndex(this.btnCreateFolder, 0);
            this.Controls.SetChildIndex(this.groupAutomaticallySwitchFolder, 0);
            this.groupAutomaticallySwitchFolder.ResumeLayout(false);
            this.applicationDeviceSettings.ResumeLayout(false);
            this.applicationDeviceSettings.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private RoundedTextBox folderName;
        private Label lblFolderName;
        private ButtonPrimary btnCreateFolder;
        private GroupBox groupAutomaticallySwitchFolder;
        private RadioButton radioOnFocus;
        private RadioButton radioNever;
        private Panel applicationDeviceSettings;
        private CheckedListBox devicesList;
        private Label lblDevices;
        private ComboBox applicationList;
        private Label lblApplication;
        private ButtonPrimary btnReloadApplications;
    }
}