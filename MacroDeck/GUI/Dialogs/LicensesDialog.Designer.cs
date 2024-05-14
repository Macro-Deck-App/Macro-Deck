
using System.ComponentModel;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class LicensesDialog
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
            label1 = new Label();
            label3 = new Label();
            licensesPanel = new FlowLayoutPanel();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Tahoma", 14.25F);
            label1.Location = new Point(12, 10);
            label1.Name = "label1";
            label1.Size = new Size(174, 23);
            label1.TabIndex = 3;
            label1.Text = "Third-party licenses";
            label1.UseMnemonic = false;
            // 
            // label3
            // 
            label3.Location = new Point(12, 37);
            label3.Name = "label3";
            label3.Size = new Size(537, 41);
            label3.TabIndex = 4;
            label3.Text = "Macro Deck uses some awesome free and open-source software. Some of them require their licenses to be included. Thank you all who created this awesome software!\r\n";
            label3.UseMnemonic = false;
            label3.Click += label3_Click;
            // 
            // licensesPanel
            // 
            licensesPanel.AutoScroll = true;
            licensesPanel.Location = new Point(12, 81);
            licensesPanel.Name = "licensesPanel";
            licensesPanel.Size = new Size(537, 538);
            licensesPanel.TabIndex = 5;
            // 
            // LicensesDialog
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(563, 632);
            Controls.Add(licensesPanel);
            Controls.Add(label3);
            Controls.Add(label1);
            Name = "LicensesDialog";
            Text = "Licenses";
            Load += LicensesDialog_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Label label3;
        private FlowLayoutPanel licensesPanel;
    }
}