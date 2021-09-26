﻿
namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class PackageManagerIconPackItem
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
            this.lblVersion = new System.Windows.Forms.Label();
            this.lblDownloads = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblDescription = new System.Windows.Forms.Label();
            this.preview = new System.Windows.Forms.PictureBox();
            this.lblAuthor = new System.Windows.Forms.Label();
            this.btnInstall = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblName = new System.Windows.Forms.Label();
            this.lblType = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.preview)).BeginInit();
            this.SuspendLayout();
            // 
            // lblVersion
            // 
            this.lblVersion.Location = new System.Drawing.Point(252, 76);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(57, 22);
            this.lblVersion.TabIndex = 15;
            this.lblVersion.Text = "1.0.0.0";
            this.lblVersion.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // lblDownloads
            // 
            this.lblDownloads.Location = new System.Drawing.Point(179, 27);
            this.lblDownloads.Name = "lblDownloads";
            this.lblDownloads.Size = new System.Drawing.Size(126, 17);
            this.lblDownloads.TabIndex = 14;
            this.lblDownloads.Text = "0 downloads";
            this.lblDownloads.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(3, 150);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(198, 10);
            this.progressBar.TabIndex = 13;
            this.progressBar.Visible = false;
            // 
            // lblDescription
            // 
            this.lblDescription.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDescription.Location = new System.Drawing.Point(3, 113);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(304, 21);
            this.lblDescription.TabIndex = 12;
            this.lblDescription.Text = "Description";
            // 
            // preview
            // 
            this.preview.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.preview.Location = new System.Drawing.Point(3, 3);
            this.preview.Name = "preview";
            this.preview.Size = new System.Drawing.Size(158, 68);
            this.preview.TabIndex = 11;
            this.preview.TabStop = false;
            // 
            // lblAuthor
            // 
            this.lblAuthor.Location = new System.Drawing.Point(3, 96);
            this.lblAuthor.Name = "lblAuthor";
            this.lblAuthor.Size = new System.Drawing.Size(121, 17);
            this.lblAuthor.TabIndex = 10;
            this.lblAuthor.Text = "Author";
            this.lblAuthor.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
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
            this.btnInstall.TabIndex = 9;
            this.btnInstall.Text = "Install";
            this.btnInstall.UseVisualStyleBackColor = false;
            this.btnInstall.Click += new System.EventHandler(this.btnInstall_Click);
            // 
            // lblName
            // 
            this.lblName.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblName.Location = new System.Drawing.Point(3, 74);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(243, 22);
            this.lblName.TabIndex = 8;
            this.lblName.Text = "Icon pack name";
            this.lblName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblType
            // 
            this.lblType.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(173)))), ((int)(((byte)(14)))));
            this.lblType.Location = new System.Drawing.Point(213, 2);
            this.lblType.Name = "lblType";
            this.lblType.Size = new System.Drawing.Size(94, 17);
            this.lblType.TabIndex = 16;
            this.lblType.Text = "Icon pack";
            this.lblType.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // PackageManagerIconPackItem
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
            this.Controls.Add(this.preview);
            this.Controls.Add(this.lblAuthor);
            this.Controls.Add(this.btnInstall);
            this.Controls.Add(this.lblName);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "PackageManagerIconPackItem";
            this.Size = new System.Drawing.Size(310, 163);
            ((System.ComponentModel.ISupportInitialize)(this.preview)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.Label lblDownloads;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.PictureBox preview;
        private System.Windows.Forms.Label lblAuthor;
        private ButtonPrimary btnInstall;
        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.Label lblType;
    }
}
