
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class FeedbackDialog
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
            this.btnSendFeedback = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.feedback = new System.Windows.Forms.TextBox();
            this.email = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnSendFeedback
            // 
            this.btnSendFeedback.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnSendFeedback.FlatAppearance.BorderSize = 0;
            this.btnSendFeedback.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSendFeedback.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnSendFeedback.ForeColor = System.Drawing.Color.White;
            this.btnSendFeedback.Location = new System.Drawing.Point(490, 508);
            this.btnSendFeedback.Name = "btnSendFeedback";
            this.btnSendFeedback.Size = new System.Drawing.Size(163, 23);
            this.btnSendFeedback.TabIndex = 3;
            this.btnSendFeedback.Text = "Send feedback";
            this.btnSendFeedback.UseMnemonic = false;
            this.btnSendFeedback.UseVisualStyleBackColor = false;
            this.btnSendFeedback.Click += new System.EventHandler(this.BtnSendFeedback_Click);
            // 
            // feedback
            // 
            this.feedback.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.feedback.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.feedback.ForeColor = System.Drawing.Color.White;
            this.feedback.Location = new System.Drawing.Point(12, 123);
            this.feedback.Multiline = true;
            this.feedback.Name = "feedback";
            this.feedback.Size = new System.Drawing.Size(641, 379);
            this.feedback.TabIndex = 4;
            // 
            // email
            // 
            this.email.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.email.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.email.ForeColor = System.Drawing.Color.White;
            this.email.Location = new System.Drawing.Point(12, 50);
            this.email.Name = "email";
            this.email.Size = new System.Drawing.Size(425, 23);
            this.email.TabIndex = 5;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.Location = new System.Drawing.Point(12, 28);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(206, 19);
            this.label1.TabIndex = 6;
            this.label1.Text = "E-Mail for contact (optional)";
            this.label1.UseMnemonic = false;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label3.Location = new System.Drawing.Point(12, 101);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(74, 19);
            this.label3.TabIndex = 7;
            this.label3.Text = "Feedback";
            this.label3.UseMnemonic = false;
            // 
            // FeedbackDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(665, 541);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.email);
            this.Controls.Add(this.feedback);
            this.Controls.Add(this.btnSendFeedback);
            this.Name = "FeedbackDialog";
            this.Text = "FeedbackDialog";
            this.Load += new System.EventHandler(this.FeedbackDialog_Load);
            this.Controls.SetChildIndex(this.btnSendFeedback, 0);
            this.Controls.SetChildIndex(this.feedback, 0);
            this.Controls.SetChildIndex(this.email, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.label3, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private CustomControls.ButtonPrimary btnSendFeedback;
        private System.Windows.Forms.TextBox feedback;
        private System.Windows.Forms.TextBox email;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label3;
    }
}