
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
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.SuspendLayout();
            // 
            // lblVersion
            // 
            this.lblVersion.ForeColor = System.Drawing.Color.White;
            this.lblVersion.Location = new System.Drawing.Point(14, 15);
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
            this.version.Location = new System.Drawing.Point(95, 15);
            this.version.MaxCharacters = 32767;
            this.version.Multiline = false;
            this.version.Name = "version";
            this.version.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.version.PasswordChar = false;
            this.version.PlaceHolderColor = System.Drawing.Color.Gray;
            this.version.PlaceHolderText = "";
            this.version.ReadOnly = false;
            this.version.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.version.SelectionStart = 0;
            this.version.Size = new System.Drawing.Size(89, 25);
            this.version.TabIndex = 13;
            this.version.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnOk
            // 
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Icon = null;
            this.btnOk.Location = new System.Drawing.Point(141, 46);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 10;
            this.btnOk.Text = "Ok";
            this.btnOk.UseMnemonic = false;
            this.btnOk.UseVisualStyleBackColor = false;
            this.btnOk.UseWindowsAccentColor = true;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // ExportIconPackDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(220, 84);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.version);
            this.Controls.Add(this.btnOk);
            this.Location = new System.Drawing.Point(0, 0);
            this.Name = "ExportIconPackDialog";
            this.Text = "ExportIconPackDialog";
            this.Load += new System.EventHandler(this.ExportIconPackDialog_Load);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.version, 0);
            this.Controls.SetChildIndex(this.lblVersion, 0);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblVersion;
        private RoundedTextBox version;
        private CustomControls.ButtonPrimary btnOk;
    }
}