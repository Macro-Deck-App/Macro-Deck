
using System.ComponentModel;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class MessageBox
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
            lblMessage = new Label();
            buttonMessageBoxPanel = new FlowLayoutPanel();
            lblTitle = new Label();
            SuspendLayout();
            // 
            // lblMessage
            // 
            lblMessage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblMessage.Font = new Font("Tahoma", 12F);
            lblMessage.Location = new Point(12, 41);
            lblMessage.Name = "lblMessage";
            lblMessage.Size = new Size(439, 112);
            lblMessage.TabIndex = 3;
            lblMessage.TextAlign = ContentAlignment.MiddleCenter;
            lblMessage.UseMnemonic = false;
            // 
            // buttonMessageBoxPanel
            // 
            buttonMessageBoxPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            buttonMessageBoxPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonMessageBoxPanel.Location = new Point(12, 167);
            buttonMessageBoxPanel.Name = "buttonMessageBoxPanel";
            buttonMessageBoxPanel.Size = new Size(439, 29);
            buttonMessageBoxPanel.TabIndex = 4;
            // 
            // lblTitle
            // 
            lblTitle.Font = new Font("Tahoma", 14.25F);
            lblTitle.ForeColor = Color.Silver;
            lblTitle.Location = new Point(12, 9);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new Size(439, 32);
            lblTitle.TabIndex = 5;
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblTitle.UseMnemonic = false;
            // 
            // MessageBox
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(463, 204);
            Controls.Add(lblTitle);
            Controls.Add(buttonMessageBoxPanel);
            Controls.Add(lblMessage);
            Name = "MessageBox";
            Text = "Macro Deck";
            ResumeLayout(false);
        }

        #endregion

        private Label lblMessage;
        private FlowLayoutPanel buttonMessageBoxPanel;
        private Label lblTitle;
    }
}