
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class DeviceConfigurator
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
            this.lblBrightness = new System.Windows.Forms.Label();
            this.brightness = new System.Windows.Forms.TrackBar();
            ((System.ComponentModel.ISupportInitialize)(this.brightness)).BeginInit();
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
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Location = new System.Drawing.Point(227, 72);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 2;
            this.btnOk.Text = "Ok";
            this.btnOk.UseVisualStyleBackColor = false;
            // 
            // lblBrightness
            // 
            this.lblBrightness.AutoSize = true;
            this.lblBrightness.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblBrightness.Location = new System.Drawing.Point(4, 21);
            this.lblBrightness.Name = "lblBrightness";
            this.lblBrightness.Size = new System.Drawing.Size(82, 19);
            this.lblBrightness.TabIndex = 3;
            this.lblBrightness.Text = "Brightness";
            // 
            // brightness
            // 
            this.brightness.Location = new System.Drawing.Point(105, 21);
            this.brightness.Minimum = 1;
            this.brightness.Name = "brightness";
            this.brightness.Size = new System.Drawing.Size(156, 45);
            this.brightness.TabIndex = 4;
            this.brightness.TickStyle = System.Windows.Forms.TickStyle.None;
            this.brightness.Value = 10;
            this.brightness.Scroll += new System.EventHandler(this.Brightness_Scroll);
            // 
            // DeviceConfigurator
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(306, 103);
            this.Controls.Add(this.brightness);
            this.Controls.Add(this.lblBrightness);
            this.Controls.Add(this.btnOk);
            this.Name = "DeviceConfigurator";
            this.Text = "DeviceConfigurator";
            this.Load += new System.EventHandler(this.DeviceConfigurator_Load);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.lblBrightness, 0);
            this.Controls.SetChildIndex(this.brightness, 0);
            ((System.ComponentModel.ISupportInitialize)(this.brightness)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private CustomControls.ButtonPrimary btnOk;
        private System.Windows.Forms.Label lblBrightness;
        private System.Windows.Forms.TrackBar brightness;
    }
}