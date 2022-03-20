
namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class ActionConfiguratorPluginItem
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
            this.pluginIcon = new System.Windows.Forms.PictureBox();
            this.pluginName = new System.Windows.Forms.Label();
            this.lblCountActions = new System.Windows.Forms.Label();
            this.chevron = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.pluginIcon)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.chevron)).BeginInit();
            this.SuspendLayout();
            // 
            // pluginIcon
            // 
            this.pluginIcon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pluginIcon.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pluginIcon.Location = new System.Drawing.Point(33, 5);
            this.pluginIcon.Name = "pluginIcon";
            this.pluginIcon.Size = new System.Drawing.Size(30, 30);
            this.pluginIcon.TabIndex = 0;
            this.pluginIcon.TabStop = false;
            // 
            // pluginName
            // 
            this.pluginName.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pluginName.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.pluginName.Location = new System.Drawing.Point(69, 3);
            this.pluginName.Name = "pluginName";
            this.pluginName.Size = new System.Drawing.Size(203, 20);
            this.pluginName.TabIndex = 1;
            this.pluginName.Text = "label1";
            this.pluginName.UseMnemonic = false;
            // 
            // lblCountActions
            // 
            this.lblCountActions.Cursor = System.Windows.Forms.Cursors.Hand;
            this.lblCountActions.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblCountActions.Location = new System.Drawing.Point(69, 18);
            this.lblCountActions.Name = "lblCountActions";
            this.lblCountActions.Size = new System.Drawing.Size(203, 20);
            this.lblCountActions.TabIndex = 2;
            this.lblCountActions.Text = "label1";
            this.lblCountActions.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblCountActions.UseMnemonic = false;
            // 
            // chevron
            // 
            this.chevron.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Chevron_Right;
            this.chevron.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.chevron.Cursor = System.Windows.Forms.Cursors.Hand;
            this.chevron.Location = new System.Drawing.Point(8, 10);
            this.chevron.Name = "chevron";
            this.chevron.Size = new System.Drawing.Size(20, 20);
            this.chevron.TabIndex = 3;
            this.chevron.TabStop = false;
            // 
            // ActionConfiguratorPluginItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.chevron);
            this.Controls.Add(this.lblCountActions);
            this.Controls.Add(this.pluginName);
            this.Controls.Add(this.pluginIcon);
            this.Cursor = System.Windows.Forms.Cursors.Hand;
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Margin = new System.Windows.Forms.Padding(0, 6, 0, 1);
            this.Name = "ActionConfiguratorPluginItem";
            this.Size = new System.Drawing.Size(280, 40);
            this.Load += new System.EventHandler(this.ActionConfiguratorPluginItem_Load);
            ((System.ComponentModel.ISupportInitialize)(this.pluginIcon)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.chevron)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.PictureBox pluginIcon;
        private System.Windows.Forms.Label pluginName;
        private System.Windows.Forms.Label lblCountActions;
        private System.Windows.Forms.PictureBox chevron;
    }
}
