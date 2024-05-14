
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class IconImportQuality
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
            btnOk = new ButtonPrimary();
            qualityLowest = new RadioButton();
            label1 = new Label();
            qualityLow = new RadioButton();
            qualityNormal = new RadioButton();
            qualityHigh = new RadioButton();
            qualityOriginal = new RadioButton();
            lblInfo = new Label();
            SuspendLayout();
            // 
            // btnOk
            // 
            btnOk.BorderRadius = 8;
            btnOk.Cursor = Cursors.Hand;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.Font = new Font("Tahoma", 9.75F);
            btnOk.ForeColor = Color.White;
            btnOk.HoverColor = Color.Empty;
            btnOk.Icon = null;
            btnOk.Location = new Point(288, 188);
            btnOk.Name = "btnOk";
            btnOk.Progress = 0;
            btnOk.ProgressColor = Color.FromArgb(0, 103, 205);
            btnOk.Size = new Size(75, 25);
            btnOk.TabIndex = 3;
            btnOk.Text = "Ok";
            btnOk.UseMnemonic = false;
            btnOk.UseVisualStyleBackColor = false;
            btnOk.UseWindowsAccentColor = true;
            btnOk.WriteProgress = true;
            btnOk.Click += BtnOk_Click;
            // 
            // qualityLowest
            // 
            qualityLowest.AutoSize = true;
            qualityLowest.Location = new Point(12, 144);
            qualityLowest.Name = "qualityLowest";
            qualityLowest.Size = new Size(113, 20);
            qualityLowest.TabIndex = 4;
            qualityLowest.Text = "Lowest (100px)";
            qualityLowest.UseMnemonic = false;
            qualityLowest.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Tahoma", 12F);
            label1.Location = new Point(44, 12);
            label1.Name = "label1";
            label1.Size = new Size(59, 19);
            label1.TabIndex = 5;
            label1.Text = "Quality";
            label1.UseMnemonic = false;
            // 
            // qualityLow
            // 
            qualityLow.AutoSize = true;
            qualityLow.Location = new Point(12, 118);
            qualityLow.Name = "qualityLow";
            qualityLow.Size = new Size(96, 20);
            qualityLow.TabIndex = 6;
            qualityLow.Text = "Low (150px)";
            qualityLow.UseMnemonic = false;
            qualityLow.UseVisualStyleBackColor = true;
            // 
            // qualityNormal
            // 
            qualityNormal.AutoSize = true;
            qualityNormal.Checked = true;
            qualityNormal.Location = new Point(12, 92);
            qualityNormal.Name = "qualityNormal";
            qualityNormal.Size = new Size(114, 20);
            qualityNormal.TabIndex = 7;
            qualityNormal.TabStop = true;
            qualityNormal.Text = "Normal (200px)";
            qualityNormal.UseVisualStyleBackColor = true;
            // 
            // qualityHigh
            // 
            qualityHigh.AutoSize = true;
            qualityHigh.Location = new Point(12, 66);
            qualityHigh.Name = "qualityHigh";
            qualityHigh.Size = new Size(98, 20);
            qualityHigh.TabIndex = 8;
            qualityHigh.Text = "High (350px)";
            qualityHigh.UseMnemonic = false;
            qualityHigh.UseVisualStyleBackColor = true;
            // 
            // qualityOriginal
            // 
            qualityOriginal.AutoSize = true;
            qualityOriginal.Location = new Point(12, 40);
            qualityOriginal.Name = "qualityOriginal";
            qualityOriginal.Size = new Size(69, 20);
            qualityOriginal.TabIndex = 9;
            qualityOriginal.Text = "Original";
            qualityOriginal.UseMnemonic = false;
            qualityOriginal.UseVisualStyleBackColor = true;
            // 
            // lblInfo
            // 
            lblInfo.Location = new Point(148, 40);
            lblInfo.Name = "lblInfo";
            lblInfo.Size = new Size(215, 145);
            lblInfo.TabIndex = 10;
            lblInfo.Text = "High quality icons can lead to an increase of memory usage and loading times especially when using many big animated gifs.\r\n\r\nFor gifs it is recommended to use the low or the lowest preset.";
            lblInfo.UseMnemonic = false;
            // 
            // IconImportQuality
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(376, 224);
            Controls.Add(lblInfo);
            Controls.Add(qualityOriginal);
            Controls.Add(qualityHigh);
            Controls.Add(qualityNormal);
            Controls.Add(qualityLow);
            Controls.Add(label1);
            Controls.Add(qualityLowest);
            Controls.Add(btnOk);
            Name = "IconImportQuality";
            Text = "Icon import";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ButtonPrimary btnOk;
        private RadioButton qualityLowest;
        private Label label1;
        private RadioButton qualityLow;
        private RadioButton qualityNormal;
        private RadioButton qualityHigh;
        private RadioButton qualityOriginal;
        private Label lblInfo;
    }
}