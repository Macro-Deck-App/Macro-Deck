
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionStoreDownloader
{
    partial class ExtensionStoreDownloaderItem
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
            this.extensionIcon = new PictureBox();
            this.lblPackageName = new Label();
            this.lblStatus = new Label();
            this.progressBar = new ButtonPrimary();
            this.btnAbort = new PictureButton();
            ((ISupportInitialize)(this.extensionIcon)).BeginInit();
            ((ISupportInitialize)(this.btnAbort)).BeginInit();
            this.SuspendLayout();
            // 
            // extensionIcon
            // 
            this.extensionIcon.BackgroundImageLayout = ImageLayout.Stretch;
            this.extensionIcon.Location = new Point(8, 8);
            this.extensionIcon.Name = "extensionIcon";
            this.extensionIcon.Size = new Size(59, 59);
            this.extensionIcon.TabIndex = 0;
            this.extensionIcon.TabStop = false;
            // 
            // lblPackageName
            // 
            this.lblPackageName.Font = new Font("Tahoma", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblPackageName.ForeColor = Color.White;
            this.lblPackageName.Location = new Point(73, 8);
            this.lblPackageName.Name = "lblPackageName";
            this.lblPackageName.Size = new Size(474, 22);
            this.lblPackageName.TabIndex = 1;
            // 
            // lblStatus
            // 
            this.lblStatus.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblStatus.ForeColor = Color.White;
            this.lblStatus.Location = new Point(73, 36);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new Size(262, 22);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // progressBar
            // 
            this.progressBar.BorderRadius = 8;
            this.progressBar.Cursor = Cursors.Hand;
            this.progressBar.Enabled = false;
            this.progressBar.FlatAppearance.BorderSize = 0;
            this.progressBar.FlatStyle = FlatStyle.Flat;
            this.progressBar.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.progressBar.ForeColor = Color.White;
            this.progressBar.HoverColor = Color.Empty;
            this.progressBar.Icon = null;
            this.progressBar.Location = new Point(341, 35);
            this.progressBar.Name = "progressBar";
            this.progressBar.Progress = 0;
            this.progressBar.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(205)))));
            this.progressBar.Size = new Size(177, 22);
            this.progressBar.TabIndex = 3;
            this.progressBar.UseVisualStyleBackColor = true;
            this.progressBar.UseWindowsAccentColor = true;
            this.progressBar.Visible = false;
            // 
            // btnAbort
            // 
            this.btnAbort.BackColor = Color.Transparent;
            this.btnAbort.BackgroundImage = Resources.Close_Normal;
            this.btnAbort.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnAbort.Cursor = Cursors.Hand;
            this.btnAbort.HoverImage = Resources.Close_Hover;
            this.btnAbort.Location = new Point(524, 35);
            this.btnAbort.Name = "btnAbort";
            this.btnAbort.Size = new Size(23, 23);
            this.btnAbort.TabIndex = 4;
            this.btnAbort.TabStop = false;
            this.btnAbort.Click += new EventHandler(this.BtnAbort_Click);
            // 
            // ExtensionStoreDownloaderItem
            // 
            this.AutoScaleDimensions = new SizeF(7F, 14F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.btnAbort);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblPackageName);
            this.Controls.Add(this.extensionIcon);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "ExtensionStoreDownloaderItem";
            this.Size = new Size(555, 75);
            this.Load += new EventHandler(this.ExtensionStoreDownloaderItem_Load);
            ((ISupportInitialize)(this.extensionIcon)).EndInit();
            ((ISupportInitialize)(this.btnAbort)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private PictureBox extensionIcon;
        private Label lblPackageName;
        private Label lblStatus;
        private ButtonPrimary progressBar;
        private PictureButton btnAbort;
    }
}
