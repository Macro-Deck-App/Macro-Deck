
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class NotificationsList
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
            this.notificationList = new FlowLayoutPanel();
            this.SuspendLayout();
            // 
            // notificationList
            // 
            this.notificationList.AutoScroll = true;
            this.notificationList.AutoSize = true;
            this.notificationList.FlowDirection = FlowDirection.TopDown;
            this.notificationList.Location = new Point(3, 3);
            this.notificationList.Margin = new Padding(0);
            this.notificationList.MaximumSize = new Size(544, 444);
            this.notificationList.MinimumSize = new Size(544, 126);
            this.notificationList.Name = "notificationList";
            this.notificationList.Size = new Size(544, 134);
            this.notificationList.TabIndex = 0;
            this.notificationList.WrapContents = false;
            // 
            // NotificationsList
            // 
            this.AutoScaleDimensions = new SizeF(7F, 14F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.notificationList);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.MaximumSize = new Size(550, 450);
            this.MinimumSize = new Size(550, 140);
            this.Name = "NotificationsList";
            this.Size = new Size(550, 142);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private FlowLayoutPanel notificationList;
    }
}
