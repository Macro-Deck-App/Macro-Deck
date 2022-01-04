
using SuchByte.MacroDeck.Hotkeys;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class HotkeySelector
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            HotkeyManager.Resume();
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblPressKeysNow = new System.Windows.Forms.Label();
            this.lblDetectedKeys = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // lblPressKeysNow
            // 
            this.lblPressKeysNow.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPressKeysNow.Location = new System.Drawing.Point(4, 41);
            this.lblPressKeysNow.Name = "lblPressKeysNow";
            this.lblPressKeysNow.Size = new System.Drawing.Size(405, 59);
            this.lblPressKeysNow.TabIndex = 2;
            this.lblPressKeysNow.Text = "Press the keys now";
            this.lblPressKeysNow.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // lblDetectedKeys
            // 
            this.lblDetectedKeys.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDetectedKeys.Location = new System.Drawing.Point(4, 129);
            this.lblDetectedKeys.Name = "lblDetectedKeys";
            this.lblDetectedKeys.Size = new System.Drawing.Size(405, 31);
            this.lblDetectedKeys.TabIndex = 3;
            this.lblDetectedKeys.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // HotkeySelector
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(413, 200);
            this.Controls.Add(this.lblDetectedKeys);
            this.Controls.Add(this.lblPressKeysNow);
            this.Name = "HotkeySelector";
            this.Text = "HotkeySelector";
            this.Load += new System.EventHandler(this.HotkeySelector_Load);
            this.Controls.SetChildIndex(this.lblPressKeysNow, 0);
            this.Controls.SetChildIndex(this.lblDetectedKeys, 0);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblPressKeysNow;
        private System.Windows.Forms.Label lblDetectedKeys;
    }
}