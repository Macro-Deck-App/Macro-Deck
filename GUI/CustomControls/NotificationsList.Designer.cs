
namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class NotificationsList
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
            foreach (var control in this.notificationList.Controls)
            {
                ((NotificationItem)control).ClearAdditionalControls();
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
            this.notificationList = new System.Windows.Forms.FlowLayoutPanel();
            this.SuspendLayout();
            // 
            // notificationList
            // 
            this.notificationList.AutoScroll = true;
            this.notificationList.AutoSize = true;
            this.notificationList.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.notificationList.Location = new System.Drawing.Point(3, 3);
            this.notificationList.Margin = new System.Windows.Forms.Padding(0);
            this.notificationList.MaximumSize = new System.Drawing.Size(544, 444);
            this.notificationList.MinimumSize = new System.Drawing.Size(544, 126);
            this.notificationList.Name = "notificationList";
            this.notificationList.Size = new System.Drawing.Size(544, 134);
            this.notificationList.TabIndex = 0;
            this.notificationList.WrapContents = false;
            // 
            // NotificationsList
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.notificationList);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.MaximumSize = new System.Drawing.Size(550, 450);
            this.MinimumSize = new System.Drawing.Size(550, 140);
            this.Name = "NotificationsList";
            this.Size = new System.Drawing.Size(550, 142);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.FlowLayoutPanel notificationList;
    }
}
