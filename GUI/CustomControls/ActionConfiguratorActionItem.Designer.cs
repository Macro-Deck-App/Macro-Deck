
namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class ActionConfiguratorActionItem
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
            this.lblActionName = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // lblActionName
            // 
            this.lblActionName.Cursor = System.Windows.Forms.Cursors.Hand;
            this.lblActionName.Location = new System.Drawing.Point(8, 5);
            this.lblActionName.Name = "lblActionName";
            this.lblActionName.Size = new System.Drawing.Size(252, 30);
            this.lblActionName.TabIndex = 0;
            this.lblActionName.Text = "label1";
            this.lblActionName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblActionName.UseMnemonic = false;
            // 
            // ActionConfiguratorActionItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.lblActionName);
            this.Cursor = System.Windows.Forms.Cursors.Hand;
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Margin = new System.Windows.Forms.Padding(6, 0, 6, 1);
            this.Name = "ActionConfiguratorActionItem";
            this.Size = new System.Drawing.Size(268, 40);
            this.Load += new System.EventHandler(this.ActionConfiguratorActionItem_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblActionName;
    }
}
