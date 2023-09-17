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
            lblSize = new System.Windows.Forms.Label();
            btnInstall = new CustomControls.ButtonPrimary();
            lblVersion = new System.Windows.Forms.Label();
            pictureBox2 = new System.Windows.Forms.PictureBox();
            lblInstalledVersion = new System.Windows.Forms.Label();
            lblShowChangeNotes = new System.Windows.Forms.LinkLabel();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).BeginInit();
            SuspendLayout();
            // 
            // lblSize
            // 
            lblSize.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            lblSize.Location = new System.Drawing.Point(158, 97);
            lblSize.Name = "lblSize";
            lblSize.Size = new System.Drawing.Size(309, 19);
            lblSize.TabIndex = 18;
            lblSize.Text = "0,00MB";
            lblSize.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            lblSize.UseMnemonic = false;
            // 
            // btnInstall
            // 
            btnInstall.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            btnInstall.BorderRadius = 8;
            btnInstall.Cursor = System.Windows.Forms.Cursors.Hand;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnInstall.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            btnInstall.ForeColor = System.Drawing.Color.White;
            btnInstall.HoverColor = System.Drawing.Color.FromArgb(0, 89, 184);
            btnInstall.Icon = null;
            btnInstall.Location = new System.Drawing.Point(171, 220);
            btnInstall.Name = "btnInstall";
            btnInstall.Progress = 0;
            btnInstall.ProgressColor = System.Drawing.Color.FromArgb(0, 46, 94);
            btnInstall.Size = new System.Drawing.Size(283, 32);
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
            lblVersion.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            lblVersion.Location = new System.Drawing.Point(64, 4);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new System.Drawing.Size(496, 50);
            lblVersion.TabIndex = 16;
            lblVersion.Text = "Version {0} is now available";
            lblVersion.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            lblVersion.UseMnemonic = false;
            // 
            // pictureBox2
            // 
            pictureBox2.BackgroundImage = Properties.Resources.Macro_Deck_2021_update;
            pictureBox2.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            pictureBox2.Location = new System.Drawing.Point(8, 86);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new System.Drawing.Size(100, 100);
            pictureBox2.TabIndex = 15;
            pictureBox2.TabStop = false;
            // 
            // lblInstalledVersion
            // 
            lblInstalledVersion.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            lblInstalledVersion.Location = new System.Drawing.Point(158, 116);
            lblInstalledVersion.Name = "lblInstalledVersion";
            lblInstalledVersion.Size = new System.Drawing.Size(309, 19);
            lblInstalledVersion.TabIndex = 19;
            lblInstalledVersion.Text = "Installed version: 2.0.0";
            lblInstalledVersion.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            lblInstalledVersion.UseMnemonic = false;
            // 
            // lblShowChangeNotes
            // 
            lblShowChangeNotes.AutoSize = true;
            lblShowChangeNotes.LinkColor = System.Drawing.Color.DeepSkyBlue;
            lblShowChangeNotes.Location = new System.Drawing.Point(255, 160);
            lblShowChangeNotes.Name = "lblShowChangeNotes";
            lblShowChangeNotes.Size = new System.Drawing.Size(115, 16);
            lblShowChangeNotes.TabIndex = 20;
            lblShowChangeNotes.TabStop = true;
            lblShowChangeNotes.Text = "View change notes";
            lblShowChangeNotes.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            lblShowChangeNotes.LinkClicked += LblShowChangeNotes_LinkClicked;
            // 
            // UpdateAvailableDialog
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(624, 272);
            Controls.Add(lblShowChangeNotes);
            Controls.Add(lblInstalledVersion);
            Controls.Add(lblSize);
            Controls.Add(btnInstall);
            Controls.Add(lblVersion);
            Controls.Add(pictureBox2);
            Location = new System.Drawing.Point(0, 0);
            Name = "UpdateAvailableDialog";
            Text = "UpdateAvailableDialog";
            Load += UpdateAvailableDialog_Load;
            Controls.SetChildIndex(pictureBox2, 0);
            Controls.SetChildIndex(lblVersion, 0);
            Controls.SetChildIndex(btnInstall, 0);
            Controls.SetChildIndex(lblSize, 0);
            Controls.SetChildIndex(lblInstalledVersion, 0);
            Controls.SetChildIndex(lblShowChangeNotes, 0);
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