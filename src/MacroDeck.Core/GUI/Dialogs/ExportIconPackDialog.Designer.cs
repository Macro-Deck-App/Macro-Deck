
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class ExportIconPackDialog
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
            lblVersion = new Label();
            version = new RoundedTextBox();
            btnOk = new ButtonPrimary();
            SuspendLayout();
            // 
            // lblVersion
            // 
            lblVersion.ForeColor = Color.White;
            lblVersion.Location = new Point(14, 15);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(75, 25);
            lblVersion.TabIndex = 14;
            lblVersion.Text = "Version:";
            lblVersion.TextAlign = ContentAlignment.MiddleLeft;
            lblVersion.UseMnemonic = false;
            // 
            // version
            // 
            version.BackColor = Color.FromArgb(65, 65, 65);
            version.Cursor = Cursors.Hand;
            version.Font = new Font("Tahoma", 9F);
            version.ForeColor = Color.White;
            version.Icon = null;
            version.Location = new Point(95, 15);
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
            version.TabIndex = 13;
            version.TextAlignment = HorizontalAlignment.Left;
            // 
            // btnOk
            // 
            btnOk.BorderRadius = 8;
            btnOk.Cursor = Cursors.Hand;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.Font = new Font("Tahoma", 9.75F);
            btnOk.ForeColor = Color.White;
            btnOk.HoverColor = Color.FromArgb(0, 89, 184);
            btnOk.Icon = null;
            btnOk.Location = new Point(141, 46);
            btnOk.Name = "btnOk";
            btnOk.Progress = 0;
            btnOk.ProgressColor = Color.FromArgb(0, 46, 94);
            btnOk.Size = new Size(75, 25);
            btnOk.TabIndex = 10;
            btnOk.Text = "Ok";
            btnOk.UseMnemonic = false;
            btnOk.UseVisualStyleBackColor = false;
            btnOk.UseWindowsAccentColor = true;
            btnOk.WriteProgress = true;
            btnOk.Click += BtnOk_Click;
            // 
            // ExportIconPackDialog
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(220, 84);
            Controls.Add(lblVersion);
            Controls.Add(version);
            Controls.Add(btnOk);
            Name = "ExportIconPackDialog";
            Text = "Export icon pack";
            Load += ExportIconPackDialog_Load;
            ResumeLayout(false);
        }

        #endregion

        private Label lblVersion;
        private RoundedTextBox version;
        private ButtonPrimary btnOk;
    }
}