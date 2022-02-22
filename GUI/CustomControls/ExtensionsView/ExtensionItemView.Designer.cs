
namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    partial class ExtensionItemView
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
            this.lblExtensionType = new System.Windows.Forms.Label();
            this.extensionIcon = new System.Windows.Forms.PictureBox();
            this.lblExtensionName = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            this.btnConfigure = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnUninstall = new System.Windows.Forms.LinkLabel();
            this.btnUpdate = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            ((System.ComponentModel.ISupportInitialize)(this.extensionIcon)).BeginInit();
            this.SuspendLayout();
            // 
            // lblExtensionType
            // 
            this.lblExtensionType.ForeColor = System.Drawing.Color.White;
            this.lblExtensionType.Location = new System.Drawing.Point(182, 5);
            this.lblExtensionType.Name = "lblExtensionType";
            this.lblExtensionType.Size = new System.Drawing.Size(111, 21);
            this.lblExtensionType.TabIndex = 0;
            this.lblExtensionType.Text = "label1";
            this.lblExtensionType.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // extensionIcon
            // 
            this.extensionIcon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.extensionIcon.Location = new System.Drawing.Point(8, 26);
            this.extensionIcon.Name = "extensionIcon";
            this.extensionIcon.Size = new System.Drawing.Size(50, 50);
            this.extensionIcon.TabIndex = 1;
            this.extensionIcon.TabStop = false;
            // 
            // lblExtensionName
            // 
            this.lblExtensionName.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblExtensionName.ForeColor = System.Drawing.Color.White;
            this.lblExtensionName.Location = new System.Drawing.Point(64, 26);
            this.lblExtensionName.Name = "lblExtensionName";
            this.lblExtensionName.Size = new System.Drawing.Size(229, 23);
            this.lblExtensionName.TabIndex = 2;
            this.lblExtensionName.Text = "label1";
            // 
            // lblVersion
            // 
            this.lblVersion.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblVersion.ForeColor = System.Drawing.Color.White;
            this.lblVersion.Location = new System.Drawing.Point(64, 52);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(229, 27);
            this.lblVersion.TabIndex = 3;
            this.lblVersion.Text = "label1";
            this.lblVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnConfigure
            // 
            this.btnConfigure.BorderRadius = 8;
            this.btnConfigure.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnConfigure.FlatAppearance.BorderSize = 0;
            this.btnConfigure.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnConfigure.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnConfigure.ForeColor = System.Drawing.Color.White;
            this.btnConfigure.HoverColor = System.Drawing.Color.Empty;
            this.btnConfigure.Icon = null;
            this.btnConfigure.Location = new System.Drawing.Point(8, 114);
            this.btnConfigure.Name = "btnConfigure";
            this.btnConfigure.Progress = 0;
            this.btnConfigure.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnConfigure.Size = new System.Drawing.Size(139, 40);
            this.btnConfigure.TabIndex = 4;
            this.btnConfigure.Text = "Configure";
            this.btnConfigure.UseVisualStyleBackColor = true;
            this.btnConfigure.UseWindowsAccentColor = true;
            this.btnConfigure.Click += new System.EventHandler(this.BtnConfigure_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.BackColor = System.Drawing.Color.Transparent;
            this.lblStatus.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(192)))), ((int)(((byte)(0)))));
            this.lblStatus.Location = new System.Drawing.Point(8, 4);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(168, 21);
            this.lblStatus.TabIndex = 6;
            this.lblStatus.Text = "Active";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnUninstall
            // 
            this.btnUninstall.ActiveLinkColor = System.Drawing.Color.DarkGray;
            this.btnUninstall.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.btnUninstall.ForeColor = System.Drawing.Color.Silver;
            this.btnUninstall.LinkColor = System.Drawing.Color.Silver;
            this.btnUninstall.Location = new System.Drawing.Point(153, 122);
            this.btnUninstall.Name = "btnUninstall";
            this.btnUninstall.Size = new System.Drawing.Size(140, 25);
            this.btnUninstall.TabIndex = 7;
            this.btnUninstall.TabStop = true;
            this.btnUninstall.Text = "Uninstall";
            this.btnUninstall.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnUninstall.VisitedLinkColor = System.Drawing.Color.Silver;
            this.btnUninstall.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.BtnUninstall_LinkClicked);
            // 
            // btnUpdate
            // 
            this.btnUpdate.BorderRadius = 8;
            this.btnUpdate.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnUpdate.FlatAppearance.BorderSize = 0;
            this.btnUpdate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnUpdate.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnUpdate.ForeColor = System.Drawing.Color.White;
            this.btnUpdate.HoverColor = System.Drawing.Color.Empty;
            this.btnUpdate.Icon = null;
            this.btnUpdate.Location = new System.Drawing.Point(153, 82);
            this.btnUpdate.Name = "btnUpdate";
            this.btnUpdate.Progress = 0;
            this.btnUpdate.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnUpdate.Size = new System.Drawing.Size(140, 27);
            this.btnUpdate.TabIndex = 8;
            this.btnUpdate.Text = "Update";
            this.btnUpdate.UseVisualStyleBackColor = true;
            this.btnUpdate.UseWindowsAccentColor = false;
            this.btnUpdate.Visible = false;
            this.btnUpdate.Click += new System.EventHandler(this.BtnUpdate_Click);
            // 
            // ExtensionItemView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.btnUpdate);
            this.Controls.Add(this.btnUninstall);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnConfigure);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.lblExtensionName);
            this.Controls.Add(this.extensionIcon);
            this.Controls.Add(this.lblExtensionType);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "ExtensionItemView";
            this.Size = new System.Drawing.Size(301, 164);
            ((System.ComponentModel.ISupportInitialize)(this.extensionIcon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblExtensionType;
        private System.Windows.Forms.PictureBox extensionIcon;
        private System.Windows.Forms.Label lblExtensionName;
        private System.Windows.Forms.Label lblVersion;
        private ButtonPrimary btnConfigure;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.LinkLabel btnUninstall;
        private ButtonPrimary btnUpdate;
    }
}
