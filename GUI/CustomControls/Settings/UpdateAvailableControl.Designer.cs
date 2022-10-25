
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls.Settings
{
    partial class UpdateAvailableControl
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

            Updater.Updater.OnProgressChanged -= ProgressChanged;
            Updater.Updater.OnError -= Error;
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
            this.pictureBox2 = new PictureBox();
            this.lblVersion = new Label();
            this.changelogPanel = new FlowLayoutPanel();
            this.btnInstall = new ButtonPrimary();
            this.lblSize = new Label();
            ((ISupportInitialize)(this.pictureBox2)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox2
            // 
            this.pictureBox2.BackgroundImage = Resources.Macro_Deck_2021_update;
            this.pictureBox2.BackgroundImageLayout = ImageLayout.Stretch;
            this.pictureBox2.Location = new Point(3, 3);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new Size(50, 50);
            this.pictureBox2.TabIndex = 5;
            this.pictureBox2.TabStop = false;
            // 
            // lblVersion
            // 
            this.lblVersion.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblVersion.Location = new Point(62, 3);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new Size(919, 34);
            this.lblVersion.TabIndex = 6;
            this.lblVersion.Text = "Version {0} is now available";
            this.lblVersion.TextAlign = ContentAlignment.MiddleLeft;
            this.lblVersion.UseMnemonic = false;
            // 
            // changelogPanel
            // 
            this.changelogPanel.AutoScroll = true;
            this.changelogPanel.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.changelogPanel.Location = new Point(3, 59);
            this.changelogPanel.Name = "changelogPanel";
            this.changelogPanel.Padding = new Padding(5);
            this.changelogPanel.Size = new Size(975, 300);
            this.changelogPanel.TabIndex = 12;
            // 
            // btnInstall
            // 
            this.btnInstall.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.btnInstall.BorderRadius = 8;
            this.btnInstall.Cursor = Cursors.Hand;
            this.btnInstall.FlatAppearance.BorderSize = 0;
            this.btnInstall.FlatStyle = FlatStyle.Flat;
            this.btnInstall.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnInstall.ForeColor = Color.White;
            this.btnInstall.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnInstall.Icon = null;
            this.btnInstall.Location = new Point(349, 365);
            this.btnInstall.Name = "btnInstall";
            this.btnInstall.Progress = 0;
            this.btnInstall.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnInstall.Size = new Size(283, 32);
            this.btnInstall.TabIndex = 13;
            this.btnInstall.Text = "Download and install";
            this.btnInstall.UseMnemonic = false;
            this.btnInstall.UseVisualStyleBackColor = false;
            this.btnInstall.UseWindowsAccentColor = true;
            this.btnInstall.Click += new EventHandler(this.BtnInstall_Click);
            // 
            // lblSize
            // 
            this.lblSize.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblSize.Location = new Point(62, 37);
            this.lblSize.Name = "lblSize";
            this.lblSize.Size = new Size(309, 19);
            this.lblSize.TabIndex = 14;
            this.lblSize.Text = "0,00MB";
            this.lblSize.TextAlign = ContentAlignment.MiddleLeft;
            this.lblSize.UseMnemonic = false;
            // 
            // UpdateAvailableControl
            // 
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.lblSize);
            this.Controls.Add(this.btnInstall);
            this.Controls.Add(this.changelogPanel);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.pictureBox2);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Name = "UpdateAvailableControl";
            this.Size = new Size(981, 418);
            ((ISupportInitialize)(this.pictureBox2)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private PictureBox pictureBox2;
        private Label lblVersion;
        private FlowLayoutPanel changelogPanel;
        private ButtonPrimary btnInstall;
        private Label lblSize;
    }
}
