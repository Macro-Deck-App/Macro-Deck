
namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage4
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
            this.lblPickAllPlugins = new System.Windows.Forms.Label();
            this.lblDontWorry = new System.Windows.Forms.Label();
            this.plugins = new System.Windows.Forms.FlowLayoutPanel();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.SuspendLayout();
            // 
            // lblPickAllPlugins
            // 
            this.lblPickAllPlugins.Font = new System.Drawing.Font("Tahoma", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPickAllPlugins.ForeColor = System.Drawing.Color.White;
            this.lblPickAllPlugins.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.lblPickAllPlugins.Location = new System.Drawing.Point(3, 0);
            this.lblPickAllPlugins.Name = "lblPickAllPlugins";
            this.lblPickAllPlugins.Size = new System.Drawing.Size(685, 45);
            this.lblPickAllPlugins.TabIndex = 5;
            this.lblPickAllPlugins.Text = "Pick all the plugins you need";
            this.lblPickAllPlugins.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // lblDontWorry
            // 
            this.lblDontWorry.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDontWorry.ForeColor = System.Drawing.Color.White;
            this.lblDontWorry.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.lblDontWorry.Location = new System.Drawing.Point(3, 45);
            this.lblDontWorry.Name = "lblDontWorry";
            this.lblDontWorry.Size = new System.Drawing.Size(685, 45);
            this.lblDontWorry.TabIndex = 6;
            this.lblDontWorry.Text = "Don\'t worry, you can always install/uninstall plugins later in the package manage" +
    "r";
            this.lblDontWorry.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // plugins
            // 
            this.plugins.Location = new System.Drawing.Point(2, 93);
            this.plugins.Name = "plugins";
            this.plugins.Size = new System.Drawing.Size(688, 414);
            this.plugins.TabIndex = 7;
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(3, 513);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(275, 17);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 8;
            this.progressBar.Visible = false;
            // 
            // SetupPage4
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.plugins);
            this.Controls.Add(this.lblDontWorry);
            this.Controls.Add(this.lblPickAllPlugins);
            this.Name = "SetupPage4";
            this.Size = new System.Drawing.Size(691, 571);
            this.Load += new System.EventHandler(this.SetupPage4_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblPickAllPlugins;
        private System.Windows.Forms.Label lblDontWorry;
        private System.Windows.Forms.FlowLayoutPanel plugins;
        private System.Windows.Forms.ProgressBar progressBar;
    }
}
