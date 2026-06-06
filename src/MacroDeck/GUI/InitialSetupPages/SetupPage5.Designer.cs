
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage5
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
            this.lblWantIcons = new Label();
            this.lblInstallLaterPackageManager = new Label();
            this.progressBar = new ProgressBar();
            this.iconPacks = new FlowLayoutPanel();
            this.SuspendLayout();
            // 
            // lblWantIcons
            // 
            this.lblWantIcons.Font = new Font("Tahoma", 24F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblWantIcons.ForeColor = Color.White;
            this.lblWantIcons.ImageAlign = ContentAlignment.BottomCenter;
            this.lblWantIcons.Location = new Point(3, 0);
            this.lblWantIcons.Name = "lblWantIcons";
            this.lblWantIcons.Size = new Size(685, 48);
            this.lblWantIcons.TabIndex = 6;
            this.lblWantIcons.Text = "Do you want some icons?";
            this.lblWantIcons.TextAlign = ContentAlignment.TopCenter;
            this.lblWantIcons.UseMnemonic = false;
            // 
            // lblInstallLaterPackageManager
            // 
            this.lblInstallLaterPackageManager.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblInstallLaterPackageManager.ForeColor = Color.White;
            this.lblInstallLaterPackageManager.ImageAlign = ContentAlignment.BottomCenter;
            this.lblInstallLaterPackageManager.Location = new Point(3, 48);
            this.lblInstallLaterPackageManager.Name = "lblInstallLaterPackageManager";
            this.lblInstallLaterPackageManager.Size = new Size(685, 48);
            this.lblInstallLaterPackageManager.TabIndex = 7;
            this.lblInstallLaterPackageManager.Text = "You can also install icon packs later in the package manager";
            this.lblInstallLaterPackageManager.TextAlign = ContentAlignment.TopCenter;
            this.lblInstallLaterPackageManager.UseMnemonic = false;
            // 
            // progressBar
            // 
            this.progressBar.Location = new Point(208, 547);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new Size(275, 18);
            this.progressBar.Style = ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 10;
            this.progressBar.Visible = false;
            // 
            // iconPacks
            // 
            this.iconPacks.AutoScroll = true;
            this.iconPacks.Location = new Point(120, 99);
            this.iconPacks.Name = "iconPacks";
            this.iconPacks.Size = new Size(450, 442);
            this.iconPacks.TabIndex = 9;
            // 
            // SetupPage5
            // 
           
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.iconPacks);
            this.Controls.Add(this.lblInstallLaterPackageManager);
            this.Controls.Add(this.lblWantIcons);
            this.Name = "SetupPage5";
            this.Size = new Size(691, 609);
            this.Load += new EventHandler(this.SetupPage5_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private Label lblWantIcons;
        private Label lblInstallLaterPackageManager;
        private ProgressBar progressBar;
        private FlowLayoutPanel iconPacks;
    }
}
