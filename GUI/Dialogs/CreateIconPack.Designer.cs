
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
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblName = new System.Windows.Forms.Label();
            this.iconPackName = new RoundedTextBox();
            this.lblAuthor = new System.Windows.Forms.Label();
            this.author = new RoundedTextBox();
            this.version = new RoundedTextBox();
            this.lblVersion = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnOk
            // 
            this.btnOk.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.Location = new System.Drawing.Point(212, 99);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 5;
            this.btnOk.Text = "Ok";
            this.btnOk.UseMnemonic = false;
            this.btnOk.UseVisualStyleBackColor = false;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // lblName
            // 
            this.lblName.AutoSize = true;
            this.lblName.ForeColor = System.Drawing.Color.White;
            this.lblName.Location = new System.Drawing.Point(12, 28);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(45, 16);
            this.lblName.TabIndex = 4;
            this.lblName.Text = "Name:";
            this.lblName.UseMnemonic = false;
            // 
            // iconPackName
            // 
            this.iconPackName.ForeColor = System.Drawing.Color.White;
            this.iconPackName.Location = new System.Drawing.Point(80, 26);
            this.iconPackName.Name = "iconPackName";
            this.iconPackName.Size = new System.Drawing.Size(180, 23);
            this.iconPackName.TabIndex = 3;
            // 
            // lblAuthor
            // 
            this.lblAuthor.AutoSize = true;
            this.lblAuthor.ForeColor = System.Drawing.Color.White;
            this.lblAuthor.Location = new System.Drawing.Point(12, 57);
            this.lblAuthor.Name = "lblAuthor";
            this.lblAuthor.Size = new System.Drawing.Size(50, 16);
            this.lblAuthor.TabIndex = 7;
            this.lblAuthor.Text = "Author:";
            this.lblAuthor.UseMnemonic = false;
            // 
            // author
            // 
            this.author.ForeColor = System.Drawing.Color.White;
            this.author.Location = new System.Drawing.Point(80, 55);
            this.author.Name = "author";
            this.author.Size = new System.Drawing.Size(180, 23);
            this.author.TabIndex = 6;
            // 
            // version
            // 
            this.version.ForeColor = System.Drawing.Color.White;
            this.version.Location = new System.Drawing.Point(80, 84);
            this.version.Name = "version";
            this.version.Size = new System.Drawing.Size(89, 23);
            this.version.TabIndex = 8;
            // 
            // lblVersion
            // 
            this.lblVersion.AutoSize = true;
            this.lblVersion.ForeColor = System.Drawing.Color.White;
            this.lblVersion.Location = new System.Drawing.Point(12, 86);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(55, 16);
            this.lblVersion.TabIndex = 9;
            this.lblVersion.Text = "Version:";
            this.lblVersion.UseMnemonic = false;
            // 
            // CreateIconPack
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(299, 133);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.version);
            this.Controls.Add(this.lblAuthor);
            this.Controls.Add(this.author);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.lblName);
            this.Controls.Add(this.iconPackName);
            this.Name = "CreateIconPack";
            this.Text = "CreateIconPack";
            this.Controls.SetChildIndex(this.iconPackName, 0);
            this.Controls.SetChildIndex(this.lblName, 0);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.author, 0);
            this.Controls.SetChildIndex(this.lblAuthor, 0);
            this.Controls.SetChildIndex(this.version, 0);
            this.Controls.SetChildIndex(this.lblVersion, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

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