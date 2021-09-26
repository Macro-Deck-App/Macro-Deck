
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class LicensesDialog
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
            this.label1 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.licensesPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.Location = new System.Drawing.Point(12, 10);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(174, 23);
            this.label1.TabIndex = 3;
            this.label1.Text = "Third-party licenses";
            // 
            // label3
            // 
            this.label3.Location = new System.Drawing.Point(12, 37);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(537, 41);
            this.label3.TabIndex = 4;
            this.label3.Text = "Macro Deck uses some awesome free and open-source software. Some of them require " +
    "their licenses to be included. Thank you all who created this awesome software!\r" +
    "\n";
            this.label3.Click += new System.EventHandler(this.label3_Click);
            // 
            // licensesPanel
            // 
            this.licensesPanel.AutoScroll = true;
            this.licensesPanel.Location = new System.Drawing.Point(12, 81);
            this.licensesPanel.Name = "licensesPanel";
            this.licensesPanel.Size = new System.Drawing.Size(537, 538);
            this.licensesPanel.TabIndex = 5;
            // 
            // LicensesDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(563, 632);
            this.Controls.Add(this.licensesPanel);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label1);
            this.Name = "LicensesDialog";
            this.Text = "LicensesDialog";
            this.Load += new System.EventHandler(this.LicensesDialog_Load);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.label3, 0);
            this.Controls.SetChildIndex(this.licensesPanel, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.FlowLayoutPanel licensesPanel;
    }
}