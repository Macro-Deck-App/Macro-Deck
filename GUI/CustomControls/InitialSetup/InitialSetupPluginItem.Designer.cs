
namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class InitialSetupPluginItem
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
            this.lblAuthor = new System.Windows.Forms.Label();
            this.icon = new System.Windows.Forms.PictureBox();
            this.lblDescription = new System.Windows.Forms.Label();
            this.checkInstall = new System.Windows.Forms.CheckBox();
            this.lblDownloads = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.icon)).BeginInit();
            this.SuspendLayout();
            // 
            // lblName
            // 
            this.lblName.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblName.Location = new System.Drawing.Point(34, 4);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(234, 22);
            this.lblName.TabIndex = 0;
            this.lblName.Text = "label1";
            // 
            // lblAuthor
            // 
            this.lblAuthor.Location = new System.Drawing.Point(34, 27);
            this.lblAuthor.Name = "lblAuthor";
            this.lblAuthor.Size = new System.Drawing.Size(145, 17);
            this.lblAuthor.TabIndex = 2;
            this.lblAuthor.Text = "label1";
            // 
            // icon
            // 
            this.icon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.icon.Location = new System.Drawing.Point(34, 47);
            this.icon.Name = "icon";
            this.icon.Size = new System.Drawing.Size(65, 65);
            this.icon.TabIndex = 3;
            this.icon.TabStop = false;
            // 
            // lblDescription
            // 
            this.lblDescription.Location = new System.Drawing.Point(105, 47);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(191, 65);
            this.lblDescription.TabIndex = 4;
            this.lblDescription.Text = "label1";
            // 
            // checkInstall
            // 
            this.checkInstall.CheckAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.checkInstall.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkInstall.Location = new System.Drawing.Point(3, 3);
            this.checkInstall.Name = "checkInstall";
            this.checkInstall.Size = new System.Drawing.Size(25, 111);
            this.checkInstall.TabIndex = 5;
            this.checkInstall.UseVisualStyleBackColor = true;
            // 
            // lblDownloads
            // 
            this.lblDownloads.Location = new System.Drawing.Point(185, 26);
            this.lblDownloads.Name = "lblDownloads";
            this.lblDownloads.Size = new System.Drawing.Size(111, 17);
            this.lblDownloads.TabIndex = 7;
            this.lblDownloads.Text = "0 downloads";
            this.lblDownloads.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // InitialSetupPluginItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.lblDownloads);
            this.Controls.Add(this.checkInstall);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.icon);
            this.Controls.Add(this.lblAuthor);
            this.Controls.Add(this.lblName);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "InitialSetupPluginItem";
            this.Size = new System.Drawing.Size(299, 117);
            ((System.ComponentModel.ISupportInitialize)(this.icon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.Label lblAuthor;
        private System.Windows.Forms.PictureBox icon;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.CheckBox checkInstall;
        private System.Windows.Forms.Label lblDownloads;
    }
}
