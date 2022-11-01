
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class LicenseItem
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
            this.lblName = new Label();
            this.lblAuthor = new Label();
            this.lblLicense = new LinkLabel();
            this.lblProjectPage = new LinkLabel();
            this.SuspendLayout();
            // 
            // lblName
            // 
            this.lblName.Font = new Font("Tahoma", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblName.Location = new Point(3, 4);
            this.lblName.Name = "lblName";
            this.lblName.Size = new Size(338, 22);
            this.lblName.TabIndex = 0;
            this.lblName.Text = "Name";
            this.lblName.UseMnemonic = false;
            // 
            // lblAuthor
            // 
            this.lblAuthor.Location = new Point(3, 26);
            this.lblAuthor.Name = "lblAuthor";
            this.lblAuthor.Size = new Size(210, 15);
            this.lblAuthor.TabIndex = 1;
            this.lblAuthor.Text = "Author";
            this.lblAuthor.UseMnemonic = false;
            // 
            // lblLicense
            // 
            this.lblLicense.ActiveLinkColor = Color.FromArgb(((int)(((byte)(155)))), ((int)(((byte)(155)))), ((int)(((byte)(155)))));
            this.lblLicense.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblLicense.LinkColor = Color.FromArgb(((int)(((byte)(155)))), ((int)(((byte)(155)))), ((int)(((byte)(155)))));
            this.lblLicense.Location = new Point(347, 4);
            this.lblLicense.Name = "lblLicense";
            this.lblLicense.Size = new Size(149, 19);
            this.lblLicense.TabIndex = 2;
            this.lblLicense.TabStop = true;
            this.lblLicense.Text = "View license";
            this.lblLicense.TextAlign = ContentAlignment.MiddleRight;
            this.lblLicense.UseMnemonic = false;
            this.lblLicense.VisitedLinkColor = Color.FromArgb(((int)(((byte)(155)))), ((int)(((byte)(155)))), ((int)(((byte)(155)))));
            this.lblLicense.LinkClicked += new LinkLabelLinkClickedEventHandler(this.LblLicense_LinkClicked);
            // 
            // lblProjectPage
            // 
            this.lblProjectPage.ActiveLinkColor = Color.FromArgb(((int)(((byte)(155)))), ((int)(((byte)(155)))), ((int)(((byte)(155)))));
            this.lblProjectPage.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblProjectPage.LinkColor = Color.FromArgb(((int)(((byte)(155)))), ((int)(((byte)(155)))), ((int)(((byte)(155)))));
            this.lblProjectPage.Location = new Point(347, 23);
            this.lblProjectPage.Name = "lblProjectPage";
            this.lblProjectPage.Size = new Size(149, 17);
            this.lblProjectPage.TabIndex = 3;
            this.lblProjectPage.TabStop = true;
            this.lblProjectPage.Text = "Project page";
            this.lblProjectPage.TextAlign = ContentAlignment.MiddleRight;
            this.lblProjectPage.UseMnemonic = false;
            this.lblProjectPage.VisitedLinkColor = Color.FromArgb(((int)(((byte)(155)))), ((int)(((byte)(155)))), ((int)(((byte)(155)))));
            this.lblProjectPage.LinkClicked += new LinkLabelLinkClickedEventHandler(this.lblProjectPage_LinkClicked);
            // 
            // LicenseItem
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.lblProjectPage);
            this.Controls.Add(this.lblLicense);
            this.Controls.Add(this.lblAuthor);
            this.Controls.Add(this.lblName);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Name = "LicenseItem";
            this.Size = new Size(499, 44);
            this.Load += new EventHandler(this.LicenseItem_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private Label lblName;
        private Label lblAuthor;
        private LinkLabel lblLicense;
        private LinkLabel lblProjectPage;
    }
}
