
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage4
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
            this.lblPickAllPlugins = new Label();
            this.lblDontWorry = new Label();
            this.plugins = new FlowLayoutPanel();
            this.progressBar = new ProgressBar();
            this.SuspendLayout();
            // 
            // lblPickAllPlugins
            // 
            this.lblPickAllPlugins.Font = new Font("Tahoma", 24F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblPickAllPlugins.ForeColor = Color.White;
            this.lblPickAllPlugins.ImageAlign = ContentAlignment.BottomCenter;
            this.lblPickAllPlugins.Location = new Point(3, 0);
            this.lblPickAllPlugins.Name = "lblPickAllPlugins";
            this.lblPickAllPlugins.Size = new Size(685, 48);
            this.lblPickAllPlugins.TabIndex = 5;
            this.lblPickAllPlugins.Text = "Pick all the plugins you need";
            this.lblPickAllPlugins.TextAlign = ContentAlignment.TopCenter;
            this.lblPickAllPlugins.UseMnemonic = false;
            // 
            // lblDontWorry
            // 
            this.lblDontWorry.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblDontWorry.ForeColor = Color.White;
            this.lblDontWorry.ImageAlign = ContentAlignment.BottomCenter;
            this.lblDontWorry.Location = new Point(3, 48);
            this.lblDontWorry.Name = "lblDontWorry";
            this.lblDontWorry.Size = new Size(685, 48);
            this.lblDontWorry.TabIndex = 6;
            this.lblDontWorry.Text = "Don\'t worry, you can always install/uninstall plugins later in the package manage" +
    "r";
            this.lblDontWorry.TextAlign = ContentAlignment.TopCenter;
            this.lblDontWorry.UseMnemonic = false;
            // 
            // plugins
            // 
            this.plugins.AutoScroll = true;
            this.plugins.Location = new Point(120, 99);
            this.plugins.Name = "plugins";
            this.plugins.Size = new Size(450, 442);
            this.plugins.TabIndex = 7;
            // 
            // progressBar
            // 
            this.progressBar.Location = new Point(208, 547);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new Size(275, 18);
            this.progressBar.Style = ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 8;
            this.progressBar.Visible = false;
            // 
            // SetupPage4
            // 
           
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.plugins);
            this.Controls.Add(this.lblDontWorry);
            this.Controls.Add(this.lblPickAllPlugins);
            this.Name = "SetupPage4";
            this.Size = new Size(691, 609);
            this.Load += new EventHandler(this.SetupPage4_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private Label lblPickAllPlugins;
        private Label lblDontWorry;
        private FlowLayoutPanel plugins;
        private ProgressBar progressBar;
    }
}
