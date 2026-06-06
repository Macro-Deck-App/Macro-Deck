namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class UpdateAvailableDialog
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
            lblSize = new Label();
            btnInstall = new CustomControls.ButtonPrimary();
            lblVersion = new Label();
            pictureBox2 = new PictureBox();
            lblInstalledVersion = new Label();
            lblShowChangeNotes = new LinkLabel();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).BeginInit();
            SuspendLayout();
            // 
            // lblSize
            // 
            lblSize.Font = new Font("Tahoma", 12F);
            lblSize.Location = new Point(158, 97);
            lblSize.Name = "lblSize";
            lblSize.Size = new Size(309, 19);
            lblSize.TabIndex = 18;
            lblSize.Text = "0,00MB";
            lblSize.TextAlign = ContentAlignment.MiddleCenter;
            lblSize.UseMnemonic = false;
            // 
            // btnInstall
            // 
            btnInstall.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnInstall.BorderRadius = 8;
            btnInstall.Cursor = Cursors.Hand;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.Font = new Font("Tahoma", 9.75F);
            btnInstall.ForeColor = Color.White;
            btnInstall.HoverColor = Color.FromArgb(0, 89, 184);
            btnInstall.Icon = null;
            btnInstall.Location = new Point(171, 220);
            btnInstall.Name = "btnInstall";
            btnInstall.Progress = 0;
            btnInstall.ProgressColor = Color.FromArgb(0, 46, 94);
            btnInstall.Size = new Size(283, 32);
            btnInstall.TabIndex = 17;
            btnInstall.Text = "Download and install";
            btnInstall.UseMnemonic = false;
            btnInstall.UseVisualStyleBackColor = false;
            btnInstall.UseWindowsAccentColor = true;
            btnInstall.WriteProgress = false;
            btnInstall.Click += BtnInstall_Click;
            // 
            // lblVersion
            // 
            lblVersion.Font = new Font("Tahoma", 15.75F);
            lblVersion.Location = new Point(64, 4);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(496, 50);
            lblVersion.TabIndex = 16;
            lblVersion.Text = "Version {0} is now available";
            lblVersion.TextAlign = ContentAlignment.MiddleCenter;
            lblVersion.UseMnemonic = false;
            // 
            // pictureBox2
            // 
            pictureBox2.BackgroundImage = Properties.Resources.Macro_Deck_2021_update;
            pictureBox2.BackgroundImageLayout = ImageLayout.Stretch;
            pictureBox2.Location = new Point(8, 86);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new Size(100, 100);
            pictureBox2.TabIndex = 15;
            pictureBox2.TabStop = false;
            // 
            // lblInstalledVersion
            // 
            lblInstalledVersion.Font = new Font("Tahoma", 12F);
            lblInstalledVersion.Location = new Point(158, 116);
            lblInstalledVersion.Name = "lblInstalledVersion";
            lblInstalledVersion.Size = new Size(309, 19);
            lblInstalledVersion.TabIndex = 19;
            lblInstalledVersion.Text = "Installed version: 2.0.0";
            lblInstalledVersion.TextAlign = ContentAlignment.MiddleCenter;
            lblInstalledVersion.UseMnemonic = false;
            // 
            // lblShowChangeNotes
            // 
            lblShowChangeNotes.AutoSize = true;
            lblShowChangeNotes.LinkColor = Color.DeepSkyBlue;
            lblShowChangeNotes.Location = new Point(255, 160);
            lblShowChangeNotes.Name = "lblShowChangeNotes";
            lblShowChangeNotes.Size = new Size(115, 16);
            lblShowChangeNotes.TabIndex = 20;
            lblShowChangeNotes.TabStop = true;
            lblShowChangeNotes.Text = "View change notes";
            lblShowChangeNotes.TextAlign = ContentAlignment.MiddleCenter;
            lblShowChangeNotes.LinkClicked += LblShowChangeNotes_LinkClicked;
            // 
            // UpdateAvailableDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 16F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(624, 272);
            Controls.Add(lblShowChangeNotes);
            Controls.Add(lblInstalledVersion);
            Controls.Add(lblSize);
            Controls.Add(btnInstall);
            Controls.Add(lblVersion);
            Controls.Add(pictureBox2);
            Name = "UpdateAvailableDialog";
            Text = "Update available";
            Load += UpdateAvailableDialog_Load;
            ((System.ComponentModel.ISupportInitialize)pictureBox2).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblSize;
        private CustomControls.ButtonPrimary btnInstall;
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.PictureBox pictureBox2;
        private System.Windows.Forms.Label lblInstalledVersion;
        private System.Windows.Forms.LinkLabel lblShowChangeNotes;
    }
}