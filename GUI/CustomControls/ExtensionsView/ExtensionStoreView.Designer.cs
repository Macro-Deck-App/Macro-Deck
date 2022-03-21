
namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    partial class ExtensionStoreView
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
            this.extensionStoreIcon = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.webView = new CefSharp.WinForms.ChromiumWebBrowser();
            ((System.ComponentModel.ISupportInitialize)(this.extensionStoreIcon)).BeginInit();
            this.SuspendLayout();
            // 
            // extensionStoreIcon
            // 
            this.extensionStoreIcon.Image = global::SuchByte.MacroDeck.Properties.Resources.Macro_Deck_2021;
            this.extensionStoreIcon.Location = new System.Drawing.Point(9, 3);
            this.extensionStoreIcon.Name = "extensionStoreIcon";
            this.extensionStoreIcon.Size = new System.Drawing.Size(41, 41);
            this.extensionStoreIcon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.extensionStoreIcon.TabIndex = 1;
            this.extensionStoreIcon.TabStop = false;
            // 
            // label1
            // 
            this.label1.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.ForeColor = System.Drawing.Color.Silver;
            this.label1.Location = new System.Drawing.Point(56, 3);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(250, 41);
            this.label1.TabIndex = 2;
            this.label1.Text = "Extension Store";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // webView
            // 
            this.webView.ActivateBrowserOnCreation = false;
            this.webView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.webView.Location = new System.Drawing.Point(0, 50);
            this.webView.Name = "webView";
            this.webView.Size = new System.Drawing.Size(1137, 490);
            this.webView.TabIndex = 3;
            this.webView.Text = "Extension Store";
            // 
            // ExtensionStoreView
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.webView);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.extensionStoreIcon);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "ExtensionStoreView";
            this.Size = new System.Drawing.Size(1137, 540);
            ((System.ComponentModel.ISupportInitialize)(this.extensionStoreIcon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.PictureBox extensionStoreIcon;
        private System.Windows.Forms.Label label1;
        private CefSharp.WinForms.ChromiumWebBrowser webView;
    }
}
