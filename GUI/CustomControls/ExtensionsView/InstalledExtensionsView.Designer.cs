
namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    partial class InstalledExtensionsView
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
            this.btnAddExtensions = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.installedExtensionsList = new System.Windows.Forms.FlowLayoutPanel();
            this.btnCheckUpdates = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblUpdateState = new System.Windows.Forms.Label();
            this.lblInstalledExtensions = new System.Windows.Forms.Label();
            this.btnAddViaZip = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.SuspendLayout();
            // 
            // btnAddExtensions
            // 
            this.btnAddExtensions.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAddExtensions.BorderRadius = 8;
            this.btnAddExtensions.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddExtensions.FlatAppearance.BorderSize = 0;
            this.btnAddExtensions.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddExtensions.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddExtensions.ForeColor = System.Drawing.Color.White;
            this.btnAddExtensions.HoverColor = System.Drawing.Color.Empty;
            this.btnAddExtensions.Icon = null;
            this.btnAddExtensions.Location = new System.Drawing.Point(904, 497);
            this.btnAddExtensions.Name = "btnAddExtensions";
            this.btnAddExtensions.Progress = 0;
            this.btnAddExtensions.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnAddExtensions.Size = new System.Drawing.Size(230, 40);
            this.btnAddExtensions.TabIndex = 0;
            this.btnAddExtensions.Text = "Install from Extension Store";
            this.btnAddExtensions.UseMnemonic = false;
            this.btnAddExtensions.UseVisualStyleBackColor = true;
            this.btnAddExtensions.UseWindowsAccentColor = true;
            this.btnAddExtensions.Click += new System.EventHandler(this.BtnAddExtensions_Click);
            // 
            // installedExtensionsList
            // 
            this.installedExtensionsList.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.installedExtensionsList.AutoScroll = true;
            this.installedExtensionsList.Location = new System.Drawing.Point(3, 36);
            this.installedExtensionsList.Name = "installedExtensionsList";
            this.installedExtensionsList.Size = new System.Drawing.Size(1131, 456);
            this.installedExtensionsList.TabIndex = 1;
            // 
            // btnCheckUpdates
            // 
            this.btnCheckUpdates.BorderRadius = 8;
            this.btnCheckUpdates.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCheckUpdates.FlatAppearance.BorderSize = 0;
            this.btnCheckUpdates.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckUpdates.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnCheckUpdates.ForeColor = System.Drawing.Color.White;
            this.btnCheckUpdates.HoverColor = System.Drawing.Color.Empty;
            this.btnCheckUpdates.Icon = null;
            this.btnCheckUpdates.Location = new System.Drawing.Point(237, 3);
            this.btnCheckUpdates.Name = "btnCheckUpdates";
            this.btnCheckUpdates.Progress = 0;
            this.btnCheckUpdates.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnCheckUpdates.Size = new System.Drawing.Size(191, 30);
            this.btnCheckUpdates.TabIndex = 3;
            this.btnCheckUpdates.Text = "Check updates";
            this.btnCheckUpdates.UseMnemonic = false;
            this.btnCheckUpdates.UseVisualStyleBackColor = true;
            this.btnCheckUpdates.UseWindowsAccentColor = true;
            this.btnCheckUpdates.Click += new System.EventHandler(this.BtnCheckUpdates_Click);
            // 
            // lblUpdateState
            // 
            this.lblUpdateState.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblUpdateState.ForeColor = System.Drawing.Color.Silver;
            this.lblUpdateState.Location = new System.Drawing.Point(11, 3);
            this.lblUpdateState.Name = "lblUpdateState";
            this.lblUpdateState.Size = new System.Drawing.Size(220, 30);
            this.lblUpdateState.TabIndex = 4;
            this.lblUpdateState.Text = "All extensions are up-to-date";
            this.lblUpdateState.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblUpdateState.UseMnemonic = false;
            // 
            // lblInstalledExtensions
            // 
            this.lblInstalledExtensions.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblInstalledExtensions.ForeColor = System.Drawing.Color.Silver;
            this.lblInstalledExtensions.Location = new System.Drawing.Point(434, 0);
            this.lblInstalledExtensions.Name = "lblInstalledExtensions";
            this.lblInstalledExtensions.Size = new System.Drawing.Size(268, 33);
            this.lblInstalledExtensions.TabIndex = 2;
            this.lblInstalledExtensions.Text = "Installed Extensions";
            this.lblInstalledExtensions.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblInstalledExtensions.UseMnemonic = false;
            // 
            // btnAddViaZip
            // 
            this.btnAddViaZip.BorderRadius = 8;
            this.btnAddViaZip.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddViaZip.FlatAppearance.BorderSize = 0;
            this.btnAddViaZip.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddViaZip.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddViaZip.ForeColor = System.Drawing.Color.White;
            this.btnAddViaZip.HoverColor = System.Drawing.Color.Empty;
            this.btnAddViaZip.Icon = null;
            this.btnAddViaZip.Location = new System.Drawing.Point(706, 497);
            this.btnAddViaZip.Name = "btnAddViaZip";
            this.btnAddViaZip.Progress = 0;
            this.btnAddViaZip.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnAddViaZip.Size = new System.Drawing.Size(192, 40);
            this.btnAddViaZip.TabIndex = 5;
            this.btnAddViaZip.Text = "Install from file";
            this.btnAddViaZip.UseVisualStyleBackColor = true;
            this.btnAddViaZip.UseWindowsAccentColor = true;
            this.btnAddViaZip.Click += new System.EventHandler(this.BtnAddViaZip_Click);
            // 
            // InstalledExtensionsView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.btnAddViaZip);
            this.Controls.Add(this.lblUpdateState);
            this.Controls.Add(this.btnCheckUpdates);
            this.Controls.Add(this.lblInstalledExtensions);
            this.Controls.Add(this.installedExtensionsList);
            this.Controls.Add(this.btnAddExtensions);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "InstalledExtensionsView";
            this.Size = new System.Drawing.Size(1137, 540);
            this.Load += new System.EventHandler(this.InstalledExtensionsView_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private ButtonPrimary btnAddExtensions;
        private System.Windows.Forms.FlowLayoutPanel installedExtensionsList;
        private ButtonPrimary btnCheckUpdates;
        private System.Windows.Forms.Label lblUpdateState;
        private System.Windows.Forms.Label lblInstalledExtensions;
        private ButtonPrimary btnAddViaZip;
    }
}
