namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    partial class ExtensionStoreItemView
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
            btnInstall = new ButtonPrimary();
            lblDescription = new System.Windows.Forms.Label();
            lblExtensionName = new System.Windows.Forms.Label();
            extensionIcon = new System.Windows.Forms.PictureBox();
            lblExtensionType = new System.Windows.Forms.Label();
            lblAuthor = new System.Windows.Forms.Label();
            lblRepository = new System.Windows.Forms.LinkLabel();
            ((System.ComponentModel.ISupportInitialize)extensionIcon).BeginInit();
            SuspendLayout();
            // 
            // btnInstall
            // 
            btnInstall.BorderRadius = 8;
            btnInstall.Cursor = System.Windows.Forms.Cursors.Hand;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnInstall.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            btnInstall.ForeColor = System.Drawing.Color.White;
            btnInstall.HoverColor = System.Drawing.Color.Empty;
            btnInstall.Icon = null;
            btnInstall.Location = new System.Drawing.Point(153, 129);
            btnInstall.Name = "btnInstall";
            btnInstall.Progress = 0;
            btnInstall.ProgressColor = System.Drawing.Color.FromArgb(0, 103, 225);
            btnInstall.Size = new System.Drawing.Size(140, 27);
            btnInstall.TabIndex = 12;
            btnInstall.Text = "Install";
            btnInstall.UseMnemonic = false;
            btnInstall.UseVisualStyleBackColor = true;
            btnInstall.UseWindowsAccentColor = false;
            btnInstall.Click += BtnInstall_Click;
            // 
            // lblDescription
            // 
            lblDescription.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            lblDescription.ForeColor = System.Drawing.Color.White;
            lblDescription.Location = new System.Drawing.Point(64, 71);
            lblDescription.Name = "lblDescription";
            lblDescription.Size = new System.Drawing.Size(229, 40);
            lblDescription.TabIndex = 11;
            lblDescription.Text = "label1";
            lblDescription.UseMnemonic = false;
            // 
            // lblExtensionName
            // 
            lblExtensionName.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            lblExtensionName.ForeColor = System.Drawing.Color.White;
            lblExtensionName.Location = new System.Drawing.Point(64, 26);
            lblExtensionName.Name = "lblExtensionName";
            lblExtensionName.Size = new System.Drawing.Size(229, 23);
            lblExtensionName.TabIndex = 10;
            lblExtensionName.Text = "label1";
            lblExtensionName.UseMnemonic = false;
            // 
            // extensionIcon
            // 
            extensionIcon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            extensionIcon.Location = new System.Drawing.Point(8, 26);
            extensionIcon.Name = "extensionIcon";
            extensionIcon.Size = new System.Drawing.Size(50, 50);
            extensionIcon.TabIndex = 9;
            extensionIcon.TabStop = false;
            // 
            // lblExtensionType
            // 
            lblExtensionType.ForeColor = System.Drawing.Color.White;
            lblExtensionType.Location = new System.Drawing.Point(182, 5);
            lblExtensionType.Name = "lblExtensionType";
            lblExtensionType.Size = new System.Drawing.Size(111, 21);
            lblExtensionType.TabIndex = 13;
            lblExtensionType.Text = "label1";
            lblExtensionType.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            lblExtensionType.UseMnemonic = false;
            // 
            // lblAuthor
            // 
            lblAuthor.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            lblAuthor.ForeColor = System.Drawing.Color.LightGray;
            lblAuthor.Location = new System.Drawing.Point(64, 49);
            lblAuthor.Name = "lblAuthor";
            lblAuthor.Size = new System.Drawing.Size(229, 21);
            lblAuthor.TabIndex = 14;
            lblAuthor.Text = "label1";
            lblAuthor.UseMnemonic = false;
            // 
            // lblRepository
            // 
            lblRepository.ActiveLinkColor = System.Drawing.Color.White;
            lblRepository.LinkColor = System.Drawing.Color.White;
            lblRepository.Location = new System.Drawing.Point(8, 129);
            lblRepository.Name = "lblRepository";
            lblRepository.Size = new System.Drawing.Size(139, 27);
            lblRepository.TabIndex = 15;
            lblRepository.TabStop = true;
            lblRepository.Text = "Repository";
            lblRepository.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            lblRepository.VisitedLinkColor = System.Drawing.Color.White;
            lblRepository.LinkClicked += LblRepository_LinkClicked;
            // 
            // ExtensionStoreItemView
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            BackColor = System.Drawing.Color.FromArgb(65, 65, 65);
            Controls.Add(lblRepository);
            Controls.Add(lblAuthor);
            Controls.Add(lblExtensionType);
            Controls.Add(btnInstall);
            Controls.Add(lblDescription);
            Controls.Add(lblExtensionName);
            Controls.Add(extensionIcon);
            Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            Name = "ExtensionStoreItemView";
            Size = new System.Drawing.Size(301, 164);
            ((System.ComponentModel.ISupportInitialize)extensionIcon).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private ButtonPrimary btnInstall;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.Label lblExtensionName;
        private System.Windows.Forms.PictureBox extensionIcon;
        private System.Windows.Forms.Label lblExtensionType;
        private System.Windows.Forms.Label lblAuthor;
        private System.Windows.Forms.LinkLabel lblRepository;
    }
}
