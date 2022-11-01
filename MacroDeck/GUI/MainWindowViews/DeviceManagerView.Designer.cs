
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    partial class DeviceManagerView
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
            this.devicesList = new FlowLayoutPanel();
            this.lblKnownDevices = new Label();
            this.lblBehaviour = new Label();
            this.panel1 = new Panel();
            this.radioBlockNew = new RadioButton();
            this.radioAllowAll = new RadioButton();
            this.radioAskNewConnections = new RadioButton();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // devicesList
            // 
            this.devicesList.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                       | AnchorStyles.Left)));
            this.devicesList.AutoScroll = true;
            this.devicesList.Location = new Point(3, 29);
            this.devicesList.Name = "devicesList";
            this.devicesList.Size = new Size(570, 510);
            this.devicesList.TabIndex = 12;
            // 
            // lblKnownDevices
            // 
            this.lblKnownDevices.AutoSize = true;
            this.lblKnownDevices.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblKnownDevices.ForeColor = Color.White;
            this.lblKnownDevices.Location = new Point(3, 3);
            this.lblKnownDevices.Name = "lblKnownDevices";
            this.lblKnownDevices.Size = new Size(134, 23);
            this.lblKnownDevices.TabIndex = 13;
            this.lblKnownDevices.Text = "Known devices";
            this.lblKnownDevices.UseMnemonic = false;
            // 
            // lblBehaviour
            // 
            this.lblBehaviour.AutoSize = true;
            this.lblBehaviour.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblBehaviour.ForeColor = Color.White;
            this.lblBehaviour.Location = new Point(583, 3);
            this.lblBehaviour.Name = "lblBehaviour";
            this.lblBehaviour.Size = new Size(93, 23);
            this.lblBehaviour.TabIndex = 14;
            this.lblBehaviour.Text = "Behaviour";
            this.lblBehaviour.UseMnemonic = false;
            // 
            // panel1
            // 
            this.panel1.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Left) 
                                                  | AnchorStyles.Right)));
            this.panel1.Controls.Add(this.radioBlockNew);
            this.panel1.Controls.Add(this.radioAllowAll);
            this.panel1.Controls.Add(this.radioAskNewConnections);
            this.panel1.Location = new Point(583, 29);
            this.panel1.Name = "panel1";
            this.panel1.Size = new Size(537, 96);
            this.panel1.TabIndex = 15;
            // 
            // radioBlockNew
            // 
            this.radioBlockNew.AutoSize = true;
            this.radioBlockNew.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioBlockNew.ForeColor = Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.radioBlockNew.Location = new Point(3, 61);
            this.radioBlockNew.Name = "radioBlockNew";
            this.radioBlockNew.Size = new Size(207, 23);
            this.radioBlockNew.TabIndex = 2;
            this.radioBlockNew.TabStop = true;
            this.radioBlockNew.Text = "Block all new connections";
            this.radioBlockNew.UseMnemonic = false;
            this.radioBlockNew.UseVisualStyleBackColor = true;
            this.radioBlockNew.CheckedChanged += new EventHandler(this.RadioBehaviour_CheckedChanged);
            // 
            // radioAllowAll
            // 
            this.radioAllowAll.AutoSize = true;
            this.radioAllowAll.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioAllowAll.ForeColor = Color.White;
            this.radioAllowAll.Location = new Point(3, 32);
            this.radioAllowAll.Name = "radioAllowAll";
            this.radioAllowAll.Size = new Size(358, 23);
            this.radioAllowAll.TabIndex = 1;
            this.radioAllowAll.TabStop = true;
            this.radioAllowAll.Text = "Allow all new connections (Not recommended)";
            this.radioAllowAll.UseMnemonic = false;
            this.radioAllowAll.UseVisualStyleBackColor = true;
            this.radioAllowAll.CheckedChanged += new EventHandler(this.RadioBehaviour_CheckedChanged);
            // 
            // radioAskNewConnections
            // 
            this.radioAskNewConnections.AutoSize = true;
            this.radioAskNewConnections.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioAskNewConnections.ForeColor = Color.White;
            this.radioAskNewConnections.Location = new Point(3, 3);
            this.radioAskNewConnections.Name = "radioAskNewConnections";
            this.radioAskNewConnections.Size = new Size(198, 23);
            this.radioAskNewConnections.TabIndex = 0;
            this.radioAskNewConnections.TabStop = true;
            this.radioAskNewConnections.Text = "Ask on new connections";
            this.radioAskNewConnections.UseMnemonic = false;
            this.radioAskNewConnections.UseVisualStyleBackColor = true;
            this.radioAskNewConnections.CheckedChanged += new EventHandler(this.RadioBehaviour_CheckedChanged);
            // 
            // DeviceManagerView
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.lblBehaviour);
            this.Controls.Add(this.lblKnownDevices);
            this.Controls.Add(this.devicesList);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "DeviceManagerView";
            this.Size = new Size(1123, 542);
            this.Load += new EventHandler(this.DeviceManagerPage_Load);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private FlowLayoutPanel devicesList;
        private Label lblKnownDevices;
        private Label lblBehaviour;
        private Panel panel1;
        private RadioButton radioBlockNew;
        private RadioButton radioAllowAll;
        private RadioButton radioAskNewConnections;
    }
}
