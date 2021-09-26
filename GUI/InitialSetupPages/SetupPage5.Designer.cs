
namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage5
    {
        /// <summary> 
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

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
            this.lblWantIcons = new System.Windows.Forms.Label();
            this.lblInstallLaterPackageManager = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.iconPacks = new System.Windows.Forms.FlowLayoutPanel();
            this.SuspendLayout();
            // 
            // lblWantIcons
            // 
            this.lblWantIcons.Font = new System.Drawing.Font("Tahoma", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblWantIcons.ForeColor = System.Drawing.Color.White;
            this.lblWantIcons.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.lblWantIcons.Location = new System.Drawing.Point(3, 0);
            this.lblWantIcons.Name = "lblWantIcons";
            this.lblWantIcons.Size = new System.Drawing.Size(685, 45);
            this.lblWantIcons.TabIndex = 6;
            this.lblWantIcons.Text = "Do you want some icons?";
            this.lblWantIcons.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // lblInstallLaterPackageManager
            // 
            this.lblInstallLaterPackageManager.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblInstallLaterPackageManager.ForeColor = System.Drawing.Color.White;
            this.lblInstallLaterPackageManager.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.lblInstallLaterPackageManager.Location = new System.Drawing.Point(3, 45);
            this.lblInstallLaterPackageManager.Name = "lblInstallLaterPackageManager";
            this.lblInstallLaterPackageManager.Size = new System.Drawing.Size(685, 45);
            this.lblInstallLaterPackageManager.TabIndex = 7;
            this.lblInstallLaterPackageManager.Text = "You can also install icon packs later in the package manager";
            this.lblInstallLaterPackageManager.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(3, 513);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(275, 17);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 10;
            this.progressBar.Visible = false;
            // 
            // iconPacks
            // 
            this.iconPacks.Location = new System.Drawing.Point(2, 93);
            this.iconPacks.Name = "iconPacks";
            this.iconPacks.Size = new System.Drawing.Size(688, 414);
            this.iconPacks.TabIndex = 9;
            // 
            // SetupPage5
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.iconPacks);
            this.Controls.Add(this.lblInstallLaterPackageManager);
            this.Controls.Add(this.lblWantIcons);
            this.Name = "SetupPage5";
            this.Size = new System.Drawing.Size(691, 571);
            this.Load += new System.EventHandler(this.SetupPage5_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblWantIcons;
        private System.Windows.Forms.Label lblInstallLaterPackageManager;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.FlowLayoutPanel iconPacks;
    }
}
