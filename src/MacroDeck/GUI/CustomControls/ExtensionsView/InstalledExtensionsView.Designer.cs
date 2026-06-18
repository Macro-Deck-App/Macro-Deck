
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    partial class InstalledExtensionsView
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
            ExtensionStore.ExtensionStoreHelper.OnUpdateCheckFinished -= UpdateCheckFinished;
            ExtensionStore.ExtensionStoreHelper.OnInstallationFinished -= ExtensionStoreHelper_OnInstallationFinished;

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
            this.txtSearch = new RoundedTextBox();
            this.installedExtensionsGrid = new ExtensionGrid();
            this.btnCheckUpdates = new ButtonPrimary();
            this.lblUpdateState = new Label();
            this.btnAddViaZip = new ButtonPrimary();
            this.SuspendLayout();
            //
            // txtSearch
            //
            this.txtSearch.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Left) | AnchorStyles.Right)));
            this.txtSearch.BackColor = Color.FromArgb(65, 65, 65);
            this.txtSearch.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.txtSearch.ForeColor = Color.White;
            this.txtSearch.Icon = null;
            this.txtSearch.Location = new Point(4, 6);
            this.txtSearch.MaxCharacters = 32767;
            this.txtSearch.Multiline = false;
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Padding = new Padding(8, 5, 8, 5);
            this.txtSearch.PasswordChar = false;
            this.txtSearch.PlaceHolderColor = Color.Gray;
            this.txtSearch.PlaceHolderText = "Search installed extensions…";
            this.txtSearch.ReadOnly = false;
            this.txtSearch.ScrollBars = ScrollBars.None;
            this.txtSearch.SelectionStart = 0;
            this.txtSearch.Size = new Size(1130, 30);
            this.txtSearch.TabIndex = 0;
            this.txtSearch.TextAlignment = HorizontalAlignment.Left;
            this.txtSearch.TextChanged += new EventHandler(this.TxtSearch_TextChanged);
            //
            // installedExtensionsGrid
            //
            this.installedExtensionsGrid.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom)
                                                                    | AnchorStyles.Left)
                                                                   | AnchorStyles.Right)));
            this.installedExtensionsGrid.BackColor = Color.FromArgb(45, 45, 45);
            this.installedExtensionsGrid.Location = new Point(0, 42);
            this.installedExtensionsGrid.Name = "installedExtensionsGrid";
            this.installedExtensionsGrid.Size = new Size(1137, 414);
            this.installedExtensionsGrid.TabIndex = 1;
            //
            // btnCheckUpdates
            //
            this.btnCheckUpdates.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Left)));
            this.btnCheckUpdates.BorderRadius = 8;
            this.btnCheckUpdates.Cursor = Cursors.Hand;
            this.btnCheckUpdates.FlatAppearance.BorderSize = 0;
            this.btnCheckUpdates.FlatStyle = FlatStyle.Flat;
            this.btnCheckUpdates.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnCheckUpdates.ForeColor = Color.White;
            this.btnCheckUpdates.HoverColor = Color.Empty;
            this.btnCheckUpdates.Icon = null;
            this.btnCheckUpdates.Location = new Point(230, 462);
            this.btnCheckUpdates.Name = "btnCheckUpdates";
            this.btnCheckUpdates.Progress = 0;
            this.btnCheckUpdates.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnCheckUpdates.Size = new Size(191, 30);
            this.btnCheckUpdates.TabIndex = 3;
            this.btnCheckUpdates.Text = "Check updates";
            this.btnCheckUpdates.UseMnemonic = false;
            this.btnCheckUpdates.UseVisualStyleBackColor = true;
            this.btnCheckUpdates.UseWindowsAccentColor = true;
            this.btnCheckUpdates.Click += new EventHandler(this.BtnCheckUpdates_Click);
            //
            // lblUpdateState
            //
            this.lblUpdateState.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Left)));
            this.lblUpdateState.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblUpdateState.ForeColor = Color.Silver;
            this.lblUpdateState.Location = new Point(4, 463);
            this.lblUpdateState.Name = "lblUpdateState";
            this.lblUpdateState.Size = new Size(220, 30);
            this.lblUpdateState.TabIndex = 4;
            this.lblUpdateState.Text = "All extensions are up-to-date";
            this.lblUpdateState.TextAlign = ContentAlignment.MiddleLeft;
            this.lblUpdateState.UseMnemonic = false;
            //
            // btnAddViaZip
            //
            this.btnAddViaZip.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.btnAddViaZip.BorderRadius = 8;
            this.btnAddViaZip.Cursor = Cursors.Hand;
            this.btnAddViaZip.FlatAppearance.BorderSize = 0;
            this.btnAddViaZip.FlatStyle = FlatStyle.Flat;
            this.btnAddViaZip.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnAddViaZip.ForeColor = Color.White;
            this.btnAddViaZip.HoverColor = Color.Empty;
            this.btnAddViaZip.Icon = null;
            this.btnAddViaZip.Location = new Point(942, 463);
            this.btnAddViaZip.Name = "btnAddViaZip";
            this.btnAddViaZip.Progress = 0;
            this.btnAddViaZip.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnAddViaZip.Size = new Size(192, 29);
            this.btnAddViaZip.TabIndex = 5;
            this.btnAddViaZip.Text = "Install from file";
            this.btnAddViaZip.UseVisualStyleBackColor = true;
            this.btnAddViaZip.UseWindowsAccentColor = true;
            this.btnAddViaZip.Click += new EventHandler(this.BtnAddViaZip_Click);
            //
            // InstalledExtensionsView
            //

            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.btnAddViaZip);
            this.Controls.Add(this.lblUpdateState);
            this.Controls.Add(this.btnCheckUpdates);
            this.Controls.Add(this.installedExtensionsGrid);
            this.Controls.Add(this.txtSearch);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Name = "InstalledExtensionsView";
            this.Size = new Size(1137, 495);
            this.Load += new EventHandler(this.InstalledExtensionsView_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private RoundedTextBox txtSearch;
        private ExtensionGrid installedExtensionsGrid;
        private ButtonPrimary btnCheckUpdates;
        private Label lblUpdateState;
        private ButtonPrimary btnAddViaZip;
    }
}
