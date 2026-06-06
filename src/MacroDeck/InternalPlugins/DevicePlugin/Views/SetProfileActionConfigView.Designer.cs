using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views
{
    partial class SetProfileActionConfigView
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
            this.devicesList = new RoundedComboBox();
            this.profilesList = new RoundedComboBox();
            this.lblDevice = new Label();
            this.lblProfile = new Label();
            this.radioCurrentDevice = new RadioButton();
            this.radioFixedDevice = new RadioButton();
            this.SuspendLayout();
            // 
            // devicesList
            // 
            this.devicesList.Anchor = AnchorStyles.None;
            this.devicesList.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.devicesList.Cursor = Cursors.Hand;
            this.devicesList.DropDownStyle = ComboBoxStyle.DropDownList;
            this.devicesList.Enabled = false;
            this.devicesList.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.devicesList.Icon = null;
            this.devicesList.Location = new Point(504, 176);
            this.devicesList.Name = "devicesList";
            this.devicesList.Padding = new Padding(8, 2, 8, 2);
            this.devicesList.SelectedIndex = -1;
            this.devicesList.SelectedItem = null;
            this.devicesList.Size = new Size(199, 30);
            this.devicesList.TabIndex = 0;
            // 
            // profilesList
            // 
            this.profilesList.Anchor = AnchorStyles.None;
            this.profilesList.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.profilesList.Cursor = Cursors.Hand;
            this.profilesList.DropDownStyle = ComboBoxStyle.DropDownList;
            this.profilesList.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.profilesList.Icon = null;
            this.profilesList.Location = new Point(271, 218);
            this.profilesList.Name = "profilesList";
            this.profilesList.Padding = new Padding(8, 2, 8, 2);
            this.profilesList.SelectedIndex = -1;
            this.profilesList.SelectedItem = null;
            this.profilesList.Size = new Size(432, 30);
            this.profilesList.TabIndex = 1;
            // 
            // lblDevice
            // 
            this.lblDevice.Anchor = AnchorStyles.None;
            this.lblDevice.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblDevice.Location = new Point(153, 176);
            this.lblDevice.Name = "lblDevice";
            this.lblDevice.Size = new Size(112, 30);
            this.lblDevice.TabIndex = 2;
            this.lblDevice.Text = "Device";
            this.lblDevice.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblProfile
            // 
            this.lblProfile.Anchor = AnchorStyles.None;
            this.lblProfile.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblProfile.Location = new Point(153, 218);
            this.lblProfile.Name = "lblProfile";
            this.lblProfile.Size = new Size(112, 30);
            this.lblProfile.TabIndex = 3;
            this.lblProfile.Text = "Profile";
            this.lblProfile.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // radioCurrentDevice
            // 
            this.radioCurrentDevice.Checked = true;
            this.radioCurrentDevice.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioCurrentDevice.Location = new Point(271, 176);
            this.radioCurrentDevice.Name = "radioCurrentDevice";
            this.radioCurrentDevice.Size = new Size(207, 30);
            this.radioCurrentDevice.TabIndex = 4;
            this.radioCurrentDevice.TabStop = true;
            this.radioCurrentDevice.Text = "Where executed";
            this.radioCurrentDevice.UseVisualStyleBackColor = true;
            // 
            // radioFixedDevice
            // 
            this.radioFixedDevice.AutoSize = true;
            this.radioFixedDevice.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioFixedDevice.Location = new Point(484, 185);
            this.radioFixedDevice.Name = "radioFixedDevice";
            this.radioFixedDevice.Size = new Size(14, 13);
            this.radioFixedDevice.TabIndex = 5;
            this.radioFixedDevice.UseVisualStyleBackColor = true;
            this.radioFixedDevice.CheckedChanged += new EventHandler(this.radioFixedDevice_CheckedChanged);
            // 
            // SetProfileActionConfigView
            // 
            this.AutoScaleDimensions = new SizeF(10F, 23F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Controls.Add(this.radioFixedDevice);
            this.Controls.Add(this.radioCurrentDevice);
            this.Controls.Add(this.lblProfile);
            this.Controls.Add(this.lblDevice);
            this.Controls.Add(this.profilesList);
            this.Controls.Add(this.devicesList);
            this.Name = "SetProfileActionConfigView";
            this.Load += new EventHandler(this.SetProfileActionConfigView_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private RoundedComboBox devicesList;
        private RoundedComboBox profilesList;
        private Label lblDevice;
        private Label lblProfile;
        private RadioButton radioCurrentDevice;
        private RadioButton radioFixedDevice;
    }
}
