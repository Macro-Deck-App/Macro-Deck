
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using ComboBox = SuchByte.MacroDeck.GUI.CustomControls.ComboBox;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage2
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
            ComponentResourceManager resources = new ComponentResourceManager(typeof(SetupPage2));
            this.lblConfigureNetwork = new Label();
            this.adapter = new ComboBox();
            this.lblNetworkAdapter = new Label();
            this.iPAddress = new Label();
            this.lblIpAddress = new Label();
            this.lblPort = new Label();
            this.port = new NumericUpDown();
            this.groupInfo = new GroupBox();
            this.lblInfo = new Label();
            ((ISupportInitialize)(this.port)).BeginInit();
            this.groupInfo.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblConfigureNetwork
            // 
            this.lblConfigureNetwork.Font = new Font("Tahoma", 24F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblConfigureNetwork.ForeColor = Color.White;
            this.lblConfigureNetwork.ImageAlign = ContentAlignment.BottomCenter;
            this.lblConfigureNetwork.Location = new Point(3, 0);
            this.lblConfigureNetwork.Name = "lblConfigureNetwork";
            this.lblConfigureNetwork.Size = new Size(685, 48);
            this.lblConfigureNetwork.TabIndex = 3;
            this.lblConfigureNetwork.Text = "Configure your network settings\r\n";
            this.lblConfigureNetwork.TextAlign = ContentAlignment.TopCenter;
            this.lblConfigureNetwork.UseMnemonic = false;
            // 
            // adapter
            // 
            this.adapter.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.adapter.BorderRadius = 8;
            this.adapter.Cursor = Cursors.Hand;
            this.adapter.DropDownStyle = ComboBoxStyle.DropDownList;
            this.adapter.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.adapter.ForeColor = Color.White;
            this.adapter.FormattingEnabled = true;
            this.adapter.Location = new Point(141, 118);
            this.adapter.Name = "adapter";
            this.adapter.Size = new Size(359, 24);
            this.adapter.TabIndex = 4;
            this.adapter.SelectedIndexChanged += new EventHandler(this.Adapter_SelectedIndexChanged);
            // 
            // lblNetworkAdapter
            // 
            this.lblNetworkAdapter.AutoSize = true;
            this.lblNetworkAdapter.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblNetworkAdapter.ForeColor = Color.White;
            this.lblNetworkAdapter.Location = new Point(3, 124);
            this.lblNetworkAdapter.Name = "lblNetworkAdapter";
            this.lblNetworkAdapter.Size = new Size(132, 19);
            this.lblNetworkAdapter.TabIndex = 5;
            this.lblNetworkAdapter.Text = "Network adapter:";
            this.lblNetworkAdapter.UseMnemonic = false;
            // 
            // iPAddress
            // 
            this.iPAddress.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.iPAddress.ForeColor = Color.White;
            this.iPAddress.Location = new Point(141, 152);
            this.iPAddress.Name = "iPAddress";
            this.iPAddress.Size = new Size(547, 24);
            this.iPAddress.TabIndex = 6;
            this.iPAddress.UseMnemonic = false;
            this.iPAddress.Click += new EventHandler(this.lblIpAddress_Click);
            // 
            // lblIpAddress
            // 
            this.lblIpAddress.AutoSize = true;
            this.lblIpAddress.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblIpAddress.ForeColor = Color.White;
            this.lblIpAddress.Location = new Point(3, 152);
            this.lblIpAddress.Name = "lblIpAddress";
            this.lblIpAddress.Size = new Size(89, 19);
            this.lblIpAddress.TabIndex = 7;
            this.lblIpAddress.Text = "IP address:";
            this.lblIpAddress.UseMnemonic = false;
            // 
            // lblPort
            // 
            this.lblPort.AutoSize = true;
            this.lblPort.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblPort.ForeColor = Color.White;
            this.lblPort.Location = new Point(3, 229);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new Size(44, 19);
            this.lblPort.TabIndex = 8;
            this.lblPort.Text = "Port:";
            this.lblPort.UseMnemonic = false;
            // 
            // port
            // 
            this.port.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.port.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.port.ForeColor = Color.White;
            this.port.Location = new Point(141, 224);
            this.port.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.port.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.port.Name = "port";
            this.port.Size = new Size(120, 30);
            this.port.TabIndex = 9;
            this.port.Value = new decimal(new int[] {
            8191,
            0,
            0,
            0});
            this.port.ValueChanged += new EventHandler(this.port_ValueChanged);
            // 
            // groupInfo
            // 
            this.groupInfo.Controls.Add(this.lblInfo);
            this.groupInfo.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.groupInfo.ForeColor = Color.White;
            this.groupInfo.Location = new Point(3, 311);
            this.groupInfo.Name = "groupInfo";
            this.groupInfo.Size = new Size(685, 205);
            this.groupInfo.TabIndex = 10;
            this.groupInfo.TabStop = false;
            this.groupInfo.Text = "Info";
            // 
            // lblInfo
            // 
            this.lblInfo.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblInfo.Location = new Point(6, 26);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new Size(673, 167);
            this.lblInfo.TabIndex = 0;
            this.lblInfo.Text = resources.GetString("lblInfo.Text");
            this.lblInfo.UseMnemonic = false;
            // 
            // SetupPage2
            // 
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.groupInfo);
            this.Controls.Add(this.port);
            this.Controls.Add(this.lblPort);
            this.Controls.Add(this.lblIpAddress);
            this.Controls.Add(this.iPAddress);
            this.Controls.Add(this.lblNetworkAdapter);
            this.Controls.Add(this.adapter);
            this.Controls.Add(this.lblConfigureNetwork);
            this.Name = "SetupPage2";
            this.Size = new Size(691, 571);
            this.Load += new EventHandler(this.SetupPage2_Load);
            ((ISupportInitialize)(this.port)).EndInit();
            this.groupInfo.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Label lblConfigureNetwork;
        private ComboBox adapter;
        private Label lblNetworkAdapter;
        private Label iPAddress;
        private Label lblIpAddress;
        private Label lblPort;
        private NumericUpDown port;
        private GroupBox groupInfo;
        private Label lblInfo;
    }
}
