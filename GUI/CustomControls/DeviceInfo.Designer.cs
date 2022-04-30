
namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class DeviceInfo
    {
        /// <summary> 
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Komponenten-Designer generierter Code

        /// <summary> 
        /// Erforderliche Methode für die Designerunterstützung. 
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblDisplayName = new System.Windows.Forms.Label();
            this.displayName = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.btnRemove = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.lblIdLabel = new System.Windows.Forms.Label();
            this.lblId = new System.Windows.Forms.Label();
            this.printDocument1 = new System.Drawing.Printing.PrintDocument();
            this.checkBlockConnection = new System.Windows.Forms.CheckBox();
            this.lblStatusLabel = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnChangeDisplayName = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.profiles = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.lblProfile = new System.Windows.Forms.Label();
            this.lblDeviceType = new System.Windows.Forms.Label();
            this.iconDeviceType = new System.Windows.Forms.PictureBox();
            this.btnConfigure = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemove)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnChangeDisplayName)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.iconDeviceType)).BeginInit();
            this.SuspendLayout();
            // 
            // lblDisplayName
            // 
            this.lblDisplayName.AutoSize = true;
            this.lblDisplayName.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDisplayName.ForeColor = System.Drawing.Color.White;
            this.lblDisplayName.Location = new System.Drawing.Point(3, 132);
            this.lblDisplayName.Name = "lblDisplayName";
            this.lblDisplayName.Size = new System.Drawing.Size(110, 19);
            this.lblDisplayName.TabIndex = 0;
            this.lblDisplayName.Text = "Display name:";
            this.lblDisplayName.UseMnemonic = false;
            // 
            // displayName
            // 
            this.displayName.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.displayName.Cursor = System.Windows.Forms.Cursors.Hand;
            this.displayName.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.displayName.ForeColor = System.Drawing.Color.White;
            this.displayName.Icon = null;
            this.displayName.Location = new System.Drawing.Point(119, 128);
            this.displayName.MaxCharacters = 32767;
            this.displayName.Multiline = false;
            this.displayName.Name = "displayName";
            this.displayName.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.displayName.PasswordChar = false;
            this.displayName.PlaceHolderColor = System.Drawing.Color.Gray;
            this.displayName.PlaceHolderText = "";
            this.displayName.ReadOnly = false;
            this.displayName.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.displayName.SelectionStart = 0;
            this.displayName.Size = new System.Drawing.Size(299, 27);
            this.displayName.TabIndex = 1;
            this.displayName.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnRemove
            // 
            this.btnRemove.BackColor = System.Drawing.Color.Transparent;
            this.btnRemove.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnRemove.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnRemove.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRemove.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnRemove.ForeColor = System.Drawing.Color.White;
            this.btnRemove.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnRemove.Location = new System.Drawing.Point(507, 2);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new System.Drawing.Size(25, 25);
            this.btnRemove.TabIndex = 2;
            this.btnRemove.TabStop = false;
            this.btnRemove.Click += new System.EventHandler(this.BtnRemove_Click);
            // 
            // lblIdLabel
            // 
            this.lblIdLabel.AutoSize = true;
            this.lblIdLabel.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIdLabel.ForeColor = System.Drawing.Color.White;
            this.lblIdLabel.Location = new System.Drawing.Point(5, 70);
            this.lblIdLabel.Name = "lblIdLabel";
            this.lblIdLabel.Size = new System.Drawing.Size(77, 19);
            this.lblIdLabel.TabIndex = 3;
            this.lblIdLabel.Text = "Client ID:";
            this.lblIdLabel.UseMnemonic = false;
            // 
            // lblId
            // 
            this.lblId.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblId.ForeColor = System.Drawing.Color.White;
            this.lblId.Location = new System.Drawing.Point(119, 67);
            this.lblId.Name = "lblId";
            this.lblId.Size = new System.Drawing.Size(194, 23);
            this.lblId.TabIndex = 4;
            this.lblId.UseMnemonic = false;
            // 
            // checkBlockConnection
            // 
            this.checkBlockConnection.CheckAlign = System.Drawing.ContentAlignment.TopLeft;
            this.checkBlockConnection.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkBlockConnection.ForeColor = System.Drawing.Color.White;
            this.checkBlockConnection.Location = new System.Drawing.Point(334, 5);
            this.checkBlockConnection.Name = "checkBlockConnection";
            this.checkBlockConnection.Size = new System.Drawing.Size(167, 49);
            this.checkBlockConnection.TabIndex = 5;
            this.checkBlockConnection.Text = "Block connection";
            this.checkBlockConnection.TextAlign = System.Drawing.ContentAlignment.TopLeft;
            this.checkBlockConnection.UseMnemonic = false;
            this.checkBlockConnection.UseVisualStyleBackColor = true;
            this.checkBlockConnection.CheckedChanged += new System.EventHandler(this.CheckBlockConnection_CheckedChanged);
            // 
            // lblStatusLabel
            // 
            this.lblStatusLabel.AutoSize = true;
            this.lblStatusLabel.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblStatusLabel.ForeColor = System.Drawing.Color.White;
            this.lblStatusLabel.Location = new System.Drawing.Point(5, 101);
            this.lblStatusLabel.Name = "lblStatusLabel";
            this.lblStatusLabel.Size = new System.Drawing.Size(58, 19);
            this.lblStatusLabel.TabIndex = 6;
            this.lblStatusLabel.Text = "Status:";
            this.lblStatusLabel.UseMnemonic = false;
            // 
            // lblStatus
            // 
            this.lblStatus.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblStatus.ForeColor = System.Drawing.Color.White;
            this.lblStatus.Location = new System.Drawing.Point(119, 101);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(226, 23);
            this.lblStatus.TabIndex = 7;
            this.lblStatus.Text = "?";
            this.lblStatus.UseMnemonic = false;
            // 
            // btnChangeDisplayName
            // 
            this.btnChangeDisplayName.BackColor = System.Drawing.Color.Transparent;
            this.btnChangeDisplayName.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Edit_Normal;
            this.btnChangeDisplayName.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnChangeDisplayName.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnChangeDisplayName.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnChangeDisplayName.ForeColor = System.Drawing.Color.White;
            this.btnChangeDisplayName.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Edit_Hover;
            this.btnChangeDisplayName.Location = new System.Drawing.Point(424, 122);
            this.btnChangeDisplayName.Name = "btnChangeDisplayName";
            this.btnChangeDisplayName.Size = new System.Drawing.Size(27, 27);
            this.btnChangeDisplayName.TabIndex = 8;
            this.btnChangeDisplayName.TabStop = false;
            this.btnChangeDisplayName.Click += new System.EventHandler(this.BtnChangeDisplayName_Click);
            // 
            // profiles
            // 
            this.profiles.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.profiles.Cursor = System.Windows.Forms.Cursors.Hand;
            this.profiles.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.profiles.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.profiles.ForeColor = System.Drawing.Color.White;
            this.profiles.Icon = null;
            this.profiles.Location = new System.Drawing.Point(119, 159);
            this.profiles.Name = "profiles";
            this.profiles.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.profiles.SelectedIndex = -1;
            this.profiles.SelectedItem = null;
            this.profiles.Size = new System.Drawing.Size(299, 28);
            this.profiles.TabIndex = 9;
            this.profiles.SelectedIndexChanged += new System.EventHandler(this.Profiles_SelectedIndexChanged);
            // 
            // lblProfile
            // 
            this.lblProfile.AutoSize = true;
            this.lblProfile.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblProfile.ForeColor = System.Drawing.Color.White;
            this.lblProfile.Location = new System.Drawing.Point(5, 163);
            this.lblProfile.Name = "lblProfile";
            this.lblProfile.Size = new System.Drawing.Size(60, 19);
            this.lblProfile.TabIndex = 10;
            this.lblProfile.Text = "Profile:";
            this.lblProfile.UseMnemonic = false;
            // 
            // lblDeviceType
            // 
            this.lblDeviceType.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDeviceType.ForeColor = System.Drawing.Color.White;
            this.lblDeviceType.Location = new System.Drawing.Point(34, 4);
            this.lblDeviceType.Name = "lblDeviceType";
            this.lblDeviceType.Size = new System.Drawing.Size(154, 23);
            this.lblDeviceType.TabIndex = 11;
            this.lblDeviceType.Text = "Web client";
            this.lblDeviceType.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblDeviceType.UseMnemonic = false;
            // 
            // iconDeviceType
            // 
            this.iconDeviceType.Location = new System.Drawing.Point(5, 4);
            this.iconDeviceType.Name = "iconDeviceType";
            this.iconDeviceType.Size = new System.Drawing.Size(23, 23);
            this.iconDeviceType.TabIndex = 12;
            this.iconDeviceType.TabStop = false;
            // 
            // btnConfigure
            // 
            this.btnConfigure.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnConfigure.BorderRadius = 8;
            this.btnConfigure.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnConfigure.FlatAppearance.BorderSize = 0;
            this.btnConfigure.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnConfigure.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnConfigure.ForeColor = System.Drawing.Color.White;
            this.btnConfigure.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnConfigure.Icon = null;
            this.btnConfigure.Location = new System.Drawing.Point(5, 33);
            this.btnConfigure.Name = "btnConfigure";
            this.btnConfigure.Progress = 0;
            this.btnConfigure.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnConfigure.Size = new System.Drawing.Size(183, 27);
            this.btnConfigure.TabIndex = 13;
            this.btnConfigure.Text = "Device settings";
            this.btnConfigure.UseMnemonic = false;
            this.btnConfigure.UseVisualStyleBackColor = false;
            this.btnConfigure.Click += new System.EventHandler(this.BtnConfigure_Click);
            // 
            // DeviceInfo
            // 
            
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.btnConfigure);
            this.Controls.Add(this.iconDeviceType);
            this.Controls.Add(this.lblDeviceType);
            this.Controls.Add(this.lblProfile);
            this.Controls.Add(this.profiles);
            this.Controls.Add(this.btnChangeDisplayName);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblStatusLabel);
            this.Controls.Add(this.checkBlockConnection);
            this.Controls.Add(this.lblId);
            this.Controls.Add(this.lblIdLabel);
            this.Controls.Add(this.btnRemove);
            this.Controls.Add(this.displayName);
            this.Controls.Add(this.lblDisplayName);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "DeviceInfo";
            this.Size = new System.Drawing.Size(534, 194);
            this.Load += new System.EventHandler(this.DeviceInfo_Load);
            ((System.ComponentModel.ISupportInitialize)(this.btnRemove)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnChangeDisplayName)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.iconDeviceType)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblDisplayName;
        private RoundedTextBox displayName;
        private PictureButton btnRemove;
        private System.Windows.Forms.Label lblIdLabel;
        private System.Windows.Forms.Label lblId;
        private System.Drawing.Printing.PrintDocument printDocument1;
        private System.Windows.Forms.CheckBox checkBlockConnection;
        private System.Windows.Forms.Label lblStatusLabel;
        private System.Windows.Forms.Label lblStatus;
        private PictureButton btnChangeDisplayName;
        private RoundedComboBox profiles;
        private System.Windows.Forms.Label lblProfile;
        private System.Windows.Forms.Label lblDeviceType;
        private System.Windows.Forms.PictureBox iconDeviceType;
        private ButtonPrimary btnConfigure;
    }
}
