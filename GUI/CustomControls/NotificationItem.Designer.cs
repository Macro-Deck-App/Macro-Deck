
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class NotificationItem
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
            foreach (var control in this.additionalControls.Controls)
            {
                this.additionalControls.Controls.Remove((Control)control);
            }
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
            this.lblPluginName = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblMessage = new System.Windows.Forms.Label();
            this.additionalControls = new System.Windows.Forms.FlowLayoutPanel();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.lblDateTime = new System.Windows.Forms.Label();
            this.btnRemove = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            ((System.ComponentModel.ISupportInitialize)(this.pluginIcon)).BeginInit();
            this.flowLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemove)).BeginInit();
            this.SuspendLayout();
            // 
            // pluginIcon
            // 
            this.pluginIcon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pluginIcon.Location = new System.Drawing.Point(8, 8);
            this.pluginIcon.Name = "pluginIcon";
            this.pluginIcon.Size = new System.Drawing.Size(50, 50);
            this.pluginIcon.TabIndex = 0;
            this.pluginIcon.TabStop = false;
            // 
            // lblPluginName
            // 
            this.lblPluginName.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblPluginName.ForeColor = System.Drawing.Color.Silver;
            this.lblPluginName.Location = new System.Drawing.Point(64, 5);
            this.lblPluginName.Name = "lblPluginName";
            this.lblPluginName.Size = new System.Drawing.Size(275, 13);
            this.lblPluginName.TabIndex = 1;
            this.lblPluginName.Text = "label1";
            this.lblPluginName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblTitle
            // 
            this.lblTitle.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblTitle.Location = new System.Drawing.Point(63, 23);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(399, 22);
            this.lblTitle.TabIndex = 2;
            this.lblTitle.Text = "label1";
            // 
            // lblMessage
            // 
            this.lblMessage.AutoSize = true;
            this.lblMessage.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblMessage.Location = new System.Drawing.Point(0, 0);
            this.lblMessage.Margin = new System.Windows.Forms.Padding(0);
            this.lblMessage.MaximumSize = new System.Drawing.Size(400, 0);
            this.lblMessage.MinimumSize = new System.Drawing.Size(400, 0);
            this.lblMessage.Name = "lblMessage";
            this.lblMessage.Size = new System.Drawing.Size(400, 16);
            this.lblMessage.TabIndex = 3;
            // 
            // additionalControls
            // 
            this.additionalControls.AutoSize = true;
            this.additionalControls.Location = new System.Drawing.Point(0, 26);
            this.additionalControls.Margin = new System.Windows.Forms.Padding(0, 10, 0, 0);
            this.additionalControls.MaximumSize = new System.Drawing.Size(400, 0);
            this.additionalControls.MinimumSize = new System.Drawing.Size(400, 0);
            this.additionalControls.Name = "additionalControls";
            this.additionalControls.Size = new System.Drawing.Size(400, 0);
            this.additionalControls.TabIndex = 4;
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.AutoSize = true;
            this.flowLayoutPanel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanel1.Controls.Add(this.lblMessage);
            this.flowLayoutPanel1.Controls.Add(this.additionalControls);
            this.flowLayoutPanel1.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(64, 48);
            this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(400, 26);
            this.flowLayoutPanel1.TabIndex = 5;
            // 
            // lblDateTime
            // 
            this.lblDateTime.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDateTime.ForeColor = System.Drawing.Color.Silver;
            this.lblDateTime.Location = new System.Drawing.Point(320, 5);
            this.lblDateTime.Name = "lblDateTime";
            this.lblDateTime.Size = new System.Drawing.Size(188, 13);
            this.lblDateTime.TabIndex = 6;
            this.lblDateTime.Text = "00.00.0000 - 00:00:00";
            this.lblDateTime.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // btnRemove
            // 
            this.btnRemove.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRemove.BackColor = System.Drawing.Color.Transparent;
            this.btnRemove.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnRemove.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRemove.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnRemove.Image = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnRemove.Location = new System.Drawing.Point(483, 24);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new System.Drawing.Size(30, 30);
            this.btnRemove.TabIndex = 7;
            this.btnRemove.TabStop = false;
            this.btnRemove.Click += new System.EventHandler(this.BtnRemove_Click);
            // 
            // NotificationItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.btnRemove);
            this.Controls.Add(this.lblDateTime);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblPluginName);
            this.Controls.Add(this.pluginIcon);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.MinimumSize = new System.Drawing.Size(520, 0);
            this.Name = "NotificationItem";
            this.Size = new System.Drawing.Size(525, 79);
            this.Load += new System.EventHandler(this.NotificationItem_Load);
            ((System.ComponentModel.ISupportInitialize)(this.pluginIcon)).EndInit();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemove)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox pluginIcon;
        private System.Windows.Forms.Label lblPluginName;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblMessage;
        private System.Windows.Forms.FlowLayoutPanel additionalControls;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private System.Windows.Forms.Label lblDateTime;
        private PictureButton btnRemove;
    }
}
