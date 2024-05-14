
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class CreateIconPack
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

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
            btnOk = new ButtonPrimary();
            lblName = new Label();
            iconPackName = new RoundedTextBox();
            lblAuthor = new Label();
            author = new RoundedTextBox();
            version = new RoundedTextBox();
            lblVersion = new Label();
            SuspendLayout();
            // 
            // btnOk
            // 
            btnOk.BorderRadius = 8;
            btnOk.Cursor = Cursors.Hand;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.Font = new Font("Tahoma", 9.75F);
            btnOk.ForeColor = Color.White;
            btnOk.HoverColor = Color.Empty;
            btnOk.Icon = null;
            btnOk.Location = new Point(212, 99);
            btnOk.Name = "btnOk";
            btnOk.Progress = 0;
            btnOk.ProgressColor = Color.FromArgb(0, 103, 205);
            btnOk.Size = new Size(75, 25);
            btnOk.TabIndex = 5;
            btnOk.Text = "Ok";
            btnOk.UseMnemonic = false;
            btnOk.UseVisualStyleBackColor = false;
            btnOk.UseWindowsAccentColor = true;
            btnOk.WriteProgress = true;
            btnOk.Click += BtnOk_Click;
            // 
            // lblName
            // 
            lblName.AutoSize = true;
            lblName.ForeColor = Color.White;
            lblName.Location = new Point(12, 28);
            lblName.Name = "lblName";
            lblName.Size = new Size(45, 16);
            lblName.TabIndex = 4;
            lblName.Text = "Name:";
            lblName.UseMnemonic = false;
            // 
            // iconPackName
            // 
            iconPackName.BackColor = Color.FromArgb(65, 65, 65);
            iconPackName.Font = new Font("Tahoma", 9F);
            iconPackName.ForeColor = Color.White;
            iconPackName.Icon = null;
            iconPackName.Location = new Point(80, 26);
            iconPackName.MaxCharacters = 32767;
            iconPackName.Multiline = false;
            iconPackName.Name = "iconPackName";
            iconPackName.Padding = new Padding(8, 5, 8, 5);
            iconPackName.PasswordChar = false;
            iconPackName.PlaceHolderColor = Color.Gray;
            iconPackName.PlaceHolderText = "";
            iconPackName.ReadOnly = false;
            iconPackName.ScrollBars = ScrollBars.None;
            iconPackName.SelectionStart = 0;
            iconPackName.Size = new Size(180, 25);
            iconPackName.TabIndex = 3;
            iconPackName.TextAlignment = HorizontalAlignment.Left;
            // 
            // lblAuthor
            // 
            lblAuthor.AutoSize = true;
            lblAuthor.ForeColor = Color.White;
            lblAuthor.Location = new Point(12, 57);
            lblAuthor.Name = "lblAuthor";
            lblAuthor.Size = new Size(50, 16);
            lblAuthor.TabIndex = 7;
            lblAuthor.Text = "Author:";
            lblAuthor.UseMnemonic = false;
            // 
            // author
            // 
            author.BackColor = Color.FromArgb(65, 65, 65);
            author.Font = new Font("Tahoma", 9F);
            author.ForeColor = Color.White;
            author.Icon = null;
            author.Location = new Point(80, 55);
            author.MaxCharacters = 32767;
            author.Multiline = false;
            author.Name = "author";
            author.Padding = new Padding(8, 5, 8, 5);
            author.PasswordChar = false;
            author.PlaceHolderColor = Color.Gray;
            author.PlaceHolderText = "";
            author.ReadOnly = false;
            author.ScrollBars = ScrollBars.None;
            author.SelectionStart = 0;
            author.Size = new Size(180, 25);
            author.TabIndex = 6;
            author.TextAlignment = HorizontalAlignment.Left;
            // 
            // version
            // 
            version.BackColor = Color.FromArgb(65, 65, 65);
            version.Font = new Font("Tahoma", 9F);
            version.ForeColor = Color.White;
            version.Icon = null;
            version.Location = new Point(80, 84);
            version.MaxCharacters = 32767;
            version.Multiline = false;
            version.Name = "version";
            version.Padding = new Padding(8, 5, 8, 5);
            version.PasswordChar = false;
            version.PlaceHolderColor = Color.Gray;
            version.PlaceHolderText = "";
            version.ReadOnly = false;
            version.ScrollBars = ScrollBars.None;
            version.SelectionStart = 0;
            version.Size = new Size(89, 25);
            version.TabIndex = 8;
            version.TextAlignment = HorizontalAlignment.Left;
            // 
            // lblVersion
            // 
            lblVersion.AutoSize = true;
            lblVersion.ForeColor = Color.White;
            lblVersion.Location = new Point(12, 86);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(55, 16);
            lblVersion.TabIndex = 9;
            lblVersion.Text = "Version:";
            lblVersion.UseMnemonic = false;
            // 
            // CreateIconPack
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(299, 133);
            Controls.Add(lblVersion);
            Controls.Add(version);
            Controls.Add(lblAuthor);
            Controls.Add(author);
            Controls.Add(btnOk);
            Controls.Add(lblName);
            Controls.Add(iconPackName);
            Name = "CreateIconPack";
            Text = "Create icon pack";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ButtonPrimary btnOk;
        private Label lblName;
        private RoundedTextBox iconPackName;
        private Label lblAuthor;
        private RoundedTextBox author;
        private RoundedTextBox version;
        private Label lblVersion;
    }
}