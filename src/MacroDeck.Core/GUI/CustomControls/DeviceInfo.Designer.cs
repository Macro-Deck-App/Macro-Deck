
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class DeviceInfo
    {
        /// <summary> 
        /// Erforderliche Designervariable.
        /// </summary>
        private IContainer components = null;

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
            this.lblDisplayName = new Label();
            this.displayName = new RoundedTextBox();
            this.btnRemove = new PictureButton();
            this.lblIdLabel = new Label();
            this.lblId = new Label();
            this.checkBlockConnection = new CheckBox();
            this.lblStatusLabel = new Label();
            this.lblStatus = new Label();
            this.btnChangeDisplayName = new PictureButton();
            this.profiles = new RoundedComboBox();
            this.lblProfile = new Label();
            this.btnConfigure = new ButtonPrimary();
            ((ISupportInitialize)(this.btnRemove)).BeginInit();
            ((ISupportInitialize)(this.btnChangeDisplayName)).BeginInit();
            this.SuspendLayout();
            // 
            // lblDisplayName
            // 
            this.lblDisplayName.AutoSize = true;
            this.lblDisplayName.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblDisplayName.ForeColor = Color.White;
            this.lblDisplayName.Location = new Point(3, 132);
            this.lblDisplayName.Name = "lblDisplayName";
            this.lblDisplayName.Size = new Size(110, 19);
            this.lblDisplayName.TabIndex = 0;
            this.lblDisplayName.Text = "Display name:";
            this.lblDisplayName.UseMnemonic = false;
            // 
            // displayName
            // 
            this.displayName.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.displayName.Cursor = Cursors.Hand;
            this.displayName.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.displayName.ForeColor = Color.White;
            this.displayName.Icon = null;
            this.displayName.Location = new Point(119, 128);
            this.displayName.MaxCharacters = 32767;
            this.displayName.Multiline = false;
            this.displayName.Name = "displayName";
            this.displayName.Padding = new Padding(8, 5, 8, 5);
            this.displayName.PasswordChar = false;
            this.displayName.PlaceHolderColor = Color.Gray;
            this.displayName.PlaceHolderText = "";
            this.displayName.ReadOnly = false;
            this.displayName.ScrollBars = ScrollBars.None;
            this.displayName.SelectionStart = 0;
            this.displayName.Size = new Size(299, 27);
            this.displayName.TabIndex = 1;
            this.displayName.TextAlignment = HorizontalAlignment.Left;
            // 
            // btnRemove
            // 
            this.btnRemove.BackColor = Color.Transparent;
            this.btnRemove.BackgroundImage = Resources.Delete_Normal;
            this.btnRemove.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnRemove.Cursor = Cursors.Hand;
            this.btnRemove.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnRemove.ForeColor = Color.White;
            this.btnRemove.HoverImage = Resources.Delete_Hover;
            this.btnRemove.Location = new Point(507, 2);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new Size(25, 25);
            this.btnRemove.TabIndex = 2;
            this.btnRemove.TabStop = false;
            this.btnRemove.Click += new EventHandler(this.BtnRemove_Click);
            // 
            // lblIdLabel
            // 
            this.lblIdLabel.AutoSize = true;
            this.lblIdLabel.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblIdLabel.ForeColor = Color.White;
            this.lblIdLabel.Location = new Point(5, 70);
            this.lblIdLabel.Name = "lblIdLabel";
            this.lblIdLabel.Size = new Size(77, 19);
            this.lblIdLabel.TabIndex = 3;
            this.lblIdLabel.Text = "Client ID:";
            this.lblIdLabel.UseMnemonic = false;
            // 
            // lblId
            // 
            this.lblId.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblId.ForeColor = Color.White;
            this.lblId.Location = new Point(119, 67);
            this.lblId.Name = "lblId";
            this.lblId.Size = new Size(194, 23);
            this.lblId.TabIndex = 4;
            this.lblId.UseMnemonic = false;
            // 
            // checkBlockConnection
            // 
            this.checkBlockConnection.CheckAlign = ContentAlignment.TopLeft;
            this.checkBlockConnection.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.checkBlockConnection.ForeColor = Color.White;
            this.checkBlockConnection.Location = new Point(334, 5);
            this.checkBlockConnection.Name = "checkBlockConnection";
            this.checkBlockConnection.Size = new Size(167, 49);
            this.checkBlockConnection.TabIndex = 5;
            this.checkBlockConnection.Text = "Block connection";
            this.checkBlockConnection.TextAlign = ContentAlignment.TopLeft;
            this.checkBlockConnection.UseMnemonic = false;
            this.checkBlockConnection.UseVisualStyleBackColor = true;
            this.checkBlockConnection.CheckedChanged += new EventHandler(this.CheckBlockConnection_CheckedChanged);
            // 
            // lblStatusLabel
            // 
            this.lblStatusLabel.AutoSize = true;
            this.lblStatusLabel.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblStatusLabel.ForeColor = Color.White;
            this.lblStatusLabel.Location = new Point(5, 101);
            this.lblStatusLabel.Name = "lblStatusLabel";
            this.lblStatusLabel.Size = new Size(58, 19);
            this.lblStatusLabel.TabIndex = 6;
            this.lblStatusLabel.Text = "Status:";
            this.lblStatusLabel.UseMnemonic = false;
            // 
            // lblStatus
            // 
            this.lblStatus.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblStatus.ForeColor = Color.White;
            this.lblStatus.Location = new Point(119, 101);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new Size(226, 23);
            this.lblStatus.TabIndex = 7;
            this.lblStatus.Text = "?";
            this.lblStatus.UseMnemonic = false;
            // 
            // btnChangeDisplayName
            // 
            this.btnChangeDisplayName.BackColor = Color.Transparent;
            this.btnChangeDisplayName.BackgroundImage = Resources.Edit_Normal;
            this.btnChangeDisplayName.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnChangeDisplayName.Cursor = Cursors.Hand;
            this.btnChangeDisplayName.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnChangeDisplayName.ForeColor = Color.White;
            this.btnChangeDisplayName.HoverImage = Resources.Edit_Hover;
            this.btnChangeDisplayName.Location = new Point(424, 122);
            this.btnChangeDisplayName.Name = "btnChangeDisplayName";
            this.btnChangeDisplayName.Size = new Size(27, 27);
            this.btnChangeDisplayName.TabIndex = 8;
            this.btnChangeDisplayName.TabStop = false;
            this.btnChangeDisplayName.Click += new EventHandler(this.BtnChangeDisplayName_Click);
            // 
            // profiles
            // 
            this.profiles.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.profiles.Cursor = Cursors.Hand;
            this.profiles.DropDownStyle = ComboBoxStyle.DropDownList;
            this.profiles.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.profiles.ForeColor = Color.White;
            this.profiles.Icon = null;
            this.profiles.Location = new Point(119, 159);
            this.profiles.Name = "profiles";
            this.profiles.Padding = new Padding(8, 2, 8, 2);
            this.profiles.SelectedIndex = -1;
            this.profiles.SelectedItem = null;
            this.profiles.Size = new Size(299, 28);
            this.profiles.TabIndex = 9;
            this.profiles.SelectedIndexChanged += new EventHandler(this.Profiles_SelectedIndexChanged);
            // 
            // lblProfile
            // 
            this.lblProfile.AutoSize = true;
            this.lblProfile.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblProfile.ForeColor = Color.White;
            this.lblProfile.Location = new Point(5, 163);
            this.lblProfile.Name = "lblProfile";
            this.lblProfile.Size = new Size(60, 19);
            this.lblProfile.TabIndex = 10;
            this.lblProfile.Text = "Profile:";
            this.lblProfile.UseMnemonic = false;
            // 
            // btnConfigure
            // 
            this.btnConfigure.BackColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnConfigure.BorderRadius = 8;
            this.btnConfigure.Cursor = Cursors.Hand;
            this.btnConfigure.FlatAppearance.BorderSize = 0;
            this.btnConfigure.FlatStyle = FlatStyle.Flat;
            this.btnConfigure.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnConfigure.ForeColor = Color.White;
            this.btnConfigure.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnConfigure.Icon = null;
            this.btnConfigure.Location = new Point(5, 33);
            this.btnConfigure.Name = "btnConfigure";
            this.btnConfigure.Progress = 0;
            this.btnConfigure.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnConfigure.Size = new Size(183, 27);
            this.btnConfigure.TabIndex = 13;
            this.btnConfigure.Text = "Device settings";
            this.btnConfigure.UseMnemonic = false;
            this.btnConfigure.UseVisualStyleBackColor = false;
            this.btnConfigure.Click += new EventHandler(this.BtnConfigure_Click);
            // 
            // DeviceInfo
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.btnConfigure);
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
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "DeviceInfo";
            this.Size = new Size(534, 194);
            this.Load += new EventHandler(this.DeviceInfo_Load);
            ((ISupportInitialize)(this.btnRemove)).EndInit();
            ((ISupportInitialize)(this.btnChangeDisplayName)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Label lblDisplayName;
        private RoundedTextBox displayName;
        private PictureButton btnRemove;
        private Label lblIdLabel;
        private Label lblId;
        private CheckBox checkBlockConnection;
        private Label lblStatusLabel;
        private Label lblStatus;
        private PictureButton btnChangeDisplayName;
        private RoundedComboBox profiles;
        private Label lblProfile;
        private ButtonPrimary btnConfigure;
    }
}
