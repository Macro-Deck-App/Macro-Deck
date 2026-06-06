
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views
{
    partial class SetBrightnessActionConfigView
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
            this.radioFixedDevice = new RadioButton();
            this.radioCurrentDevice = new RadioButton();
            this.lblBrightness = new Label();
            this.lblDevice = new Label();
            this.devicesList = new RoundedComboBox();
            this.brightness = new TrackBar();
            ((ISupportInitialize)(this.brightness)).BeginInit();
            this.SuspendLayout();
            // 
            // radioFixedDevice
            // 
            this.radioFixedDevice.AutoSize = true;
            this.radioFixedDevice.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioFixedDevice.Location = new Point(484, 185);
            this.radioFixedDevice.Name = "radioFixedDevice";
            this.radioFixedDevice.Size = new Size(14, 13);
            this.radioFixedDevice.TabIndex = 11;
            this.radioFixedDevice.UseVisualStyleBackColor = true;
            // 
            // radioCurrentDevice
            // 
            this.radioCurrentDevice.Checked = true;
            this.radioCurrentDevice.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioCurrentDevice.Location = new Point(271, 176);
            this.radioCurrentDevice.Name = "radioCurrentDevice";
            this.radioCurrentDevice.Size = new Size(207, 30);
            this.radioCurrentDevice.TabIndex = 10;
            this.radioCurrentDevice.TabStop = true;
            this.radioCurrentDevice.Text = "Where executed";
            this.radioCurrentDevice.UseVisualStyleBackColor = true;
            // 
            // lblBrightness
            // 
            this.lblBrightness.Anchor = AnchorStyles.None;
            this.lblBrightness.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblBrightness.Location = new Point(153, 218);
            this.lblBrightness.Name = "lblBrightness";
            this.lblBrightness.Size = new Size(112, 30);
            this.lblBrightness.TabIndex = 9;
            this.lblBrightness.Text = "Brightness";
            this.lblBrightness.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblDevice
            // 
            this.lblDevice.Anchor = AnchorStyles.None;
            this.lblDevice.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblDevice.Location = new Point(153, 176);
            this.lblDevice.Name = "lblDevice";
            this.lblDevice.Size = new Size(112, 30);
            this.lblDevice.TabIndex = 8;
            this.lblDevice.Text = "Device";
            this.lblDevice.TextAlign = ContentAlignment.MiddleLeft;
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
            this.devicesList.TabIndex = 6;
            this.devicesList.SelectedIndexChanged += new EventHandler(this.DevicesList_SelectedIndexChanged);
            // 
            // brightness
            // 
            this.brightness.Location = new Point(271, 225);
            this.brightness.Minimum = 1;
            this.brightness.Name = "brightness";
            this.brightness.Size = new Size(188, 45);
            this.brightness.TabIndex = 12;
            this.brightness.TickStyle = TickStyle.None;
            this.brightness.Value = 10;
            this.brightness.Scroll += new EventHandler(this.Brightness_Scroll);
            // 
            // SetBrightnessActionConfigView
            // 
            this.AutoScaleDimensions = new SizeF(10F, 23F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Controls.Add(this.brightness);
            this.Controls.Add(this.radioFixedDevice);
            this.Controls.Add(this.radioCurrentDevice);
            this.Controls.Add(this.lblBrightness);
            this.Controls.Add(this.lblDevice);
            this.Controls.Add(this.devicesList);
            this.Name = "SetBrightnessActionConfigView";
            this.Load += new EventHandler(this.SetBrightnessActionConfigView_Load);
            ((ISupportInitialize)(this.brightness)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private RadioButton radioFixedDevice;
        private RadioButton radioCurrentDevice;
        private Label lblBrightness;
        private Label lblDevice;
        private RoundedComboBox devicesList;
        private TrackBar brightness;
    }
}
