
namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage6
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
            this.lblAlmostDone = new System.Windows.Forms.Label();
            this.checkAutoUpdates = new System.Windows.Forms.CheckBox();
            this.checkAutoStart = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // lblAlmostDone
            // 
            this.lblAlmostDone.Font = new System.Drawing.Font("Tahoma", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblAlmostDone.ForeColor = System.Drawing.Color.White;
            this.lblAlmostDone.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.lblAlmostDone.Location = new System.Drawing.Point(3, 0);
            this.lblAlmostDone.Name = "lblAlmostDone";
            this.lblAlmostDone.Size = new System.Drawing.Size(685, 45);
            this.lblAlmostDone.TabIndex = 7;
            this.lblAlmostDone.Text = "We\'re almost done!";
            this.lblAlmostDone.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.lblAlmostDone.UseMnemonic = false;
            // 
            // checkAutoUpdates
            // 
            this.checkAutoUpdates.AutoSize = true;
            this.checkAutoUpdates.Checked = true;
            this.checkAutoUpdates.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkAutoUpdates.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkAutoUpdates.ForeColor = System.Drawing.Color.White;
            this.checkAutoUpdates.Location = new System.Drawing.Point(16, 100);
            this.checkAutoUpdates.Name = "checkAutoUpdates";
            this.checkAutoUpdates.Size = new System.Drawing.Size(253, 23);
            this.checkAutoUpdates.TabIndex = 8;
            this.checkAutoUpdates.Text = "Automatically check for updates";
            this.checkAutoUpdates.UseMnemonic = false;
            this.checkAutoUpdates.UseVisualStyleBackColor = true;
            this.checkAutoUpdates.CheckedChanged += new System.EventHandler(this.CheckAutoUpdates_CheckedChanged);
            // 
            // checkAutoStart
            // 
            this.checkAutoStart.AutoSize = true;
            this.checkAutoStart.Checked = true;
            this.checkAutoStart.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkAutoStart.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkAutoStart.ForeColor = System.Drawing.Color.White;
            this.checkAutoStart.Location = new System.Drawing.Point(16, 129);
            this.checkAutoStart.Name = "checkAutoStart";
            this.checkAutoStart.Size = new System.Drawing.Size(264, 23);
            this.checkAutoStart.TabIndex = 9;
            this.checkAutoStart.Text = "Automatically start with Windows\r\n";
            this.checkAutoStart.UseMnemonic = false;
            this.checkAutoStart.UseVisualStyleBackColor = true;
            this.checkAutoStart.CheckedChanged += new System.EventHandler(this.CheckAutoStart_CheckedChanged);
            // 
            // SetupPage6
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.checkAutoStart);
            this.Controls.Add(this.checkAutoUpdates);
            this.Controls.Add(this.lblAlmostDone);
            this.Name = "SetupPage6";
            this.Size = new System.Drawing.Size(691, 571);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblAlmostDone;
        private System.Windows.Forms.CheckBox checkAutoUpdates;
        private System.Windows.Forms.CheckBox checkAutoStart;
    }
}
