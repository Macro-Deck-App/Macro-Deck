
namespace SuchByte.MacroDeck.GUI.MainWindowViews
{
    partial class ExtensionsView
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
            this.content = new System.Windows.Forms.Panel();
            this.radioInstalled = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.radioOnline = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.SuspendLayout();
            // 
            // content
            // 
            this.content.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.content.Location = new System.Drawing.Point(0, 45);
            this.content.Name = "content";
            this.content.Size = new System.Drawing.Size(1137, 495);
            this.content.TabIndex = 0;
            // 
            // radioInstalled
            // 
            this.radioInstalled.BorderRadius = 8;
            this.radioInstalled.Checked = true;
            this.radioInstalled.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioInstalled.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioInstalled.ForeColor = System.Drawing.Color.White;
            this.radioInstalled.Icon = global::SuchByte.MacroDeck.Properties.Resources.Harddisk;
            this.radioInstalled.Location = new System.Drawing.Point(3, 3);
            this.radioInstalled.Name = "radioInstalled";
            this.radioInstalled.Size = new System.Drawing.Size(186, 37);
            this.radioInstalled.TabIndex = 1;
            this.radioInstalled.TabStop = true;
            this.radioInstalled.Text = "Installed";
            this.radioInstalled.UseVisualStyleBackColor = true;
            this.radioInstalled.CheckedChanged += new System.EventHandler(this.RadioInstalled_CheckedChanged);
            // 
            // radioOnline
            // 
            this.radioOnline.BorderRadius = 8;
            this.radioOnline.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnline.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnline.ForeColor = System.Drawing.Color.White;
            this.radioOnline.Icon = global::SuchByte.MacroDeck.Properties.Resources.Web2;
            this.radioOnline.Location = new System.Drawing.Point(195, 3);
            this.radioOnline.Name = "radioOnline";
            this.radioOnline.Size = new System.Drawing.Size(186, 37);
            this.radioOnline.TabIndex = 2;
            this.radioOnline.Text = "Online";
            this.radioOnline.UseVisualStyleBackColor = true;
            this.radioOnline.CheckedChanged += new System.EventHandler(this.RadioOnline_CheckedChanged);
            // 
            // ExtensionsView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.radioOnline);
            this.Controls.Add(this.radioInstalled);
            this.Controls.Add(this.content);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "ExtensionsView";
            this.Size = new System.Drawing.Size(1137, 540);
            this.Load += new System.EventHandler(this.ExtensionStoreView_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel content;
        private CustomControls.ButtonRadioButton radioInstalled;
        private CustomControls.ButtonRadioButton radioOnline;
    }
}
