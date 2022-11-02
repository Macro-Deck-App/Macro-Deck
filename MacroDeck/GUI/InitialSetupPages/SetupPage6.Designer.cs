
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage6
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
            this.lblAlmostDone = new Label();
            this.checkAutoUpdates = new CheckBox();
            this.checkAutoStart = new CheckBox();
            this.SuspendLayout();
            // 
            // lblAlmostDone
            // 
            this.lblAlmostDone.Font = new Font("Tahoma", 24F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblAlmostDone.ForeColor = Color.White;
            this.lblAlmostDone.ImageAlign = ContentAlignment.BottomCenter;
            this.lblAlmostDone.Location = new Point(3, 0);
            this.lblAlmostDone.Name = "lblAlmostDone";
            this.lblAlmostDone.Size = new Size(685, 45);
            this.lblAlmostDone.TabIndex = 7;
            this.lblAlmostDone.Text = "We\'re almost done!";
            this.lblAlmostDone.TextAlign = ContentAlignment.TopCenter;
            this.lblAlmostDone.UseMnemonic = false;
            // 
            // checkAutoUpdates
            // 
            this.checkAutoUpdates.AutoSize = true;
            this.checkAutoUpdates.Checked = true;
            this.checkAutoUpdates.CheckState = CheckState.Checked;
            this.checkAutoUpdates.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.checkAutoUpdates.ForeColor = Color.White;
            this.checkAutoUpdates.Location = new Point(16, 100);
            this.checkAutoUpdates.Name = "checkAutoUpdates";
            this.checkAutoUpdates.Size = new Size(253, 23);
            this.checkAutoUpdates.TabIndex = 8;
            this.checkAutoUpdates.Text = "Automatically check for updates";
            this.checkAutoUpdates.UseMnemonic = false;
            this.checkAutoUpdates.UseVisualStyleBackColor = true;
            this.checkAutoUpdates.CheckedChanged += new EventHandler(this.CheckAutoUpdates_CheckedChanged);
            // 
            // checkAutoStart
            // 
            this.checkAutoStart.AutoSize = true;
            this.checkAutoStart.Checked = true;
            this.checkAutoStart.CheckState = CheckState.Checked;
            this.checkAutoStart.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.checkAutoStart.ForeColor = Color.White;
            this.checkAutoStart.Location = new Point(16, 129);
            this.checkAutoStart.Name = "checkAutoStart";
            this.checkAutoStart.Size = new Size(264, 23);
            this.checkAutoStart.TabIndex = 9;
            this.checkAutoStart.Text = "Automatically start with Windows\r\n";
            this.checkAutoStart.UseMnemonic = false;
            this.checkAutoStart.UseVisualStyleBackColor = true;
            this.checkAutoStart.CheckedChanged += new EventHandler(this.CheckAutoStart_CheckedChanged);
            // 
            // SetupPage6
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.checkAutoStart);
            this.Controls.Add(this.checkAutoUpdates);
            this.Controls.Add(this.lblAlmostDone);
            this.Name = "SetupPage6";
            this.Size = new Size(691, 571);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Label lblAlmostDone;
        private CheckBox checkAutoUpdates;
        private CheckBox checkAutoStart;
    }
}
