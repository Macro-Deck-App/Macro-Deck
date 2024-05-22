
using System.ComponentModel;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class WaitDialog
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
            pictureBox1 = new PictureBox();
            lblPleaseWait = new Label();
            ((ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.Image = Properties.Resources.Spinner;
            pictureBox1.Location = new Point(111, 82);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(50, 50);
            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox1.TabIndex = 2;
            pictureBox1.TabStop = false;
            // 
            // lblPleaseWait
            // 
            lblPleaseWait.BackColor = Color.Transparent;
            lblPleaseWait.Font = new Font("Tahoma", 14.25F);
            lblPleaseWait.ForeColor = Color.Silver;
            lblPleaseWait.Location = new Point(4, 31);
            lblPleaseWait.Name = "lblPleaseWait";
            lblPleaseWait.Size = new Size(265, 31);
            lblPleaseWait.TabIndex = 3;
            lblPleaseWait.Text = "Please wait...";
            lblPleaseWait.TextAlign = ContentAlignment.MiddleCenter;
            lblPleaseWait.UseMnemonic = false;
            // 
            // WaitDialog
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(273, 160);
            ControlBox = false;
            Controls.Add(lblPleaseWait);
            Controls.Add(pictureBox1);
            Name = "WaitDialog";
            Text = "";
            ((ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private PictureBox pictureBox1;
        private Label lblPleaseWait;
    }
}