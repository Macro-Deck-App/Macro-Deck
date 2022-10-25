
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
            this.lblExtensionType = new Label();
            this.extensionIcon = new PictureBox();
            this.lblExtensionName = new Label();
            this.lblVersion = new Label();
            this.btnConfigure = new ButtonPrimary();
            this.lblStatus = new Label();
            this.btnUninstall = new LinkLabel();
            this.btnUpdate = new ButtonPrimary();
            ((ISupportInitialize)(this.extensionIcon)).BeginInit();
            this.SuspendLayout();
            // 
            // lblExtensionType
            // 
            this.lblExtensionType.ForeColor = Color.White;
            this.lblExtensionType.Location = new Point(182, 5);
            this.lblExtensionType.Name = "lblExtensionType";
            this.lblExtensionType.Size = new Size(111, 21);
            this.lblExtensionType.TabIndex = 0;
            this.lblExtensionType.Text = "label1";
            this.lblExtensionType.TextAlign = ContentAlignment.MiddleCenter;
            this.lblExtensionType.UseMnemonic = false;
            // 
            // extensionIcon
            // 
            this.extensionIcon.BackgroundImageLayout = ImageLayout.Stretch;
            this.extensionIcon.Location = new Point(8, 26);
            this.extensionIcon.Name = "extensionIcon";
            this.extensionIcon.Size = new Size(50, 50);
            this.extensionIcon.TabIndex = 1;
            this.extensionIcon.TabStop = false;
            // 
            // lblExtensionName
            // 
            this.lblExtensionName.Font = new Font("Tahoma", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblExtensionName.ForeColor = Color.White;
            this.lblExtensionName.Location = new Point(64, 26);
            this.lblExtensionName.Name = "lblExtensionName";
            this.lblExtensionName.Size = new Size(229, 23);
            this.lblExtensionName.TabIndex = 2;
            this.lblExtensionName.Text = "label1";
            this.lblExtensionName.UseMnemonic = false;
            // 
            // lblVersion
            // 
            this.lblVersion.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblVersion.ForeColor = Color.White;
            this.lblVersion.Location = new Point(64, 52);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new Size(229, 27);
            this.lblVersion.TabIndex = 3;
            this.lblVersion.Text = "label1";
            this.lblVersion.TextAlign = ContentAlignment.MiddleLeft;
            this.lblVersion.UseMnemonic = false;
            // 
            // btnConfigure
            // 
            this.btnConfigure.BorderRadius = 8;
            this.btnConfigure.Cursor = Cursors.Hand;
            this.btnConfigure.FlatAppearance.BorderSize = 0;
            this.btnConfigure.FlatStyle = FlatStyle.Flat;
            this.btnConfigure.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnConfigure.ForeColor = Color.White;
            this.btnConfigure.HoverColor = Color.Empty;
            this.btnConfigure.Icon = null;
            this.btnConfigure.Location = new Point(8, 114);
            this.btnConfigure.Name = "btnConfigure";
            this.btnConfigure.Progress = 0;
            this.btnConfigure.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnConfigure.Size = new Size(139, 40);
            this.btnConfigure.TabIndex = 4;
            this.btnConfigure.Text = "Configure";
            this.btnConfigure.UseVisualStyleBackColor = true;
            this.btnConfigure.UseWindowsAccentColor = true;
            this.btnConfigure.UseMnemonic = false;
            this.btnConfigure.Click += new EventHandler(this.BtnConfigure_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.BackColor = Color.Transparent;
            this.lblStatus.Font = new Font("Tahoma", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblStatus.ForeColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(192)))), ((int)(((byte)(0)))));
            this.lblStatus.Location = new Point(8, 4);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new Size(168, 21);
            this.lblStatus.TabIndex = 6;
            this.lblStatus.Text = "Active";
            this.lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            this.lblStatus.UseMnemonic = false;
            // 
            // btnUninstall
            // 
            this.btnUninstall.ActiveLinkColor = Color.DarkGray;
            this.btnUninstall.Font = new Font("Tahoma", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
            this.btnUninstall.ForeColor = Color.Silver;
            this.btnUninstall.LinkColor = Color.Silver;
            this.btnUninstall.Location = new Point(153, 122);
            this.btnUninstall.Name = "btnUninstall";
            this.btnUninstall.Size = new Size(140, 25);
            this.btnUninstall.TabIndex = 7;
            this.btnUninstall.TabStop = true;
            this.btnUninstall.Text = "Uninstall";
            this.btnUninstall.TextAlign = ContentAlignment.MiddleCenter;
            this.btnUninstall.UseMnemonic = false;
            this.btnUninstall.VisitedLinkColor = Color.Silver;
            this.btnUninstall.LinkClicked += new LinkLabelLinkClickedEventHandler(this.BtnUninstall_LinkClicked);
            // 
            // btnUpdate
            // 
            this.btnUpdate.BorderRadius = 8;
            this.btnUpdate.Cursor = Cursors.Hand;
            this.btnUpdate.FlatAppearance.BorderSize = 0;
            this.btnUpdate.FlatStyle = FlatStyle.Flat;
            this.btnUpdate.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnUpdate.ForeColor = Color.White;
            this.btnUpdate.HoverColor = Color.Empty;
            this.btnUpdate.Icon = null;
            this.btnUpdate.Location = new Point(153, 82);
            this.btnUpdate.Name = "btnUpdate";
            this.btnUpdate.Progress = 0;
            this.btnUpdate.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnUpdate.Size = new Size(140, 27);
            this.btnUpdate.TabIndex = 8;
            this.btnUpdate.Text = "Update";
            this.btnUpdate.UseMnemonic = false;
            this.btnUpdate.UseVisualStyleBackColor = true;
            this.btnUpdate.UseWindowsAccentColor = false;
            this.btnUpdate.Visible = false;
            this.btnUpdate.Click += new EventHandler(this.BtnUpdate_Click);
            // 
            // ExtensionItemView
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.btnUpdate);
            this.Controls.Add(this.btnUninstall);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnConfigure);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.lblExtensionName);
            this.Controls.Add(this.extensionIcon);
            this.Controls.Add(this.lblExtensionType);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "ExtensionItemView";
            this.Size = new Size(301, 164);
            ((ISupportInitialize)(this.extensionIcon)).EndInit();
            this.ResumeLayout(false);

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
