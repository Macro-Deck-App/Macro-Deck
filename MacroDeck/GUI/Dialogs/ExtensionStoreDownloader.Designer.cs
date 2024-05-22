
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class ExtensionStoreDownloader
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
            btnDone = new ButtonPrimary();
            downloadList = new FlowLayoutPanel();
            lblPackagesToDownload = new Label();
            SuspendLayout();
            // 
            // btnDone
            // 
            btnDone.BorderRadius = 8;
            btnDone.Cursor = Cursors.Hand;
            btnDone.FlatAppearance.BorderSize = 0;
            btnDone.FlatStyle = FlatStyle.Flat;
            btnDone.Font = new Font("Tahoma", 9.75F);
            btnDone.ForeColor = Color.White;
            btnDone.HoverColor = Color.Empty;
            btnDone.Icon = null;
            btnDone.Location = new Point(183, 358);
            btnDone.Name = "btnDone";
            btnDone.Progress = 0;
            btnDone.ProgressColor = Color.FromArgb(0, 103, 225);
            btnDone.Size = new Size(231, 30);
            btnDone.TabIndex = 3;
            btnDone.Text = "Done";
            btnDone.UseMnemonic = false;
            btnDone.UseVisualStyleBackColor = true;
            btnDone.UseWindowsAccentColor = true;
            btnDone.Visible = false;
            btnDone.WriteProgress = true;
            btnDone.Click += BtnDone_Click;
            // 
            // downloadList
            // 
            downloadList.AutoScroll = true;
            downloadList.Location = new Point(6, 27);
            downloadList.Name = "downloadList";
            downloadList.Size = new Size(607, 325);
            downloadList.TabIndex = 6;
            // 
            // lblPackagesToDownload
            // 
            lblPackagesToDownload.Font = new Font("Tahoma", 9.75F);
            lblPackagesToDownload.ForeColor = Color.Silver;
            lblPackagesToDownload.Location = new Point(6, 1);
            lblPackagesToDownload.Name = "lblPackagesToDownload";
            lblPackagesToDownload.Size = new Size(245, 23);
            lblPackagesToDownload.TabIndex = 7;
            lblPackagesToDownload.TextAlign = ContentAlignment.MiddleLeft;
            lblPackagesToDownload.UseMnemonic = false;
            // 
            // ExtensionStoreDownloader
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(618, 392);
            Controls.Add(lblPackagesToDownload);
            Controls.Add(downloadList);
            Controls.Add(btnDone);
            Name = "ExtensionStoreDownloader";
            ShowIcon = true;
            Text = "Extension Store Downloader";
            Load += ExtensionStoreDownloader_Load;
            ResumeLayout(false);
        }

        #endregion
        private ButtonPrimary btnDone;
        private FlowLayoutPanel downloadList;
        private Label lblPackagesToDownload;
    }
}