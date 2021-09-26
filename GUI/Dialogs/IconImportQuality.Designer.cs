
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class IconImportQuality
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
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.qualityLowest = new System.Windows.Forms.RadioButton();
            this.label1 = new System.Windows.Forms.Label();
            this.qualityLow = new System.Windows.Forms.RadioButton();
            this.qualityNormal = new System.Windows.Forms.RadioButton();
            this.qualityHigh = new System.Windows.Forms.RadioButton();
            this.qualityOriginal = new System.Windows.Forms.RadioButton();
            this.lblInfo = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnOk
            // 
            this.btnOk.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.Location = new System.Drawing.Point(288, 188);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 3;
            this.btnOk.Text = "Ok";
            this.btnOk.UseVisualStyleBackColor = false;
            this.btnOk.Click += new System.EventHandler(this.btnOk_Click);
            // 
            // qualityLowest
            // 
            this.qualityLowest.AutoSize = true;
            this.qualityLowest.Location = new System.Drawing.Point(12, 144);
            this.qualityLowest.Name = "qualityLowest";
            this.qualityLowest.Size = new System.Drawing.Size(113, 20);
            this.qualityLowest.TabIndex = 4;
            this.qualityLowest.Text = "Lowest (100px)";
            this.qualityLowest.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.Location = new System.Drawing.Point(44, 12);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(59, 19);
            this.label1.TabIndex = 5;
            this.label1.Text = "Quality";
            // 
            // qualityLow
            // 
            this.qualityLow.AutoSize = true;
            this.qualityLow.Location = new System.Drawing.Point(12, 118);
            this.qualityLow.Name = "qualityLow";
            this.qualityLow.Size = new System.Drawing.Size(96, 20);
            this.qualityLow.TabIndex = 6;
            this.qualityLow.Text = "Low (150px)";
            this.qualityLow.UseVisualStyleBackColor = true;
            // 
            // qualityNormal
            // 
            this.qualityNormal.AutoSize = true;
            this.qualityNormal.Checked = true;
            this.qualityNormal.Location = new System.Drawing.Point(12, 92);
            this.qualityNormal.Name = "qualityNormal";
            this.qualityNormal.Size = new System.Drawing.Size(114, 20);
            this.qualityNormal.TabIndex = 7;
            this.qualityNormal.TabStop = true;
            this.qualityNormal.Text = "Normal (200px)";
            this.qualityNormal.UseVisualStyleBackColor = true;
            // 
            // qualityHigh
            // 
            this.qualityHigh.AutoSize = true;
            this.qualityHigh.Location = new System.Drawing.Point(12, 66);
            this.qualityHigh.Name = "qualityHigh";
            this.qualityHigh.Size = new System.Drawing.Size(98, 20);
            this.qualityHigh.TabIndex = 8;
            this.qualityHigh.Text = "High (350px)";
            this.qualityHigh.UseVisualStyleBackColor = true;
            // 
            // qualityOriginal
            // 
            this.qualityOriginal.AutoSize = true;
            this.qualityOriginal.Location = new System.Drawing.Point(12, 40);
            this.qualityOriginal.Name = "qualityOriginal";
            this.qualityOriginal.Size = new System.Drawing.Size(69, 20);
            this.qualityOriginal.TabIndex = 9;
            this.qualityOriginal.Text = "Original";
            this.qualityOriginal.UseVisualStyleBackColor = true;
            // 
            // lblInfo
            // 
            this.lblInfo.Location = new System.Drawing.Point(148, 40);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(215, 145);
            this.lblInfo.TabIndex = 10;
            this.lblInfo.Text = "High quality icons can lead to an increase of memory usage and loading times espe" +
    "cially when using many big animated gifs.\r\n\r\nFor gifs it is recommended to use t" +
    "he low or the lowest preset.";
            // 
            // IconImportQuality
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(376, 224);
            this.Controls.Add(this.lblInfo);
            this.Controls.Add(this.qualityOriginal);
            this.Controls.Add(this.qualityHigh);
            this.Controls.Add(this.qualityNormal);
            this.Controls.Add(this.qualityLow);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.qualityLowest);
            this.Controls.Add(this.btnOk);
            this.Name = "IconImportQuality";
            this.Text = "IconImportQuality";
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.qualityLowest, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.qualityLow, 0);
            this.Controls.SetChildIndex(this.qualityNormal, 0);
            this.Controls.SetChildIndex(this.qualityHigh, 0);
            this.Controls.SetChildIndex(this.qualityOriginal, 0);
            this.Controls.SetChildIndex(this.lblInfo, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private CustomControls.ButtonPrimary btnOk;
        private System.Windows.Forms.RadioButton qualityLowest;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.RadioButton qualityLow;
        private System.Windows.Forms.RadioButton qualityNormal;
        private System.Windows.Forms.RadioButton qualityHigh;
        private System.Windows.Forms.RadioButton qualityOriginal;
        private System.Windows.Forms.Label lblInfo;
    }
}