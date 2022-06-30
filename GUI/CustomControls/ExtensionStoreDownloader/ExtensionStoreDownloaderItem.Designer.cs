
namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionStoreDownloader
{
    partial class ExtensionStoreDownloaderItem
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
            this.extensionIcon = new System.Windows.Forms.PictureBox();
            this.lblPackageName = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.progressBar = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnAbort = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            ((System.ComponentModel.ISupportInitialize)(this.extensionIcon)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnAbort)).BeginInit();
            this.SuspendLayout();
            // 
            // extensionIcon
            // 
            this.extensionIcon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.extensionIcon.Location = new System.Drawing.Point(8, 8);
            this.extensionIcon.Name = "extensionIcon";
            this.extensionIcon.Size = new System.Drawing.Size(59, 59);
            this.extensionIcon.TabIndex = 0;
            this.extensionIcon.TabStop = false;
            // 
            // lblPackageName
            // 
            this.lblPackageName.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblPackageName.ForeColor = System.Drawing.Color.White;
            this.lblPackageName.Location = new System.Drawing.Point(73, 8);
            this.lblPackageName.Name = "lblPackageName";
            this.lblPackageName.Size = new System.Drawing.Size(474, 22);
            this.lblPackageName.TabIndex = 1;
            // 
            // lblStatus
            // 
            this.lblStatus.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblStatus.ForeColor = System.Drawing.Color.White;
            this.lblStatus.Location = new System.Drawing.Point(73, 36);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(262, 22);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // progressBar
            // 
            this.progressBar.BorderRadius = 8;
            this.progressBar.Cursor = System.Windows.Forms.Cursors.Hand;
            this.progressBar.Enabled = false;
            this.progressBar.FlatAppearance.BorderSize = 0;
            this.progressBar.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.progressBar.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.progressBar.ForeColor = System.Drawing.Color.White;
            this.progressBar.HoverColor = System.Drawing.Color.Empty;
            this.progressBar.Icon = null;
            this.progressBar.Location = new System.Drawing.Point(341, 35);
            this.progressBar.Name = "progressBar";
            this.progressBar.Progress = 0;
            this.progressBar.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(205)))));
            this.progressBar.Size = new System.Drawing.Size(177, 22);
            this.progressBar.TabIndex = 3;
            this.progressBar.UseVisualStyleBackColor = true;
            this.progressBar.UseWindowsAccentColor = true;
            this.progressBar.Visible = false;
            // 
            // btnAbort
            // 
            this.btnAbort.BackColor = System.Drawing.Color.Transparent;
            this.btnAbort.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Close_Normal;
            this.btnAbort.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnAbort.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAbort.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Close_Hover;
            this.btnAbort.Location = new System.Drawing.Point(524, 35);
            this.btnAbort.Name = "btnAbort";
            this.btnAbort.Size = new System.Drawing.Size(23, 23);
            this.btnAbort.TabIndex = 4;
            this.btnAbort.TabStop = false;
            this.btnAbort.Click += new System.EventHandler(this.BtnAbort_Click);
            // 
            // ExtensionStoreDownloaderItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.btnAbort);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblPackageName);
            this.Controls.Add(this.extensionIcon);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "ExtensionStoreDownloaderItem";
            this.Size = new System.Drawing.Size(555, 75);
            this.Load += new System.EventHandler(this.ExtensionStoreDownloaderItem_Load);
            ((System.ComponentModel.ISupportInitialize)(this.extensionIcon)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnAbort)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.PictureBox extensionIcon;
        private System.Windows.Forms.Label lblPackageName;
        private System.Windows.Forms.Label lblStatus;
        private ButtonPrimary progressBar;
        private PictureButton btnAbort;
    }
}
