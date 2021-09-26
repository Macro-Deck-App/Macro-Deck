
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Server;
using System;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    partial class DeviceManagerView
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
            MacroDeckServer.OnDeviceConnectionStateChanged -= this.OnClientsChanged;
            DeviceManager.OnDevicesChange -= this.OnClientsChanged;
            
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
            this.devicesList = new System.Windows.Forms.FlowLayoutPanel();
            this.lblKnownDevices = new System.Windows.Forms.Label();
            this.lblBehaviour = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.radioBlockNew = new System.Windows.Forms.RadioButton();
            this.radioAllowAll = new System.Windows.Forms.RadioButton();
            this.radioAskNewConnections = new System.Windows.Forms.RadioButton();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // devicesList
            // 
            this.devicesList.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.devicesList.AutoScroll = true;
            this.devicesList.Location = new System.Drawing.Point(3, 29);
            this.devicesList.Name = "devicesList";
            this.devicesList.Size = new System.Drawing.Size(570, 510);
            this.devicesList.TabIndex = 12;
            // 
            // lblKnownDevices
            // 
            this.lblKnownDevices.AutoSize = true;
            this.lblKnownDevices.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblKnownDevices.ForeColor = System.Drawing.Color.White;
            this.lblKnownDevices.Location = new System.Drawing.Point(3, 3);
            this.lblKnownDevices.Name = "lblKnownDevices";
            this.lblKnownDevices.Size = new System.Drawing.Size(134, 23);
            this.lblKnownDevices.TabIndex = 13;
            this.lblKnownDevices.Text = "Known devices";
            // 
            // lblBehaviour
            // 
            this.lblBehaviour.AutoSize = true;
            this.lblBehaviour.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblBehaviour.ForeColor = System.Drawing.Color.White;
            this.lblBehaviour.Location = new System.Drawing.Point(583, 3);
            this.lblBehaviour.Name = "lblBehaviour";
            this.lblBehaviour.Size = new System.Drawing.Size(93, 23);
            this.lblBehaviour.TabIndex = 14;
            this.lblBehaviour.Text = "Behaviour";
            // 
            // panel1
            // 
            this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panel1.Controls.Add(this.radioBlockNew);
            this.panel1.Controls.Add(this.radioAllowAll);
            this.panel1.Controls.Add(this.radioAskNewConnections);
            this.panel1.Location = new System.Drawing.Point(583, 29);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(537, 96);
            this.panel1.TabIndex = 15;
            // 
            // radioBlockNew
            // 
            this.radioBlockNew.AutoSize = true;
            this.radioBlockNew.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioBlockNew.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.radioBlockNew.Location = new System.Drawing.Point(3, 61);
            this.radioBlockNew.Name = "radioBlockNew";
            this.radioBlockNew.Size = new System.Drawing.Size(207, 23);
            this.radioBlockNew.TabIndex = 2;
            this.radioBlockNew.TabStop = true;
            this.radioBlockNew.Text = "Block all new connections";
            this.radioBlockNew.UseVisualStyleBackColor = true;
            this.radioBlockNew.CheckedChanged += new System.EventHandler(this.RadioBehaviour_CheckedChanged);
            // 
            // radioAllowAll
            // 
            this.radioAllowAll.AutoSize = true;
            this.radioAllowAll.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioAllowAll.ForeColor = System.Drawing.Color.White;
            this.radioAllowAll.Location = new System.Drawing.Point(3, 32);
            this.radioAllowAll.Name = "radioAllowAll";
            this.radioAllowAll.Size = new System.Drawing.Size(358, 23);
            this.radioAllowAll.TabIndex = 1;
            this.radioAllowAll.TabStop = true;
            this.radioAllowAll.Text = "Allow all new connections (Not recommended)";
            this.radioAllowAll.UseVisualStyleBackColor = true;
            this.radioAllowAll.CheckedChanged += new System.EventHandler(this.RadioBehaviour_CheckedChanged);
            // 
            // radioAskNewConnections
            // 
            this.radioAskNewConnections.AutoSize = true;
            this.radioAskNewConnections.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioAskNewConnections.ForeColor = System.Drawing.Color.White;
            this.radioAskNewConnections.Location = new System.Drawing.Point(3, 3);
            this.radioAskNewConnections.Name = "radioAskNewConnections";
            this.radioAskNewConnections.Size = new System.Drawing.Size(198, 23);
            this.radioAskNewConnections.TabIndex = 0;
            this.radioAskNewConnections.TabStop = true;
            this.radioAskNewConnections.Text = "Ask on new connections";
            this.radioAskNewConnections.UseVisualStyleBackColor = true;
            this.radioAskNewConnections.CheckedChanged += new System.EventHandler(this.RadioBehaviour_CheckedChanged);
            // 
            // DeviceManagerView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.lblBehaviour);
            this.Controls.Add(this.lblKnownDevices);
            this.Controls.Add(this.devicesList);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "DeviceManagerView";
            this.Size = new System.Drawing.Size(1123, 542);
            this.Load += new System.EventHandler(this.DeviceManagerPage_Load);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.FlowLayoutPanel devicesList;
        private System.Windows.Forms.Label lblKnownDevices;
        private System.Windows.Forms.Label lblBehaviour;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.RadioButton radioBlockNew;
        private System.Windows.Forms.RadioButton radioAllowAll;
        private System.Windows.Forms.RadioButton radioAskNewConnections;
    }
}
