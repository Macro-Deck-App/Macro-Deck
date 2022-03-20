
namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class InitialSetupPluginItem
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
            this.lblName = new System.Windows.Forms.Label();
            this.icon = new System.Windows.Forms.PictureBox();
            this.lblDescription = new System.Windows.Forms.Label();
            this.checkInstall = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.icon)).BeginInit();
            this.SuspendLayout();
            // 
            // lblName
            // 
            this.lblName.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblName.Location = new System.Drawing.Point(81, 3);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(325, 18);
            this.lblName.TabIndex = 0;
            this.lblName.Text = "label1";
            this.lblName.UseMnemonic = false;
            // 
            // icon
            // 
            this.icon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.icon.Location = new System.Drawing.Point(34, 8);
            this.icon.Name = "icon";
            this.icon.Size = new System.Drawing.Size(35, 35);
            this.icon.TabIndex = 3;
            this.icon.TabStop = false;
            // 
            // lblDescription
            // 
            this.lblDescription.Location = new System.Drawing.Point(81, 21);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(325, 29);
            this.lblDescription.TabIndex = 4;
            this.lblDescription.Text = "label1";
            this.lblDescription.UseMnemonic = false;
            // 
            // checkInstall
            // 
            this.checkInstall.CheckAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.checkInstall.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkInstall.Location = new System.Drawing.Point(3, 4);
            this.checkInstall.Name = "checkInstall";
            this.checkInstall.Size = new System.Drawing.Size(25, 42);
            this.checkInstall.TabIndex = 5;
            this.checkInstall.UseMnemonic = false;
            this.checkInstall.UseVisualStyleBackColor = true;
            // 
            // InitialSetupPluginItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.checkInstall);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.icon);
            this.Controls.Add(this.lblName);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "InitialSetupPluginItem";
            this.Size = new System.Drawing.Size(411, 52);
            ((System.ComponentModel.ISupportInitialize)(this.icon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.PictureBox icon;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.CheckBox checkInstall;
    }
}
