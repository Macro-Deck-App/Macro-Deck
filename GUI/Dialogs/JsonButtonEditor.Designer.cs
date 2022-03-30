
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class JsonButtonEditor
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
            this.jsonTextBox = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.btnApply = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.SuspendLayout();
            // 
            // jsonTextBox
            // 
            this.jsonTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.jsonTextBox.AutoScroll = true;
            this.jsonTextBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.jsonTextBox.Cursor = System.Windows.Forms.Cursors.Hand;
            this.jsonTextBox.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.jsonTextBox.Icon = null;
            this.jsonTextBox.Location = new System.Drawing.Point(10, 46);
            this.jsonTextBox.MaxCharacters = 32767;
            this.jsonTextBox.Multiline = true;
            this.jsonTextBox.Name = "jsonTextBox";
            this.jsonTextBox.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.jsonTextBox.PasswordChar = false;
            this.jsonTextBox.PlaceHolderColor = System.Drawing.Color.Gray;
            this.jsonTextBox.PlaceHolderText = "";
            this.jsonTextBox.ReadOnly = false;
            this.jsonTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.jsonTextBox.SelectionStart = 0;
            this.jsonTextBox.Size = new System.Drawing.Size(1050, 493);
            this.jsonTextBox.TabIndex = 2;
            this.jsonTextBox.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnApply
            // 
            this.btnApply.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnApply.BorderRadius = 8;
            this.btnApply.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnApply.FlatAppearance.BorderSize = 0;
            this.btnApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnApply.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnApply.ForeColor = System.Drawing.Color.White;
            this.btnApply.HoverColor = System.Drawing.Color.Empty;
            this.btnApply.Icon = null;
            this.btnApply.Location = new System.Drawing.Point(960, 545);
            this.btnApply.Name = "btnApply";
            this.btnApply.Progress = 0;
            this.btnApply.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnApply.Size = new System.Drawing.Size(100, 25);
            this.btnApply.TabIndex = 3;
            this.btnApply.Text = "Apply";
            this.btnApply.UseMnemonic = false;
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.UseWindowsAccentColor = true;
            this.btnApply.Click += new System.EventHandler(this.BtnApply_Click);
            // 
            // JsonButtonEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1070, 577);
            this.Controls.Add(this.btnApply);
            this.Controls.Add(this.jsonTextBox);
            this.Location = new System.Drawing.Point(0, 0);
            this.Name = "JsonButtonEditor";
            this.Text = "JsonButtonEditor";
            this.Controls.SetChildIndex(this.jsonTextBox, 0);
            this.Controls.SetChildIndex(this.btnApply, 0);
            this.ResumeLayout(false);

        }

        #endregion

        private CustomControls.RoundedTextBox jsonTextBox;
        private CustomControls.ButtonPrimary btnApply;
    }
}