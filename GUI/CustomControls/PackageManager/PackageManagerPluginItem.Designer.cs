
using System;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class PackageManagerPluginItem
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
            this.lblName = new System.Windows.Forms.Label();
            this.btnInstall = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblAuthor = new System.Windows.Forms.Label();
            this.icon = new System.Windows.Forms.PictureBox();
            this.lblDescription = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblDownloads = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            this.lblType = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.icon)).BeginInit();
            this.SuspendLayout();
            // 
            // lblName
            // 
            this.lblName.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblName.Location = new System.Drawing.Point(3, 4);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(238, 23);
            this.lblName.TabIndex = 0;
            this.lblName.Text = "Plugin name";
            this.lblName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnInstall
            // 
            this.btnInstall.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnInstall.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnInstall.FlatAppearance.BorderSize = 0;
            this.btnInstall.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnInstall.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnInstall.ForeColor = System.Drawing.Color.White;
            this.btnInstall.Location = new System.Drawing.Point(207, 137);
            this.btnInstall.Name = "btnInstall";
            this.btnInstall.Size = new System.Drawing.Size(100, 23);
            this.btnInstall.TabIndex = 1;
            this.btnInstall.Text = "Install";
            this.btnInstall.UseVisualStyleBackColor = false;
            this.btnInstall.Click += new System.EventHandler(this.BtnInstall_Click);
            // 
            // lblAuthor
            // 
            this.lblAuthor.Location = new System.Drawing.Point(3, 44);
            this.lblAuthor.Name = "lblAuthor";
            this.lblAuthor.Size = new System.Drawing.Size(145, 17);
            this.lblAuthor.TabIndex = 2;
            this.lblAuthor.Text = "Author";
            this.lblAuthor.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // icon
            // 
            this.icon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.icon.Location = new System.Drawing.Point(3, 70);
            this.icon.Name = "icon";
            this.icon.Size = new System.Drawing.Size(90, 90);
            this.icon.TabIndex = 3;
            this.icon.TabStop = false;
            // 
            // lblDescription
            // 
            this.lblDescription.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDescription.Location = new System.Drawing.Point(103, 70);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(202, 64);
            this.lblDescription.TabIndex = 4;
            this.lblDescription.Text = "Description";
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(99, 150);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(102, 10);
            this.progressBar.TabIndex = 5;
            this.progressBar.Visible = false;
            // 
            // lblDownloads
            // 
            this.lblDownloads.Location = new System.Drawing.Point(179, 27);
            this.lblDownloads.Name = "lblDownloads";
            this.lblDownloads.Size = new System.Drawing.Size(126, 17);
            this.lblDownloads.TabIndex = 6;
            this.lblDownloads.Text = "0 downloads";
            this.lblDownloads.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // lblVersion
            // 
            this.lblVersion.Location = new System.Drawing.Point(3, 28);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(57, 16);
            this.lblVersion.TabIndex = 7;
            this.lblVersion.Text = "1.0.0.0";
            this.lblVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblType
            // 
            this.lblType.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(95)))), ((int)(((byte)(173)))));
            this.lblType.Location = new System.Drawing.Point(247, 2);
            this.lblType.Name = "lblType";
            this.lblType.Size = new System.Drawing.Size(60, 17);
            this.lblType.TabIndex = 8;
            this.lblType.Text = "Plugin";
            this.lblType.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // PackageManagerPluginItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.lblType);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.lblDownloads);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.icon);
            this.Controls.Add(this.lblAuthor);
            this.Controls.Add(this.btnInstall);
            this.Controls.Add(this.lblName);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "PackageManagerPluginItem";
            this.Size = new System.Drawing.Size(310, 163);
            ((System.ComponentModel.ISupportInitialize)(this.icon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblName;
        private ButtonPrimary btnInstall;
        private System.Windows.Forms.Label lblAuthor;
        private System.Windows.Forms.PictureBox icon;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblDownloads;
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.Label lblType;
    }
}
