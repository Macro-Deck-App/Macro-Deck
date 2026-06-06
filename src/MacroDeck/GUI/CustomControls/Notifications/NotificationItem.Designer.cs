
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class NotificationItem
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
            this.lblPluginName = new Label();
            this.lblTitle = new Label();
            this.lblMessage = new Label();
            this.additionalControls = new FlowLayoutPanel();
            this.flowLayoutPanel1 = new FlowLayoutPanel();
            this.lblDateTime = new Label();
            this.btnRemove = new PictureButton();
            ((ISupportInitialize)(this.pluginIcon)).BeginInit();
            this.flowLayoutPanel1.SuspendLayout();
            ((ISupportInitialize)(this.btnRemove)).BeginInit();
            this.SuspendLayout();
            // 
            // pluginIcon
            // 
            this.pluginIcon.BackgroundImageLayout = ImageLayout.Stretch;
            this.pluginIcon.Location = new Point(8, 8);
            this.pluginIcon.Name = "pluginIcon";
            this.pluginIcon.Size = new Size(50, 50);
            this.pluginIcon.TabIndex = 0;
            this.pluginIcon.TabStop = false;
            // 
            // lblPluginName
            // 
            this.lblPluginName.Font = new Font("Tahoma", 9F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblPluginName.ForeColor = Color.Silver;
            this.lblPluginName.Location = new Point(64, 5);
            this.lblPluginName.Name = "lblPluginName";
            this.lblPluginName.Size = new Size(275, 13);
            this.lblPluginName.TabIndex = 1;
            this.lblPluginName.Text = "label1";
            this.lblPluginName.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblTitle
            // 
            this.lblTitle.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblTitle.Location = new Point(63, 23);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new Size(399, 22);
            this.lblTitle.TabIndex = 2;
            this.lblTitle.Text = "label1";
            // 
            // lblMessage
            // 
            this.lblMessage.AutoSize = true;
            this.lblMessage.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblMessage.Location = new Point(0, 0);
            this.lblMessage.Margin = new Padding(0);
            this.lblMessage.MaximumSize = new Size(400, 0);
            this.lblMessage.MinimumSize = new Size(400, 0);
            this.lblMessage.Name = "lblMessage";
            this.lblMessage.Size = new Size(400, 16);
            this.lblMessage.TabIndex = 3;
            // 
            // additionalControls
            // 
            this.additionalControls.AutoSize = true;
            this.additionalControls.Location = new Point(0, 26);
            this.additionalControls.Margin = new Padding(0, 10, 0, 0);
            this.additionalControls.MaximumSize = new Size(400, 0);
            this.additionalControls.MinimumSize = new Size(400, 0);
            this.additionalControls.Name = "additionalControls";
            this.additionalControls.Size = new Size(400, 0);
            this.additionalControls.TabIndex = 4;
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.AutoSize = true;
            this.flowLayoutPanel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanel1.Controls.Add(this.lblMessage);
            this.flowLayoutPanel1.Controls.Add(this.additionalControls);
            this.flowLayoutPanel1.FlowDirection = FlowDirection.TopDown;
            this.flowLayoutPanel1.Location = new Point(64, 48);
            this.flowLayoutPanel1.Margin = new Padding(0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new Size(400, 26);
            this.flowLayoutPanel1.TabIndex = 5;
            // 
            // lblDateTime
            // 
            this.lblDateTime.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblDateTime.ForeColor = Color.Silver;
            this.lblDateTime.Location = new Point(320, 5);
            this.lblDateTime.Name = "lblDateTime";
            this.lblDateTime.Size = new Size(188, 13);
            this.lblDateTime.TabIndex = 6;
            this.lblDateTime.Text = "00.00.0000 - 00:00:00";
            this.lblDateTime.TextAlign = ContentAlignment.MiddleRight;
            // 
            // btnRemove
            // 
            this.btnRemove.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnRemove.BackColor = Color.Transparent;
            this.btnRemove.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnRemove.Cursor = Cursors.Hand;
            this.btnRemove.HoverImage = Resources.Delete_Hover;
            this.btnRemove.Image = Resources.Delete_Normal;
            this.btnRemove.Location = new Point(483, 24);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new Size(30, 30);
            this.btnRemove.TabIndex = 7;
            this.btnRemove.TabStop = false;
            this.btnRemove.Click += new EventHandler(this.BtnRemove_Click);
            // 
            // NotificationItem
            // 
            this.AutoScaleDimensions = new SizeF(7F, 14F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.btnRemove);
            this.Controls.Add(this.lblDateTime);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblPluginName);
            this.Controls.Add(this.pluginIcon);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.MinimumSize = new Size(520, 0);
            this.Name = "NotificationItem";
            this.Size = new Size(525, 79);
            this.Load += new EventHandler(this.NotificationItem_Load);
            ((ISupportInitialize)(this.pluginIcon)).EndInit();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            ((ISupportInitialize)(this.btnRemove)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private PictureBox pluginIcon;
        private Label lblPluginName;
        private Label lblTitle;
        private Label lblMessage;
        private FlowLayoutPanel additionalControls;
        private FlowLayoutPanel flowLayoutPanel1;
        private Label lblDateTime;
        private PictureButton btnRemove;
    }
}
