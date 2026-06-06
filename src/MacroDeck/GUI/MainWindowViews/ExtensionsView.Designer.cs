
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.MainWindowViews
{
    partial class ExtensionsView
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
            this.content = new Panel();
            this.radioInstalled = new ButtonRadioButton();
            this.radioOnline = new ButtonRadioButton();
            this.SuspendLayout();
            // 
            // content
            // 
            this.content.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                    | AnchorStyles.Left) 
                                                   | AnchorStyles.Right)));
            this.content.Location = new Point(0, 45);
            this.content.Name = "content";
            this.content.Size = new Size(1137, 495);
            this.content.TabIndex = 0;
            // 
            // radioInstalled
            // 
            this.radioInstalled.BorderRadius = 8;
            this.radioInstalled.Checked = true;
            this.radioInstalled.Cursor = Cursors.Hand;
            this.radioInstalled.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioInstalled.ForeColor = Color.White;
            this.radioInstalled.Icon = Resources.Harddisk;
            this.radioInstalled.Location = new Point(3, 3);
            this.radioInstalled.Name = "radioInstalled";
            this.radioInstalled.Size = new Size(186, 37);
            this.radioInstalled.TabIndex = 1;
            this.radioInstalled.TabStop = true;
            this.radioInstalled.Text = "Installed";
            this.radioInstalled.UseVisualStyleBackColor = true;
            this.radioInstalled.CheckedChanged += new EventHandler(this.RadioInstalled_CheckedChanged);
            // 
            // radioOnline
            // 
            this.radioOnline.BorderRadius = 8;
            this.radioOnline.Cursor = Cursors.Hand;
            this.radioOnline.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioOnline.ForeColor = Color.White;
            this.radioOnline.Icon = Resources.Web2;
            this.radioOnline.Location = new Point(195, 3);
            this.radioOnline.Name = "radioOnline";
            this.radioOnline.Size = new Size(186, 37);
            this.radioOnline.TabIndex = 2;
            this.radioOnline.Text = "Online";
            this.radioOnline.UseVisualStyleBackColor = true;
            this.radioOnline.CheckedChanged += new EventHandler(this.RadioOnline_CheckedChanged);
            // 
            // ExtensionsView
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.radioOnline);
            this.Controls.Add(this.radioInstalled);
            this.Controls.Add(this.content);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "ExtensionsView";
            this.Size = new Size(1137, 540);
            this.Load += new EventHandler(this.ExtensionStoreView_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private Panel content;
        private ButtonRadioButton radioInstalled;
        private ButtonRadioButton radioOnline;
    }
}
