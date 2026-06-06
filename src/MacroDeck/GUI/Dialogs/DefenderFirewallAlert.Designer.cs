
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class DefenderFirewallAlert
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

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
            ComponentResourceManager resources = new ComponentResourceManager(typeof(DefenderFirewallAlert));
            pictureBox1 = new PictureBox();
            lblImportant = new Label();
            lblInfo = new Label();
            btnGotIt = new ButtonPrimary();
            ((ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.BackgroundImage = Properties.Resources.WindowsDefenderSecurityAlert;
            pictureBox1.BackgroundImageLayout = ImageLayout.Stretch;
            pictureBox1.Location = new Point(53, 58);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(480, 343);
            pictureBox1.TabIndex = 2;
            pictureBox1.TabStop = false;
            // 
            // lblImportant
            // 
            lblImportant.Font = new Font("Tahoma", 24F);
            lblImportant.Location = new Point(36, 12);
            lblImportant.Name = "lblImportant";
            lblImportant.Size = new Size(514, 43);
            lblImportant.TabIndex = 3;
            lblImportant.Text = "Important!";
            lblImportant.TextAlign = ContentAlignment.MiddleCenter;
            lblImportant.UseMnemonic = false;
            // 
            // lblInfo
            // 
            lblInfo.Font = new Font("Tahoma", 12F);
            lblInfo.Location = new Point(29, 414);
            lblInfo.Name = "lblInfo";
            lblInfo.Size = new Size(529, 187);
            lblInfo.TabIndex = 4;
            lblInfo.Text = resources.GetString("lblInfo.Text");
            lblInfo.UseMnemonic = false;
            // 
            // btnGotIt
            // 
            btnGotIt.BorderRadius = 8;
            btnGotIt.Cursor = Cursors.Hand;
            btnGotIt.FlatAppearance.BorderSize = 0;
            btnGotIt.FlatStyle = FlatStyle.Flat;
            btnGotIt.Font = new Font("Tahoma", 9.75F);
            btnGotIt.ForeColor = Color.White;
            btnGotIt.HoverColor = Color.FromArgb(0, 89, 184);
            btnGotIt.Icon = null;
            btnGotIt.Location = new Point(218, 604);
            btnGotIt.Name = "btnGotIt";
            btnGotIt.Progress = 0;
            btnGotIt.ProgressColor = Color.FromArgb(0, 46, 94);
            btnGotIt.Size = new Size(150, 40);
            btnGotIt.TabIndex = 5;
            btnGotIt.Text = "Got it!";
            btnGotIt.UseMnemonic = false;
            btnGotIt.UseVisualStyleBackColor = false;
            btnGotIt.UseWindowsAccentColor = true;
            btnGotIt.WriteProgress = true;
            btnGotIt.Click += BtnGotIt_Click;
            // 
            // DefenderFirewallAlert
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(586, 655);
            Controls.Add(btnGotIt);
            Controls.Add(lblInfo);
            Controls.Add(lblImportant);
            Controls.Add(pictureBox1);
            Name = "DefenderFirewallAlert";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Macro Deck";
            ((ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private PictureBox pictureBox1;
        private Label lblImportant;
        private Label lblInfo;
        private ButtonPrimary btnGotIt;
    }
}