
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    partial class ExtensionItemView
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
            lblExtensionType = new Label();
            extensionIcon = new PictureBox();
            lblExtensionName = new Label();
            lblVersion = new Label();
            btnConfigure = new ButtonPrimary();
            lblStatus = new Label();
            btnUninstall = new LinkLabel();
            btnUpdate = new ButtonPrimary();
            ((ISupportInitialize)extensionIcon).BeginInit();
            SuspendLayout();
            // 
            // lblExtensionType
            // 
            lblExtensionType.ForeColor = Color.White;
            lblExtensionType.Location = new Point(182, 5);
            lblExtensionType.Name = "lblExtensionType";
            lblExtensionType.Size = new Size(111, 21);
            lblExtensionType.TabIndex = 0;
            lblExtensionType.Text = "label1";
            lblExtensionType.TextAlign = ContentAlignment.MiddleCenter;
            lblExtensionType.UseMnemonic = false;
            // 
            // extensionIcon
            // 
            extensionIcon.BackgroundImageLayout = ImageLayout.Stretch;
            extensionIcon.Location = new Point(8, 26);
            extensionIcon.Name = "extensionIcon";
            extensionIcon.Size = new Size(50, 50);
            extensionIcon.TabIndex = 1;
            extensionIcon.TabStop = false;
            // 
            // lblExtensionName
            // 
            lblExtensionName.Font = new Font("Tahoma", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
            lblExtensionName.ForeColor = Color.White;
            lblExtensionName.Location = new Point(64, 26);
            lblExtensionName.Name = "lblExtensionName";
            lblExtensionName.Size = new Size(229, 23);
            lblExtensionName.TabIndex = 2;
            lblExtensionName.Text = "label1";
            lblExtensionName.UseMnemonic = false;
            // 
            // lblVersion
            // 
            lblVersion.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            lblVersion.ForeColor = Color.White;
            lblVersion.Location = new Point(64, 52);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(229, 27);
            lblVersion.TabIndex = 3;
            lblVersion.Text = "label1";
            lblVersion.TextAlign = ContentAlignment.MiddleLeft;
            lblVersion.UseMnemonic = false;
            // 
            // btnConfigure
            // 
            btnConfigure.BorderRadius = 8;
            btnConfigure.Cursor = Cursors.Hand;
            btnConfigure.FlatAppearance.BorderSize = 0;
            btnConfigure.FlatStyle = FlatStyle.Flat;
            btnConfigure.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnConfigure.ForeColor = Color.White;
            btnConfigure.HoverColor = Color.Empty;
            btnConfigure.Icon = null;
            btnConfigure.Location = new Point(8, 114);
            btnConfigure.Name = "btnConfigure";
            btnConfigure.Progress = 0;
            btnConfigure.ProgressColor = Color.FromArgb(0, 103, 225);
            btnConfigure.Size = new Size(139, 40);
            btnConfigure.TabIndex = 4;
            btnConfigure.Text = "Configure";
            btnConfigure.UseMnemonic = false;
            btnConfigure.UseVisualStyleBackColor = true;
            btnConfigure.UseWindowsAccentColor = true;
            btnConfigure.Click += BtnConfigure_Click;
            // 
            // lblStatus
            // 
            lblStatus.BackColor = Color.Transparent;
            lblStatus.Font = new Font("Tahoma", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
            lblStatus.ForeColor = Color.FromArgb(0, 192, 0);
            lblStatus.Location = new Point(8, 4);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(168, 21);
            lblStatus.TabIndex = 6;
            lblStatus.Text = "Active";
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.UseMnemonic = false;
            // 
            // btnUninstall
            // 
            btnUninstall.ActiveLinkColor = Color.DarkGray;
            btnUninstall.Font = new Font("Tahoma", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
            btnUninstall.ForeColor = Color.Silver;
            btnUninstall.LinkColor = Color.Silver;
            btnUninstall.Location = new Point(153, 122);
            btnUninstall.Name = "btnUninstall";
            btnUninstall.Size = new Size(140, 25);
            btnUninstall.TabIndex = 7;
            btnUninstall.TabStop = true;
            btnUninstall.Text = "Uninstall";
            btnUninstall.TextAlign = ContentAlignment.MiddleCenter;
            btnUninstall.UseMnemonic = false;
            btnUninstall.VisitedLinkColor = Color.Silver;
            btnUninstall.LinkClicked += BtnUninstall_LinkClicked;
            // 
            // btnUpdate
            // 
            btnUpdate.BorderRadius = 8;
            btnUpdate.Cursor = Cursors.Hand;
            btnUpdate.FlatAppearance.BorderSize = 0;
            btnUpdate.FlatStyle = FlatStyle.Flat;
            btnUpdate.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            btnUpdate.ForeColor = Color.White;
            btnUpdate.HoverColor = Color.Empty;
            btnUpdate.Icon = null;
            btnUpdate.Location = new Point(153, 82);
            btnUpdate.Name = "btnUpdate";
            btnUpdate.Progress = 0;
            btnUpdate.ProgressColor = Color.FromArgb(0, 103, 225);
            btnUpdate.Size = new Size(140, 27);
            btnUpdate.TabIndex = 8;
            btnUpdate.Text = "Update";
            btnUpdate.UseMnemonic = false;
            btnUpdate.UseVisualStyleBackColor = true;
            btnUpdate.UseWindowsAccentColor = false;
            btnUpdate.Visible = false;
            btnUpdate.Click += BtnUpdate_Click;
            // 
            // ExtensionItemView
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(65, 65, 65);
            Controls.Add(btnUpdate);
            Controls.Add(btnUninstall);
            Controls.Add(lblStatus);
            Controls.Add(btnConfigure);
            Controls.Add(lblVersion);
            Controls.Add(lblExtensionName);
            Controls.Add(extensionIcon);
            Controls.Add(lblExtensionType);
            Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Name = "ExtensionItemView";
            Size = new Size(301, 164);
            ((ISupportInitialize)extensionIcon).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Label lblExtensionType;
        private PictureBox extensionIcon;
        private Label lblExtensionName;
        private Label lblVersion;
        private ButtonPrimary btnConfigure;
        private Label lblStatus;
        private LinkLabel btnUninstall;
        private ButtonPrimary btnUpdate;
    }
}
