
namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views
{
    partial class SetProfileActionConfigView
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
            this.devicesList = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.profilesList = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.lblDevice = new System.Windows.Forms.Label();
            this.lblProfile = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // devicesList
            // 
            this.devicesList.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.devicesList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.devicesList.Cursor = System.Windows.Forms.Cursors.Hand;
            this.devicesList.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.devicesList.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.devicesList.Icon = null;
            this.devicesList.Location = new System.Drawing.Point(271, 176);
            this.devicesList.Name = "devicesList";
            this.devicesList.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.devicesList.SelectedIndex = -1;
            this.devicesList.SelectedItem = null;
            this.devicesList.Size = new System.Drawing.Size(432, 30);
            this.devicesList.TabIndex = 0;
            // 
            // profilesList
            // 
            this.profilesList.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.profilesList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.profilesList.Cursor = System.Windows.Forms.Cursors.Hand;
            this.profilesList.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.profilesList.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.profilesList.Icon = null;
            this.profilesList.Location = new System.Drawing.Point(271, 218);
            this.profilesList.Name = "profilesList";
            this.profilesList.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.profilesList.SelectedIndex = -1;
            this.profilesList.SelectedItem = null;
            this.profilesList.Size = new System.Drawing.Size(432, 30);
            this.profilesList.TabIndex = 1;
            // 
            // lblDevice
            // 
            this.lblDevice.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.lblDevice.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDevice.Location = new System.Drawing.Point(153, 176);
            this.lblDevice.Name = "lblDevice";
            this.lblDevice.Size = new System.Drawing.Size(112, 30);
            this.lblDevice.TabIndex = 2;
            this.lblDevice.Text = "Device";
            this.lblDevice.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblProfile
            // 
            this.lblProfile.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.lblProfile.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblProfile.Location = new System.Drawing.Point(153, 218);
            this.lblProfile.Name = "lblProfile";
            this.lblProfile.Size = new System.Drawing.Size(112, 30);
            this.lblProfile.TabIndex = 3;
            this.lblProfile.Text = "Profile";
            this.lblProfile.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SetProfileActionConfigView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 23F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.lblProfile);
            this.Controls.Add(this.lblDevice);
            this.Controls.Add(this.profilesList);
            this.Controls.Add(this.devicesList);
            this.Name = "SetProfileActionConfigView";
            this.Load += new System.EventHandler(this.SetProfileActionConfigView_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private GUI.CustomControls.RoundedComboBox devicesList;
        private GUI.CustomControls.RoundedComboBox profilesList;
        private System.Windows.Forms.Label lblDevice;
        private System.Windows.Forms.Label lblProfile;
    }
}
