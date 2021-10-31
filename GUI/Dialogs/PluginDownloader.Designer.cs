
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class PluginDownloader
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
            this.statusBox = new RoundedTextBox();
            this.lblDownloadingPlugins = new System.Windows.Forms.Label();
            this.currentProgress = new System.Windows.Forms.ProgressBar();
            this.totalProgress = new System.Windows.Forms.ProgressBar();
            this.lblTotalProgress = new System.Windows.Forms.Label();
            this.lblCurrentProgress = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // statusBox
            // 
            this.statusBox.ForeColor = System.Drawing.Color.White;
            this.statusBox.Location = new System.Drawing.Point(12, 36);
            this.statusBox.Multiline = true;
            this.statusBox.Name = "statusBox";
            this.statusBox.ReadOnly = true;
            this.statusBox.Size = new System.Drawing.Size(501, 293);
            this.statusBox.TabIndex = 3;
            // 
            // lblDownloadingPlugins
            // 
            this.lblDownloadingPlugins.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblDownloadingPlugins.Location = new System.Drawing.Point(12, 8);
            this.lblDownloadingPlugins.Name = "lblDownloadingPlugins";
            this.lblDownloadingPlugins.Size = new System.Drawing.Size(503, 25);
            this.lblDownloadingPlugins.TabIndex = 4;
            // 
            // currentProgress
            // 
            this.currentProgress.Location = new System.Drawing.Point(12, 365);
            this.currentProgress.Name = "currentProgress";
            this.currentProgress.Size = new System.Drawing.Size(501, 11);
            this.currentProgress.TabIndex = 5;
            // 
            // totalProgress
            // 
            this.totalProgress.Location = new System.Drawing.Point(12, 407);
            this.totalProgress.Name = "totalProgress";
            this.totalProgress.Size = new System.Drawing.Size(501, 11);
            this.totalProgress.TabIndex = 6;
            // 
            // lblTotalProgress
            // 
            this.lblTotalProgress.AutoSize = true;
            this.lblTotalProgress.Location = new System.Drawing.Point(12, 388);
            this.lblTotalProgress.Name = "lblTotalProgress";
            this.lblTotalProgress.Size = new System.Drawing.Size(90, 16);
            this.lblTotalProgress.TabIndex = 8;
            this.lblTotalProgress.Text = "Total progress";
            // 
            // lblCurrentProgress
            // 
            this.lblCurrentProgress.AutoSize = true;
            this.lblCurrentProgress.Location = new System.Drawing.Point(12, 346);
            this.lblCurrentProgress.Name = "lblCurrentProgress";
            this.lblCurrentProgress.Size = new System.Drawing.Size(104, 16);
            this.lblCurrentProgress.TabIndex = 9;
            this.lblCurrentProgress.Text = "Current progress";
            // 
            // PluginDownloader
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(527, 435);
            this.Controls.Add(this.lblCurrentProgress);
            this.Controls.Add(this.lblTotalProgress);
            this.Controls.Add(this.totalProgress);
            this.Controls.Add(this.currentProgress);
            this.Controls.Add(this.lblDownloadingPlugins);
            this.Controls.Add(this.statusBox);
            this.Name = "PluginDownloader";
            this.Text = "PluginDownloader";
            this.Controls.SetChildIndex(this.statusBox, 0);
            this.Controls.SetChildIndex(this.lblDownloadingPlugins, 0);
            this.Controls.SetChildIndex(this.currentProgress, 0);
            this.Controls.SetChildIndex(this.totalProgress, 0);
            this.Controls.SetChildIndex(this.lblTotalProgress, 0);
            this.Controls.SetChildIndex(this.lblCurrentProgress, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private RoundedTextBox statusBox;
        private System.Windows.Forms.Label lblDownloadingPlugins;
        private System.Windows.Forms.ProgressBar currentProgress;
        private System.Windows.Forms.ProgressBar totalProgress;
        private System.Windows.Forms.Label lblTotalProgress;
        private System.Windows.Forms.Label lblCurrentProgress;
    }
}