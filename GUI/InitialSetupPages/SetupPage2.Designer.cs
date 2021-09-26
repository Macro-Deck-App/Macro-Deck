
namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage2
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SetupPage2));
            this.lblConfigureNetwork = new System.Windows.Forms.Label();
            this.adapter = new System.Windows.Forms.ComboBox();
            this.lblNetworkAdapter = new System.Windows.Forms.Label();
            this.iPAddress = new System.Windows.Forms.Label();
            this.lblIpAddress = new System.Windows.Forms.Label();
            this.lblPort = new System.Windows.Forms.Label();
            this.port = new System.Windows.Forms.NumericUpDown();
            this.groupInfo = new System.Windows.Forms.GroupBox();
            this.lblInfo = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.port)).BeginInit();
            this.groupInfo.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblConfigureNetwork
            // 
            this.lblConfigureNetwork.Font = new System.Drawing.Font("Tahoma", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblConfigureNetwork.ForeColor = System.Drawing.Color.White;
            this.lblConfigureNetwork.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.lblConfigureNetwork.Location = new System.Drawing.Point(3, 0);
            this.lblConfigureNetwork.Name = "lblConfigureNetwork";
            this.lblConfigureNetwork.Size = new System.Drawing.Size(685, 48);
            this.lblConfigureNetwork.TabIndex = 3;
            this.lblConfigureNetwork.Text = "Configure your network settings\r\n";
            this.lblConfigureNetwork.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // adapter
            // 
            this.adapter.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.adapter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.adapter.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.adapter.ForeColor = System.Drawing.Color.White;
            this.adapter.FormattingEnabled = true;
            this.adapter.Location = new System.Drawing.Point(141, 118);
            this.adapter.Name = "adapter";
            this.adapter.Size = new System.Drawing.Size(547, 31);
            this.adapter.TabIndex = 4;
            this.adapter.SelectedIndexChanged += new System.EventHandler(this.Adapter_SelectedIndexChanged);
            // 
            // lblNetworkAdapter
            // 
            this.lblNetworkAdapter.AutoSize = true;
            this.lblNetworkAdapter.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblNetworkAdapter.ForeColor = System.Drawing.Color.White;
            this.lblNetworkAdapter.Location = new System.Drawing.Point(3, 124);
            this.lblNetworkAdapter.Name = "lblNetworkAdapter";
            this.lblNetworkAdapter.Size = new System.Drawing.Size(132, 19);
            this.lblNetworkAdapter.TabIndex = 5;
            this.lblNetworkAdapter.Text = "Network adapter:";
            // 
            // iPAddress
            // 
            this.iPAddress.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.iPAddress.ForeColor = System.Drawing.Color.White;
            this.iPAddress.Location = new System.Drawing.Point(141, 152);
            this.iPAddress.Name = "iPAddress";
            this.iPAddress.Size = new System.Drawing.Size(547, 24);
            this.iPAddress.TabIndex = 6;
            this.iPAddress.Click += new System.EventHandler(this.lblIpAddress_Click);
            // 
            // lblIpAddress
            // 
            this.lblIpAddress.AutoSize = true;
            this.lblIpAddress.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIpAddress.ForeColor = System.Drawing.Color.White;
            this.lblIpAddress.Location = new System.Drawing.Point(3, 152);
            this.lblIpAddress.Name = "lblIpAddress";
            this.lblIpAddress.Size = new System.Drawing.Size(89, 19);
            this.lblIpAddress.TabIndex = 7;
            this.lblIpAddress.Text = "IP address:";
            // 
            // lblPort
            // 
            this.lblPort.AutoSize = true;
            this.lblPort.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPort.ForeColor = System.Drawing.Color.White;
            this.lblPort.Location = new System.Drawing.Point(3, 229);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(44, 19);
            this.lblPort.TabIndex = 8;
            this.lblPort.Text = "Port:";
            // 
            // port
            // 
            this.port.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.port.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.port.ForeColor = System.Drawing.Color.White;
            this.port.Location = new System.Drawing.Point(141, 224);
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
            this.port.Size = new System.Drawing.Size(120, 30);
            this.port.TabIndex = 9;
            this.port.Value = new decimal(new int[] {
            8191,
            0,
            0,
            0});
            this.port.ValueChanged += new System.EventHandler(this.port_ValueChanged);
            // 
            // groupInfo
            // 
            this.groupInfo.Controls.Add(this.lblInfo);
            this.groupInfo.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.groupInfo.ForeColor = System.Drawing.Color.White;
            this.groupInfo.Location = new System.Drawing.Point(3, 311);
            this.groupInfo.Name = "groupInfo";
            this.groupInfo.Size = new System.Drawing.Size(685, 205);
            this.groupInfo.TabIndex = 10;
            this.groupInfo.TabStop = false;
            this.groupInfo.Text = "Info";
            // 
            // lblInfo
            // 
            this.lblInfo.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblInfo.Location = new System.Drawing.Point(6, 26);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(673, 167);
            this.lblInfo.TabIndex = 0;
            this.lblInfo.Text = resources.GetString("lblInfo.Text");
            // 
            // SetupPage2
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.groupInfo);
            this.Controls.Add(this.port);
            this.Controls.Add(this.lblPort);
            this.Controls.Add(this.lblIpAddress);
            this.Controls.Add(this.iPAddress);
            this.Controls.Add(this.lblNetworkAdapter);
            this.Controls.Add(this.adapter);
            this.Controls.Add(this.lblConfigureNetwork);
            this.Name = "SetupPage2";
            this.Size = new System.Drawing.Size(691, 571);
            this.Load += new System.EventHandler(this.SetupPage2_Load);
            ((System.ComponentModel.ISupportInitialize)(this.port)).EndInit();
            this.groupInfo.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblConfigureNetwork;
        private System.Windows.Forms.ComboBox adapter;
        private System.Windows.Forms.Label lblNetworkAdapter;
        private System.Windows.Forms.Label iPAddress;
        private System.Windows.Forms.Label lblIpAddress;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.NumericUpDown port;
        private System.Windows.Forms.GroupBox groupInfo;
        private System.Windows.Forms.Label lblInfo;
    }
}
