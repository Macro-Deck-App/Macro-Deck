
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class ActionConfiguratorPluginItem
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
            this.pluginIcon = new PictureBox();
            this.pluginName = new Label();
            this.lblCountActions = new Label();
            this.chevron = new PictureBox();
            ((ISupportInitialize)(this.pluginIcon)).BeginInit();
            ((ISupportInitialize)(this.chevron)).BeginInit();
            this.SuspendLayout();
            // 
            // pluginIcon
            // 
            this.pluginIcon.BackgroundImageLayout = ImageLayout.Stretch;
            this.pluginIcon.Cursor = Cursors.Hand;
            this.pluginIcon.Location = new Point(33, 5);
            this.pluginIcon.Name = "pluginIcon";
            this.pluginIcon.Size = new Size(30, 30);
            this.pluginIcon.TabIndex = 0;
            this.pluginIcon.TabStop = false;
            // 
            // pluginName
            // 
            this.pluginName.Cursor = Cursors.Hand;
            this.pluginName.Font = new Font("Tahoma", 9F, FontStyle.Bold, GraphicsUnit.Point);
            this.pluginName.Location = new Point(69, 3);
            this.pluginName.Name = "pluginName";
            this.pluginName.Size = new Size(203, 20);
            this.pluginName.TabIndex = 1;
            this.pluginName.Text = "label1";
            this.pluginName.UseMnemonic = false;
            // 
            // lblCountActions
            // 
            this.lblCountActions.Cursor = Cursors.Hand;
            this.lblCountActions.Font = new Font("Tahoma", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblCountActions.Location = new Point(69, 18);
            this.lblCountActions.Name = "lblCountActions";
            this.lblCountActions.Size = new Size(203, 20);
            this.lblCountActions.TabIndex = 2;
            this.lblCountActions.Text = "label1";
            this.lblCountActions.TextAlign = ContentAlignment.MiddleLeft;
            this.lblCountActions.UseMnemonic = false;
            // 
            // chevron
            // 
            this.chevron.BackgroundImage = Resources.Chevron_Right;
            this.chevron.BackgroundImageLayout = ImageLayout.Stretch;
            this.chevron.Cursor = Cursors.Hand;
            this.chevron.Location = new Point(8, 10);
            this.chevron.Name = "chevron";
            this.chevron.Size = new Size(20, 20);
            this.chevron.TabIndex = 3;
            this.chevron.TabStop = false;
            // 
            // ActionConfiguratorPluginItem
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.chevron);
            this.Controls.Add(this.lblCountActions);
            this.Controls.Add(this.pluginName);
            this.Controls.Add(this.pluginIcon);
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Margin = new Padding(0, 6, 0, 1);
            this.Name = "ActionConfiguratorPluginItem";
            this.Size = new Size(280, 40);
            this.Load += new EventHandler(this.ActionConfiguratorPluginItem_Load);
            ((ISupportInitialize)(this.pluginIcon)).EndInit();
            ((ISupportInitialize)(this.chevron)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private PictureBox pluginIcon;
        private Label pluginName;
        private Label lblCountActions;
        private PictureBox chevron;
    }
}
