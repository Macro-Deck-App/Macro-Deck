
namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views
{
    partial class SetBrightnessActionConfigView
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
            this.radioFixedDevice = new System.Windows.Forms.RadioButton();
            this.radioCurrentDevice = new System.Windows.Forms.RadioButton();
            this.lblBrightness = new System.Windows.Forms.Label();
            this.lblDevice = new System.Windows.Forms.Label();
            this.devicesList = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.brightness = new System.Windows.Forms.TrackBar();
            ((System.ComponentModel.ISupportInitialize)(this.brightness)).BeginInit();
            this.SuspendLayout();
            // 
            // radioFixedDevice
            // 
            this.radioFixedDevice.AutoSize = true;
            this.radioFixedDevice.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioFixedDevice.Location = new System.Drawing.Point(484, 185);
            this.radioFixedDevice.Name = "radioFixedDevice";
            this.radioFixedDevice.Size = new System.Drawing.Size(14, 13);
            this.radioFixedDevice.TabIndex = 11;
            this.radioFixedDevice.UseVisualStyleBackColor = true;
            // 
            // radioCurrentDevice
            // 
            this.radioCurrentDevice.Checked = true;
            this.radioCurrentDevice.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioCurrentDevice.Location = new System.Drawing.Point(271, 176);
            this.radioCurrentDevice.Name = "radioCurrentDevice";
            this.radioCurrentDevice.Size = new System.Drawing.Size(207, 30);
            this.radioCurrentDevice.TabIndex = 10;
            this.radioCurrentDevice.TabStop = true;
            this.radioCurrentDevice.Text = "Where executed";
            this.radioCurrentDevice.UseVisualStyleBackColor = true;
            // 
            // lblBrightness
            // 
            this.lblBrightness.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.lblBrightness.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblBrightness.Location = new System.Drawing.Point(153, 218);
            this.lblBrightness.Name = "lblBrightness";
            this.lblBrightness.Size = new System.Drawing.Size(112, 30);
            this.lblBrightness.TabIndex = 9;
            this.lblBrightness.Text = "Brightness";
            this.lblBrightness.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblDevice
            // 
            this.lblDevice.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.lblDevice.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDevice.Location = new System.Drawing.Point(153, 176);
            this.lblDevice.Name = "lblDevice";
            this.lblDevice.Size = new System.Drawing.Size(112, 30);
            this.lblDevice.TabIndex = 8;
            this.lblDevice.Text = "Device";
            this.lblDevice.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // devicesList
            // 
            this.devicesList.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.devicesList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.devicesList.Cursor = System.Windows.Forms.Cursors.Hand;
            this.devicesList.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.devicesList.Enabled = false;
            this.devicesList.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.devicesList.Icon = null;
            this.devicesList.Location = new System.Drawing.Point(504, 176);
            this.devicesList.Name = "devicesList";
            this.devicesList.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.devicesList.SelectedIndex = -1;
            this.devicesList.SelectedItem = null;
            this.devicesList.Size = new System.Drawing.Size(199, 30);
            this.devicesList.TabIndex = 6;
            this.devicesList.SelectedIndexChanged += new System.EventHandler(this.DevicesList_SelectedIndexChanged);
            // 
            // brightness
            // 
            this.brightness.Location = new System.Drawing.Point(271, 225);
            this.brightness.Minimum = 1;
            this.brightness.Name = "brightness";
            this.brightness.Size = new System.Drawing.Size(188, 45);
            this.brightness.TabIndex = 12;
            this.brightness.TickStyle = System.Windows.Forms.TickStyle.None;
            this.brightness.Value = 10;
            this.brightness.Scroll += new System.EventHandler(this.Brightness_Scroll);
            // 
            // SetBrightnessActionConfigView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 23F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.brightness);
            this.Controls.Add(this.radioFixedDevice);
            this.Controls.Add(this.radioCurrentDevice);
            this.Controls.Add(this.lblBrightness);
            this.Controls.Add(this.lblDevice);
            this.Controls.Add(this.devicesList);
            this.Name = "SetBrightnessActionConfigView";
            this.Load += new System.EventHandler(this.SetBrightnessActionConfigView_Load);
            ((System.ComponentModel.ISupportInitialize)(this.brightness)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RadioButton radioFixedDevice;
        private System.Windows.Forms.RadioButton radioCurrentDevice;
        private System.Windows.Forms.Label lblBrightness;
        private System.Windows.Forms.Label lblDevice;
        private GUI.CustomControls.RoundedComboBox devicesList;
        private System.Windows.Forms.TrackBar brightness;
    }
}
