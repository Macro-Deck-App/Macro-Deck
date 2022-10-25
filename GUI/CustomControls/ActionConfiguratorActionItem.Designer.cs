
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class ActionConfiguratorActionItem
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
            this.lblActionName = new Label();
            this.SuspendLayout();
            // 
            // lblActionName
            // 
            this.lblActionName.Cursor = Cursors.Hand;
            this.lblActionName.Location = new Point(8, 5);
            this.lblActionName.Name = "lblActionName";
            this.lblActionName.Size = new Size(252, 30);
            this.lblActionName.TabIndex = 0;
            this.lblActionName.Text = "label1";
            this.lblActionName.TextAlign = ContentAlignment.MiddleLeft;
            this.lblActionName.UseMnemonic = false;
            // 
            // ActionConfiguratorActionItem
            // 
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.lblActionName);
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Margin = new Padding(6, 0, 6, 1);
            this.Name = "ActionConfiguratorActionItem";
            this.Size = new Size(268, 40);
            this.Load += new EventHandler(this.ActionConfiguratorActionItem_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private Label lblActionName;
    }
}
