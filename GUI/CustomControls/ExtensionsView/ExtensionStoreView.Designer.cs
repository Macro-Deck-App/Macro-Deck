
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
            this.webView = new Microsoft.Web.WebView2.WinForms.WebView2();
            this.btnClose = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.lblInstalledExtensions = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.webView)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClose)).BeginInit();
            this.SuspendLayout();
            // 
            // webView
            // 
            this.webView.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.webView.CreationProperties = null;
            this.webView.DefaultBackgroundColor = System.Drawing.Color.White;
            this.webView.Location = new System.Drawing.Point(0, 36);
            this.webView.Name = "webView";
            this.webView.Size = new System.Drawing.Size(1137, 504);
            this.webView.TabIndex = 0;
            this.webView.ZoomFactor = 1D;
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.BackColor = System.Drawing.Color.Transparent;
            this.btnClose.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Close_Normal;
            this.btnClose.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnClose.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClose.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.btnClose.ForeColor = System.Drawing.Color.White;
            this.btnClose.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Close_Hover;
            this.btnClose.Location = new System.Drawing.Point(3, 4);
            this.btnClose.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(25, 25);
            this.btnClose.TabIndex = 2;
            this.btnClose.TabStop = false;
            this.btnClose.Click += new System.EventHandler(this.BtnClose_Click);
            // 
            // lblInstalledExtensions
            // 
            this.lblInstalledExtensions.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblInstalledExtensions.ForeColor = System.Drawing.Color.Silver;
            this.lblInstalledExtensions.Location = new System.Drawing.Point(356, 0);
            this.lblInstalledExtensions.Name = "lblInstalledExtensions";
            this.lblInstalledExtensions.Size = new System.Drawing.Size(425, 33);
            this.lblInstalledExtensions.TabIndex = 3;
            this.lblInstalledExtensions.Text = "Extension Store";
            this.lblInstalledExtensions.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ExtensionStoreView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.lblInstalledExtensions);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.webView);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "ExtensionStoreView";
            this.Size = new System.Drawing.Size(1137, 540);
            ((System.ComponentModel.ISupportInitialize)(this.webView)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClose)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Microsoft.Web.WebView2.WinForms.WebView2 webView;
        private PictureButton btnClose;
        private System.Windows.Forms.Label lblInstalledExtensions;
    }
}
