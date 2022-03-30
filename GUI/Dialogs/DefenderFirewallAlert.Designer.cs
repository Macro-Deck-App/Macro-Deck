
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class DefenderFirewallAlert
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DefenderFirewallAlert));
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.lblImportant = new System.Windows.Forms.Label();
            this.lblInfo = new System.Windows.Forms.Label();
            this.btnGotIt = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.WindowsDefenderSecurityAlert;
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox1.Location = new System.Drawing.Point(53, 58);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(480, 343);
            this.pictureBox1.TabIndex = 2;
            this.pictureBox1.TabStop = false;
            // 
            // lblImportant
            // 
            this.lblImportant.Font = new System.Drawing.Font("Tahoma", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblImportant.Location = new System.Drawing.Point(36, 12);
            this.lblImportant.Name = "lblImportant";
            this.lblImportant.Size = new System.Drawing.Size(514, 43);
            this.lblImportant.TabIndex = 3;
            this.lblImportant.Text = "Important!";
            this.lblImportant.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblImportant.UseMnemonic = false;
            // 
            // lblInfo
            // 
            this.lblInfo.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblInfo.Location = new System.Drawing.Point(29, 414);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(529, 187);
            this.lblInfo.TabIndex = 4;
            this.lblInfo.Text = resources.GetString("lblInfo.Text");
            this.lblInfo.UseMnemonic = false;
            // 
            // btnGotIt
            // 
            this.btnGotIt.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnGotIt.BorderRadius = 8;
            this.btnGotIt.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnGotIt.FlatAppearance.BorderSize = 0;
            this.btnGotIt.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGotIt.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnGotIt.ForeColor = System.Drawing.Color.White;
            this.btnGotIt.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnGotIt.Icon = null;
            this.btnGotIt.Location = new System.Drawing.Point(218, 604);
            this.btnGotIt.Name = "btnGotIt";
            this.btnGotIt.Progress = 0;
            this.btnGotIt.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnGotIt.Size = new System.Drawing.Size(150, 40);
            this.btnGotIt.TabIndex = 5;
            this.btnGotIt.Text = "Got it!";
            this.btnGotIt.UseMnemonic = false;
            this.btnGotIt.UseVisualStyleBackColor = false;
            this.btnGotIt.Click += new System.EventHandler(this.BtnGotIt_Click);
            // 
            // DefenderFirewallAlert
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(586, 655);
            this.Controls.Add(this.btnGotIt);
            this.Controls.Add(this.lblInfo);
            this.Controls.Add(this.lblImportant);
            this.Controls.Add(this.pictureBox1);
            this.Name = "DefenderFirewallAlert";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "DefenderFirewallAlert";
            this.Controls.SetChildIndex(this.pictureBox1, 0);
            this.Controls.SetChildIndex(this.lblImportant, 0);
            this.Controls.SetChildIndex(this.lblInfo, 0);
            this.Controls.SetChildIndex(this.btnGotIt, 0);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label lblImportant;
        private System.Windows.Forms.Label lblInfo;
        private CustomControls.ButtonPrimary btnGotIt;
    }
}