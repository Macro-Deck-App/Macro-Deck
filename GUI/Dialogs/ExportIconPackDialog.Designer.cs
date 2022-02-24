
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class ExportIconPackDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblVersion = new System.Windows.Forms.Label();
            this.version = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.lblAuthor = new System.Windows.Forms.Label();
            this.author = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.description = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.lblDescription = new System.Windows.Forms.Label();
            this.lblLicense = new System.Windows.Forms.Label();
            this.license = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.SuspendLayout();
            // 
            // lblVersion
            // 
            this.lblVersion.ForeColor = System.Drawing.Color.White;
            this.lblVersion.Location = new System.Drawing.Point(13, 46);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(75, 25);
            this.lblVersion.TabIndex = 14;
            this.lblVersion.Text = "Version:";
            this.lblVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblVersion.UseMnemonic = false;
            // 
            // version
            // 
            this.version.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.version.Cursor = System.Windows.Forms.Cursors.Hand;
            this.version.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.version.ForeColor = System.Drawing.Color.White;
            this.version.Icon = null;
            this.version.Location = new System.Drawing.Point(94, 46);
            this.version.MaxCharacters = 32767;
            this.version.Multiline = false;
            this.version.Name = "version";
            this.version.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.version.PasswordChar = false;
            this.version.PlaceHolderColor = System.Drawing.Color.Gray;
            this.version.PlaceHolderText = "";
            this.version.ReadOnly = false;
            this.version.SelectionStart = 0;
            this.version.Size = new System.Drawing.Size(89, 25);
            this.version.TabIndex = 13;
            this.version.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // lblAuthor
            // 
            this.lblAuthor.ForeColor = System.Drawing.Color.White;
            this.lblAuthor.Location = new System.Drawing.Point(13, 14);
            this.lblAuthor.Name = "lblAuthor";
            this.lblAuthor.Size = new System.Drawing.Size(75, 25);
            this.lblAuthor.TabIndex = 12;
            this.lblAuthor.Text = "Author:";
            this.lblAuthor.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblAuthor.UseMnemonic = false;
            // 
            // author
            // 
            this.author.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.author.Cursor = System.Windows.Forms.Cursors.Hand;
            this.author.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.author.ForeColor = System.Drawing.Color.White;
            this.author.Icon = null;
            this.author.Location = new System.Drawing.Point(94, 14);
            this.author.MaxCharacters = 32767;
            this.author.Multiline = false;
            this.author.Name = "author";
            this.author.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.author.PasswordChar = false;
            this.author.PlaceHolderColor = System.Drawing.Color.Gray;
            this.author.PlaceHolderText = "";
            this.author.ReadOnly = false;
            this.author.SelectionStart = 0;
            this.author.Size = new System.Drawing.Size(173, 25);
            this.author.TabIndex = 11;
            this.author.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnOk
            // 
            this.btnOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOk.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Icon = null;
            this.btnOk.Location = new System.Drawing.Point(227, 240);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 10;
            this.btnOk.Text = "Ok";
            this.btnOk.UseMnemonic = false;
            this.btnOk.UseVisualStyleBackColor = false;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // description
            // 
            this.description.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.description.Cursor = System.Windows.Forms.Cursors.Hand;
            this.description.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.description.Icon = null;
            this.description.Location = new System.Drawing.Point(13, 101);
            this.description.MaxCharacters = 105;
            this.description.Multiline = true;
            this.description.Name = "description";
            this.description.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.description.PasswordChar = false;
            this.description.PlaceHolderColor = System.Drawing.Color.Gray;
            this.description.PlaceHolderText = "";
            this.description.ReadOnly = false;
            this.description.SelectionStart = 0;
            this.description.Size = new System.Drawing.Size(289, 70);
            this.description.TabIndex = 15;
            this.description.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // lblDescription
            // 
            this.lblDescription.AutoSize = true;
            this.lblDescription.ForeColor = System.Drawing.Color.White;
            this.lblDescription.Location = new System.Drawing.Point(13, 82);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(70, 16);
            this.lblDescription.TabIndex = 16;
            this.lblDescription.Text = "Description";
            this.lblDescription.UseMnemonic = false;
            // 
            // lblLicense
            // 
            this.lblLicense.ForeColor = System.Drawing.Color.White;
            this.lblLicense.Location = new System.Drawing.Point(13, 188);
            this.lblLicense.Name = "lblLicense";
            this.lblLicense.Size = new System.Drawing.Size(75, 25);
            this.lblLicense.TabIndex = 18;
            this.lblLicense.Text = "License";
            this.lblLicense.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblLicense.UseMnemonic = false;
            // 
            // license
            // 
            this.license.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.license.Cursor = System.Windows.Forms.Cursors.Hand;
            this.license.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point);
            this.license.ForeColor = System.Drawing.Color.White;
            this.license.Icon = null;
            this.license.Location = new System.Drawing.Point(94, 188);
            this.license.MaxCharacters = 32767;
            this.license.Multiline = false;
            this.license.Name = "license";
            this.license.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.license.PasswordChar = false;
            this.license.PlaceHolderColor = System.Drawing.Color.Gray;
            this.license.PlaceHolderText = "https://linktolicense.com";
            this.license.ReadOnly = false;
            this.license.SelectionStart = 0;
            this.license.Size = new System.Drawing.Size(208, 25);
            this.license.TabIndex = 17;
            this.license.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // ExportIconPackDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(314, 276);
            this.Controls.Add(this.lblLicense);
            this.Controls.Add(this.license);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.description);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.version);
            this.Controls.Add(this.lblAuthor);
            this.Controls.Add(this.author);
            this.Controls.Add(this.btnOk);
            this.Name = "ExportIconPackDialog";
            this.Text = "ExportIconPackDialog";
            this.Load += new System.EventHandler(this.ExportIconPackDialog_Load);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.author, 0);
            this.Controls.SetChildIndex(this.lblAuthor, 0);
            this.Controls.SetChildIndex(this.version, 0);
            this.Controls.SetChildIndex(this.lblVersion, 0);
            this.Controls.SetChildIndex(this.description, 0);
            this.Controls.SetChildIndex(this.lblDescription, 0);
            this.Controls.SetChildIndex(this.license, 0);
            this.Controls.SetChildIndex(this.lblLicense, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblVersion;
        private RoundedTextBox version;
        private System.Windows.Forms.Label lblAuthor;
        private RoundedTextBox author;
        private CustomControls.ButtonPrimary btnOk;
        private RoundedTextBox description;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.Label lblLicense;
        private RoundedTextBox license;
    }
}