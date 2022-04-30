
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
            this.installedExtensionsList = new System.Windows.Forms.FlowLayoutPanel();
            this.btnCheckUpdates = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblUpdateState = new System.Windows.Forms.Label();
            this.btnAddViaZip = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.SuspendLayout();
            // 
            // installedExtensionsList
            // 
            this.installedExtensionsList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.installedExtensionsList.AutoScroll = true;
            this.installedExtensionsList.Location = new System.Drawing.Point(0, 0);
            this.installedExtensionsList.Name = "installedExtensionsList";
            this.installedExtensionsList.Size = new System.Drawing.Size(1137, 456);
            this.installedExtensionsList.TabIndex = 1;
            // 
            // btnCheckUpdates
            // 
            this.btnCheckUpdates.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnCheckUpdates.BorderRadius = 8;
            this.btnCheckUpdates.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCheckUpdates.FlatAppearance.BorderSize = 0;
            this.btnCheckUpdates.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckUpdates.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnCheckUpdates.ForeColor = System.Drawing.Color.White;
            this.btnCheckUpdates.HoverColor = System.Drawing.Color.Empty;
            this.btnCheckUpdates.Icon = null;
            this.btnCheckUpdates.Location = new System.Drawing.Point(230, 462);
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
            this.lblUpdateState.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblUpdateState.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblUpdateState.ForeColor = System.Drawing.Color.Silver;
            this.lblUpdateState.Location = new System.Drawing.Point(4, 463);
            this.lblUpdateState.Name = "lblUpdateState";
            this.lblUpdateState.Size = new System.Drawing.Size(220, 30);
            this.lblUpdateState.TabIndex = 4;
            this.lblUpdateState.Text = "All extensions are up-to-date";
            this.lblUpdateState.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblUpdateState.UseMnemonic = false;
            // 
            // btnAddViaZip
            // 
            this.btnAddViaZip.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAddViaZip.BorderRadius = 8;
            this.btnAddViaZip.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddViaZip.FlatAppearance.BorderSize = 0;
            this.btnAddViaZip.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddViaZip.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddViaZip.ForeColor = System.Drawing.Color.White;
            this.btnAddViaZip.HoverColor = System.Drawing.Color.Empty;
            this.btnAddViaZip.Icon = null;
            this.btnAddViaZip.Location = new System.Drawing.Point(942, 463);
            this.btnAddViaZip.Name = "btnAddViaZip";
            this.btnAddViaZip.Progress = 0;
            this.btnAddViaZip.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnAddViaZip.Size = new System.Drawing.Size(192, 29);
            this.btnAddViaZip.TabIndex = 5;
            this.btnAddViaZip.Text = "Install from file";
            this.btnAddViaZip.UseVisualStyleBackColor = true;
            this.btnAddViaZip.UseWindowsAccentColor = true;
            this.btnAddViaZip.Click += new System.EventHandler(this.BtnAddViaZip_Click);
            // 
            // InstalledExtensionsView
            // 
            
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.btnAddViaZip);
            this.Controls.Add(this.lblUpdateState);
            this.Controls.Add(this.btnCheckUpdates);
            this.Controls.Add(this.installedExtensionsList);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "InstalledExtensionsView";
            this.Size = new System.Drawing.Size(1137, 495);
            this.Load += new System.EventHandler(this.InstalledExtensionsView_Load);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.FlowLayoutPanel installedExtensionsList;
        private ButtonPrimary btnCheckUpdates;
        private System.Windows.Forms.Label lblUpdateState;
        private ButtonPrimary btnAddViaZip;
    }
}
