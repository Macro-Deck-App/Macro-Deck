
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class CrashReportDialog
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CrashReportDialog));
            this.pictureBox2 = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.crashReport = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.comment = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.btnSendCrashReport = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnContinue = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.label3 = new System.Windows.Forms.Label();
            this.autoContinueTimer = new System.Windows.Forms.Timer(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox2
            // 
            this.pictureBox2.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Macro_Deck_error;
            this.pictureBox2.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox2.Location = new System.Drawing.Point(12, 15);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new System.Drawing.Size(100, 100);
            this.pictureBox2.TabIndex = 3;
            this.pictureBox2.TabStop = false;
            // 
            // label1
            // 
            this.label1.Font = new System.Drawing.Font("Tahoma", 21.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.Location = new System.Drawing.Point(118, 15);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(423, 60);
            this.label1.TabIndex = 4;
            this.label1.Text = "Oops, there was a problem :-(";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // crashReport
            // 
            this.crashReport.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.crashReport.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.crashReport.ForeColor = System.Drawing.Color.White;
            this.crashReport.Location = new System.Drawing.Point(12, 141);
            this.crashReport.Multiline = true;
            this.crashReport.Name = "crashReport";
            this.crashReport.ReadOnly = true;
            this.crashReport.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.crashReport.Size = new System.Drawing.Size(563, 148);
            this.crashReport.TabIndex = 7;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.label4.Location = new System.Drawing.Point(12, 121);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(51, 16);
            this.label4.TabIndex = 8;
            this.label4.Text = "Details";
            // 
            // comment
            // 
            this.comment.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.comment.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.comment.ForeColor = System.Drawing.Color.White;
            this.comment.Location = new System.Drawing.Point(12, 462);
            this.comment.Multiline = true;
            this.comment.Name = "comment";
            this.comment.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.comment.Size = new System.Drawing.Size(563, 49);
            this.comment.TabIndex = 9;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.label5.Location = new System.Drawing.Point(12, 443);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(421, 16);
            this.label5.TabIndex = 10;
            this.label5.Text = "Comment e.g. what did you do before this happened? (optional)";
            // 
            // label6
            // 
            this.label6.Location = new System.Drawing.Point(12, 318);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(563, 114);
            this.label6.TabIndex = 11;
            this.label6.Text = resources.GetString("label6.Text");
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.label7.Location = new System.Drawing.Point(12, 299);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(217, 16);
            this.label7.TabIndex = 12;
            this.label7.Text = "Things that can cause a problem";
            // 
            // btnSendCrashReport
            // 
            this.btnSendCrashReport.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnSendCrashReport.BorderRadius = 8;
            this.btnSendCrashReport.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnSendCrashReport.FlatAppearance.BorderSize = 0;
            this.btnSendCrashReport.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSendCrashReport.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.btnSendCrashReport.ForeColor = System.Drawing.Color.White;
            this.btnSendCrashReport.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnSendCrashReport.Icon = null;
            this.btnSendCrashReport.Location = new System.Drawing.Point(12, 517);
            this.btnSendCrashReport.Name = "btnSendCrashReport";
            this.btnSendCrashReport.Progress = 0;
            this.btnSendCrashReport.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnSendCrashReport.Size = new System.Drawing.Size(217, 37);
            this.btnSendCrashReport.TabIndex = 14;
            this.btnSendCrashReport.Text = "Send report and continue";
            this.btnSendCrashReport.UseVisualStyleBackColor = false;
            this.btnSendCrashReport.Click += new System.EventHandler(this.BtnSendCrashReport_Click);
            // 
            // btnContinue
            // 
            this.btnContinue.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.btnContinue.BorderRadius = 8;
            this.btnContinue.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnContinue.FlatAppearance.BorderSize = 0;
            this.btnContinue.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnContinue.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.btnContinue.ForeColor = System.Drawing.Color.White;
            this.btnContinue.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnContinue.Icon = null;
            this.btnContinue.Location = new System.Drawing.Point(241, 517);
            this.btnContinue.Name = "btnContinue";
            this.btnContinue.Progress = 0;
            this.btnContinue.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnContinue.Size = new System.Drawing.Size(334, 37);
            this.btnContinue.TabIndex = 15;
            this.btnContinue.Text = "Just continue";
            this.btnContinue.UseVisualStyleBackColor = false;
            this.btnContinue.Click += new System.EventHandler(this.BtnContinue_Click);
            // 
            // label3
            // 
            this.label3.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label3.Location = new System.Drawing.Point(118, 75);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(457, 25);
            this.label3.TabIndex = 16;
            this.label3.Text = "Macro Deck will try to continue...";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // autoContinueTimer
            // 
            this.autoContinueTimer.Enabled = true;
            this.autoContinueTimer.Interval = 1000;
            // 
            // CrashReportDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(591, 568);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.btnContinue);
            this.Controls.Add(this.btnSendCrashReport);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.comment);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.crashReport);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.pictureBox2);
            this.Name = "CrashReportDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "CrashReportDialog";
            this.Controls.SetChildIndex(this.pictureBox2, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.crashReport, 0);
            this.Controls.SetChildIndex(this.label4, 0);
            this.Controls.SetChildIndex(this.comment, 0);
            this.Controls.SetChildIndex(this.label5, 0);
            this.Controls.SetChildIndex(this.label6, 0);
            this.Controls.SetChildIndex(this.label7, 0);
            this.Controls.SetChildIndex(this.btnSendCrashReport, 0);
            this.Controls.SetChildIndex(this.btnContinue, 0);
            this.Controls.SetChildIndex(this.label3, 0);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox pictureBox2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox crashReport;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox comment;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private CustomControls.ButtonPrimary btnSendCrashReport;
        private CustomControls.ButtonPrimary btnContinue;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Timer autoContinueTimer;
    }
}