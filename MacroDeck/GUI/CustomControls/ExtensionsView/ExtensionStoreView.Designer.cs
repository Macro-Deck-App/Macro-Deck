
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using CefSharp.WinForms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    partial class ExtensionStoreView
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
            this.extensionStoreIcon = new PictureBox();
            this.label1 = new Label();
            this.webView = new ChromiumWebBrowser();
            ((ISupportInitialize)(this.extensionStoreIcon)).BeginInit();
            this.SuspendLayout();
            // 
            // extensionStoreIcon
            // 
            this.extensionStoreIcon.Image = Resources.Macro_Deck_2021;
            this.extensionStoreIcon.Location = new Point(9, 3);
            this.extensionStoreIcon.Name = "extensionStoreIcon";
            this.extensionStoreIcon.Size = new Size(41, 41);
            this.extensionStoreIcon.SizeMode = PictureBoxSizeMode.StretchImage;
            this.extensionStoreIcon.TabIndex = 1;
            this.extensionStoreIcon.TabStop = false;
            // 
            // label1
            // 
            this.label1.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.label1.ForeColor = Color.Silver;
            this.label1.Location = new Point(56, 3);
            this.label1.Name = "label1";
            this.label1.Size = new Size(250, 41);
            this.label1.TabIndex = 2;
            this.label1.Text = "Extension Store";
            this.label1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // webView
            // 
            this.webView.ActivateBrowserOnCreation = false;
            this.webView.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                    | AnchorStyles.Left) 
                                                   | AnchorStyles.Right)));
            this.webView.Location = new Point(0, 50);
            this.webView.Name = "webView";
            this.webView.Size = new Size(1137, 490);
            this.webView.TabIndex = 3;
            this.webView.Text = "Extension Store";
            // 
            // ExtensionStoreView
            // 
            this.AutoScaleMode = AutoScaleMode.None;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.webView);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.extensionStoreIcon);
            this.Font = new Font("Tahoma", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Name = "ExtensionStoreView";
            this.Size = new Size(1137, 540);
            ((ISupportInitialize)(this.extensionStoreIcon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private PictureBox extensionStoreIcon;
        private Label label1;
        private ChromiumWebBrowser webView;
    }
}
