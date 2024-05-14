
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI
{
    partial class InitialSetup
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
            LanguageManager.LanguageChanged -= OnLanguageChanged;
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
            btnNext = new ButtonPrimary();
            btnBack = new ButtonPrimary();
            pagePanel = new BufferedPanel();
            lblPage = new Label();
            SuspendLayout();
            // 
            // btnNext
            // 
            btnNext.BorderRadius = 8;
            btnNext.FlatAppearance.BorderSize = 0;
            btnNext.FlatStyle = FlatStyle.Flat;
            btnNext.Font = new Font("Tahoma", 9.75F);
            btnNext.ForeColor = Color.White;
            btnNext.HoverColor = Color.Empty;
            btnNext.Icon = null;
            btnNext.Location = new Point(603, 608);
            btnNext.Name = "btnNext";
            btnNext.Progress = 0;
            btnNext.ProgressColor = Color.FromArgb(0, 103, 205);
            btnNext.Size = new Size(100, 35);
            btnNext.TabIndex = 3;
            btnNext.Text = "Next ->";
            btnNext.UseMnemonic = false;
            btnNext.UseVisualStyleBackColor = false;
            btnNext.UseWindowsAccentColor = true;
            btnNext.WriteProgress = true;
            btnNext.Click += BtnNext_Click;
            // 
            // btnBack
            // 
            btnBack.BorderRadius = 8;
            btnBack.FlatAppearance.BorderSize = 0;
            btnBack.FlatStyle = FlatStyle.Flat;
            btnBack.Font = new Font("Tahoma", 9.75F);
            btnBack.ForeColor = Color.White;
            btnBack.HoverColor = Color.Empty;
            btnBack.Icon = null;
            btnBack.Location = new Point(12, 609);
            btnBack.Name = "btnBack";
            btnBack.Progress = 0;
            btnBack.ProgressColor = Color.FromArgb(0, 103, 205);
            btnBack.Size = new Size(100, 35);
            btnBack.TabIndex = 4;
            btnBack.Text = "<- Back";
            btnBack.UseMnemonic = false;
            btnBack.UseVisualStyleBackColor = false;
            btnBack.UseWindowsAccentColor = true;
            btnBack.WriteProgress = true;
            btnBack.Click += BtnBack_Click;
            // 
            // pagePanel
            // 
            pagePanel.Location = new Point(12, 31);
            pagePanel.Name = "pagePanel";
            pagePanel.Size = new Size(691, 571);
            pagePanel.TabIndex = 5;
            // 
            // lblPage
            // 
            lblPage.Location = new Point(118, 617);
            lblPage.Name = "lblPage";
            lblPage.Size = new Size(479, 16);
            lblPage.TabIndex = 6;
            lblPage.Text = "Page 0/0";
            lblPage.TextAlign = ContentAlignment.MiddleCenter;
            lblPage.UseMnemonic = false;
            // 
            // InitialSetup
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(715, 655);
            Controls.Add(lblPage);
            Controls.Add(pagePanel);
            Controls.Add(btnBack);
            Controls.Add(btnNext);
            Name = "InitialSetup";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Welchome to Macro Deck";
            Load += InitialSetup_Load;
            ResumeLayout(false);
        }

        #endregion

        private ButtonPrimary btnNext;
        private ButtonPrimary btnBack;
        private BufferedPanel pagePanel;
        private Label lblPage;
    }
}