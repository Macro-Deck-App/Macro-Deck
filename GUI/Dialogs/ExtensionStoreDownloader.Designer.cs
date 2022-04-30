
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class ExtensionStoreDownloader
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
            this.output = new System.Windows.Forms.RichTextBox();
            this.btnDone = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // output
            // 
            this.output.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.output.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.output.Location = new System.Drawing.Point(10, 31);
            this.output.Name = "output";
            this.output.ReadOnly = true;
            this.output.Size = new System.Drawing.Size(576, 213);
            this.output.TabIndex = 2;
            this.output.Text = "";
            // 
            // btnDone
            // 
            this.btnDone.BorderRadius = 8;
            this.btnDone.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDone.FlatAppearance.BorderSize = 0;
            this.btnDone.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDone.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDone.ForeColor = System.Drawing.Color.White;
            this.btnDone.HoverColor = System.Drawing.Color.Empty;
            this.btnDone.Icon = null;
            this.btnDone.Location = new System.Drawing.Point(183, 255);
            this.btnDone.Name = "btnDone";
            this.btnDone.Progress = 0;
            this.btnDone.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnDone.Size = new System.Drawing.Size(231, 30);
            this.btnDone.TabIndex = 3;
            this.btnDone.Text = "Done";
            this.btnDone.UseMnemonic = false;
            this.btnDone.UseVisualStyleBackColor = true;
            this.btnDone.UseWindowsAccentColor = true;
            this.btnDone.Visible = false;
            this.btnDone.Click += new System.EventHandler(this.BtnDone_Click);
            // 
            // label2
            // 
            this.label2.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.label2.ForeColor = System.Drawing.Color.Silver;
            this.label2.Location = new System.Drawing.Point(10, 5);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(245, 23);
            this.label2.TabIndex = 5;
            this.label2.Text = "Extension Store Downloader";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.label2.UseMnemonic = false;
            // 
            // ExtensionStoreDownloader
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(597, 293);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.btnDone);
            this.Controls.Add(this.output);
            this.Location = new System.Drawing.Point(0, 0);
            this.Name = "ExtensionStoreDownloader";
            this.ShowIcon = true;
            this.Text = "Extension Store Downloader";
            this.Load += new System.EventHandler(this.ExtensionStoreDownloader_Load);
            this.Controls.SetChildIndex(this.output, 0);
            this.Controls.SetChildIndex(this.btnDone, 0);
            this.Controls.SetChildIndex(this.label2, 0);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.RichTextBox output;
        private CustomControls.ButtonPrimary btnDone;
        private System.Windows.Forms.Label label2;
    }
}