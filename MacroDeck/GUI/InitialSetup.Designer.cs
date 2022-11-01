
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
            this.btnNext = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnBack = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.pagePanel = new SuchByte.MacroDeck.GUI.CustomControls.BufferedPanel();
            this.lblPage = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnNext
            // 
            this.btnNext.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnNext.FlatAppearance.BorderSize = 0;
            this.btnNext.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNext.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnNext.ForeColor = System.Drawing.Color.White;
            this.btnNext.Location = new System.Drawing.Point(603, 608);
            this.btnNext.Name = "btnNext";
            this.btnNext.Size = new System.Drawing.Size(100, 35);
            this.btnNext.TabIndex = 3;
            this.btnNext.Text = "Next ->";
            this.btnNext.UseMnemonic = false;
            this.btnNext.UseVisualStyleBackColor = false;
            this.btnNext.Click += new System.EventHandler(this.BtnNext_Click);
            // 
            // btnBack
            // 
            this.btnBack.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnBack.FlatAppearance.BorderSize = 0;
            this.btnBack.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBack.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnBack.ForeColor = System.Drawing.Color.White;
            this.btnBack.Location = new System.Drawing.Point(12, 609);
            this.btnBack.Name = "btnBack";
            this.btnBack.Size = new System.Drawing.Size(100, 35);
            this.btnBack.TabIndex = 4;
            this.btnBack.Text = "<- Back";
            this.btnBack.UseMnemonic = false;
            this.btnBack.UseVisualStyleBackColor = false;
            this.btnBack.Click += new System.EventHandler(this.BtnBack_Click);
            // 
            // pagePanel
            // 
            this.pagePanel.Location = new System.Drawing.Point(12, 31);
            this.pagePanel.Name = "pagePanel";
            this.pagePanel.Size = new System.Drawing.Size(691, 571);
            this.pagePanel.TabIndex = 5;
            // 
            // lblPage
            // 
            this.lblPage.Location = new System.Drawing.Point(118, 617);
            this.lblPage.Name = "lblPage";
            this.lblPage.Size = new System.Drawing.Size(479, 16);
            this.lblPage.TabIndex = 6;
            this.lblPage.Text = "Page 0/0";
            this.lblPage.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblPage.UseMnemonic = false;
            // 
            // InitialSetup
            // 
           
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(715, 655);
            this.Controls.Add(this.lblPage);
            this.Controls.Add(this.pagePanel);
            this.Controls.Add(this.btnBack);
            this.Controls.Add(this.btnNext);
            this.Name = "InitialSetup";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "InitialSetup";
            this.Load += new System.EventHandler(this.InitialSetup_Load);
            this.Controls.SetChildIndex(this.btnNext, 0);
            this.Controls.SetChildIndex(this.btnBack, 0);
            this.Controls.SetChildIndex(this.pagePanel, 0);
            this.Controls.SetChildIndex(this.lblPage, 0);
            this.ResumeLayout(false);

        }

        #endregion

        private ButtonPrimary btnNext;
        private ButtonPrimary btnBack;
        private BufferedPanel pagePanel;
        private Label lblPage;
    }
}