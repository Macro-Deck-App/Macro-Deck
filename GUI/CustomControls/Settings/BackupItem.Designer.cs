
namespace SuchByte.MacroDeck.GUI.CustomControls.Settings
{
    partial class BackupItem
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
            this.lblFileName = new System.Windows.Forms.Label();
            this.lblDateCreated = new System.Windows.Forms.Label();
            this.lblSize = new System.Windows.Forms.Label();
            this.btnDelete = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnRestore = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.SuspendLayout();
            // 
            // lblFileName
            // 
            this.lblFileName.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblFileName.Location = new System.Drawing.Point(8, 4);
            this.lblFileName.Name = "lblFileName";
            this.lblFileName.Size = new System.Drawing.Size(484, 42);
            this.lblFileName.TabIndex = 0;
            this.lblFileName.Text = "label1";
            this.lblFileName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblDateCreated
            // 
            this.lblDateCreated.Location = new System.Drawing.Point(498, 4);
            this.lblDateCreated.Name = "lblDateCreated";
            this.lblDateCreated.Size = new System.Drawing.Size(194, 42);
            this.lblDateCreated.TabIndex = 1;
            this.lblDateCreated.Text = "label1";
            this.lblDateCreated.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // lblSize
            // 
            this.lblSize.Location = new System.Drawing.Point(698, 4);
            this.lblSize.Name = "lblSize";
            this.lblSize.Size = new System.Drawing.Size(89, 42);
            this.lblSize.TabIndex = 2;
            this.lblSize.Text = "label1";
            this.lblSize.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // btnDelete
            // 
            this.btnDelete.BorderRadius = 8;
            this.btnDelete.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDelete.FlatAppearance.BorderSize = 0;
            this.btnDelete.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDelete.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDelete.ForeColor = System.Drawing.Color.White;
            this.btnDelete.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.btnDelete.Icon = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnDelete.Location = new System.Drawing.Point(847, 8);
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Progress = 0;
            this.btnDelete.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnDelete.Size = new System.Drawing.Size(35, 35);
            this.btnDelete.TabIndex = 3;
            this.btnDelete.UseVisualStyleBackColor = false;
            this.btnDelete.UseWindowsAccentColor = false;
            this.btnDelete.Click += new System.EventHandler(this.btnDelete_Click);
            // 
            // btnRestore
            // 
            this.btnRestore.BorderRadius = 8;
            this.btnRestore.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRestore.FlatAppearance.BorderSize = 0;
            this.btnRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRestore.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnRestore.ForeColor = System.Drawing.Color.White;
            this.btnRestore.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnRestore.Icon = global::SuchByte.MacroDeck.Properties.Resources.Backup_Restore;
            this.btnRestore.Location = new System.Drawing.Point(806, 8);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Progress = 0;
            this.btnRestore.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnRestore.Size = new System.Drawing.Size(35, 35);
            this.btnRestore.TabIndex = 4;
            this.btnRestore.UseVisualStyleBackColor = false;
            this.btnRestore.UseWindowsAccentColor = true;
            this.btnRestore.Click += new System.EventHandler(this.BtnRestore_Click);
            // 
            // BackupItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.btnRestore);
            this.Controls.Add(this.btnDelete);
            this.Controls.Add(this.lblSize);
            this.Controls.Add(this.lblDateCreated);
            this.Controls.Add(this.lblFileName);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "BackupItem";
            this.Padding = new System.Windows.Forms.Padding(5, 4, 5, 4);
            this.Size = new System.Drawing.Size(890, 50);
            this.Load += new System.EventHandler(this.BackupItem_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblFileName;
        private System.Windows.Forms.Label lblDateCreated;
        private System.Windows.Forms.Label lblSize;
        private ButtonPrimary btnDelete;
        private ButtonPrimary btnRestore;
    }
}
